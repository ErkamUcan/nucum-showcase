// File summary: Phase 6.5 architecture rules for the inside of each module:
// (1) modules reference peers only through *.Contracts assemblies,
// (2) endpoints never touch a DbContext or the Infrastructure layer,
// (3) endpoints inject nothing but IDispatcher (endpoint → command/query →
//     handler), and only Endpoints/ maps routes,
// (4) command/query handlers live in the module's Application namespace.
// Lives in Nucum.ArchitectureTests. See ModuleBoundaryTests for the older
// namespace rules.
namespace Nucum.ArchitectureTests;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Nucum.SharedKernel.Messaging;
using Shouldly;
using Xunit;

// CONCEPT: Layer rules as tests (fitness functions).
// ARCHITECTURE §4.1 says Domain / Application / Infrastructure / Endpoints.
// A folder name alone does not stop anyone from injecting a DbContext into
// an endpoint "just this once"; these tests do.
// WHY: Reflection over compiled code catches lambdas too — a minimal-API
// lambda compiles to a method on a hidden nested class (<>c), and its
// parameters are exactly what the endpoint asks DI for.
// GOTCHA: NetArchTest matches namespace PREFIXES, so "Nucum.Modules.Plans"
// also matches "Nucum.Modules.Plans.Contracts". For the peer rule I check
// referenced assemblies instead — the compiler only keeps a reference when
// code really uses it.
// INTERVIEW: "How do you stop a layered architecture from eroding?" → make
// the rules executable and run them on every build.
// → Roadmap: Ay 3 · Faz 3 — Fitness Functions & Mimari Testler
public class LayerRulesTests
{
    public static readonly Assembly[] Modules =
    [
        typeof(Nucum.Modules.Activity.ActivityModule).Assembly,
        typeof(Nucum.Modules.Automation.AutomationModule).Assembly,
        typeof(Nucum.Modules.Collections.CollectionsModule).Assembly,
        typeof(Nucum.Modules.Extensions.PasteParser.PasteParserModule).Assembly,
        typeof(Nucum.Modules.Extensions.PromptBuilder.PromptBuilderModule).Assembly,
        typeof(Nucum.Modules.Focus.FocusModule).Assembly,
        typeof(Nucum.Modules.Packs.PacksModule).Assembly,
        typeof(Nucum.Modules.Plans.PlansModule).Assembly,
        typeof(Nucum.Modules.Routines.RoutinesModule).Assembly,
        typeof(Nucum.Modules.Settings.SettingsModule).Assembly,
        typeof(Nucum.Modules.Today.TodayModule).Assembly,
        typeof(Nucum.Modules.Tracking.TrackingModule).Assembly,
        typeof(Nucum.Modules.Gamification.GamificationModule).Assembly,
        typeof(Nucum.Modules.Insights.InsightsModule).Assembly,
        typeof(Nucum.Modules.Extensions.InterviewDrill.InterviewDrillModule).Assembly,
        typeof(Nucum.Modules.Extensions.PlanDrift.PlanDriftModule).Assembly,
        typeof(Nucum.Modules.Sync.SyncModule).Assembly,
        typeof(Nucum.Modules.Journal.JournalModule).Assembly,
        typeof(Nucum.Modules.Prayer.PrayerModule).Assembly,
    ];

    // Every module goes endpoint → dispatcher since Phase 6.5.
    public static readonly Assembly[] Dispatched = Modules;

    public static TheoryData<string> AllModules => new(Modules.Select(Name));
    public static TheoryData<string> DispatchedModules => new(Dispatched.Select(Name));

    private static string Name(Assembly a) => a.GetName().Name!;
    private static Assembly ByName(string name) => Modules.Single(a => Name(a) == name);

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Module_references_peers_only_through_Contracts(string module)
    {
        var bad = ByName(module).GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith("Nucum.Modules.", StringComparison.Ordinal))
            .Where(n => !n.EndsWith(".Contracts", StringComparison.Ordinal))
            .ToList();
        bad.ShouldBeEmpty($"{module} must reach other modules only through *.Contracts.");
    }

    [Theory]
    [MemberData(nameof(DispatchedModules))]
    public void Endpoints_do_not_depend_on_EF_Core_or_Infrastructure(string module)
    {
        var result = Types.InAssembly(ByName(module))
            .That().ResideInNamespace($"{module}.Endpoints")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", $"{module}.Infrastructure")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join("; ", result.FailingTypeNames ?? []));
    }

    // IDispatcher is the only service an endpoint may ask for. The other two
    // are plumbing: the route builder passed to Map(), and the async state
    // machine the compiler generates for every async lambda.
    private static readonly Type[] Allowed =
    [
        typeof(IDispatcher),
        typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder),
        typeof(System.Runtime.CompilerServices.IAsyncStateMachine),
    ];

    [Theory]
    [MemberData(nameof(DispatchedModules))]
    public void Endpoints_inject_only_the_dispatcher(string module)
    {
        var offenders = EndpointMethods(ByName(module), $"{module}.Endpoints")
            .SelectMany(m => m.GetParameters().Select(p => (Method: m, p.ParameterType)))
            .Where(x => x.ParameterType.IsInterface && !Allowed.Contains(x.ParameterType)
                        || typeof(DbContext).IsAssignableFrom(x.ParameterType))
            .Select(x => $"{x.Method.DeclaringType!.Name}.{x.Method.Name}({x.ParameterType.Name})")
            .ToList();
        offenders.ShouldBeEmpty($"{module} endpoints must go through IDispatcher.");
    }

    [Theory]
    [MemberData(nameof(DispatchedModules))]
    public void Only_Endpoints_maps_routes(string module)
    {
        // MapGet/MapPost/… live in EndpointRouteBuilderExtensions; MapGroup
        // in the Routing one. Nobody outside Endpoints/ should call them.
        var result = Types.InAssembly(ByName(module))
            .That().DoNotResideInNamespace($"{module}.Endpoints")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions",
                "Microsoft.AspNetCore.Routing.RouteGroupBuilder")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join("; ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Handlers_live_in_Application(string module)
    {
        var openHandlers = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>) };
        var misplaced = ByName(module).GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && openHandlers.Contains(i.GetGenericTypeDefinition())))
            .Where(t => t.Namespace is null || !t.Namespace.StartsWith($"{module}.Application", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();
        misplaced.ShouldBeEmpty($"{module} handlers belong in {module}.Application.");
    }

    // Route handlers in the Endpoints namespace: every lambda body (the
    // compiler names them "<Map>b__0_1" inside hidden closure classes) and
    // every method of the static *Endpoints class. Request body records
    // (OrderBody…) are skipped — their property setters are not handlers.
    private static IEnumerable<MethodInfo> EndpointMethods(Assembly asm, string ns)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return asm.GetTypes()
            .Where(t => t.Namespace == ns)
            .SelectMany(t => t.GetMethods(all))
            .Where(m => m.Name.Contains(">b__", StringComparison.Ordinal)
                        || m.DeclaringType is { IsAbstract: true, IsSealed: true } && !m.IsSpecialName);
    }
}
