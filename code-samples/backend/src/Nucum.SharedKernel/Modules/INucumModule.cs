// File summary: Backend module contract + manifest. Every module (Activity,
// Routines, Plans…) implements INucumModule and the API host discovers them
// by assembly scan at startup. Lives in SharedKernel.Modules. Concepts:
// modular monolith, dependency inversion at the host, plugin-ish discovery.
namespace Nucum.SharedKernel.Modules;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public sealed record ModuleManifest(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> PlanModuleTypes);

// CONCEPT: Modular monolith seam.
// One process, one deploy — but with hard internal walls. Each module is
// a separate project that only exposes its *.Contracts to peers. The host
// (Nucum.Api) composes them via this interface; the host does NOT know
// what any single module does.
// WHY: I want to show (a) that boundaries can be enforced without
// microservices, and (b) that I can split later if I need to without a
// rewrite. The architecture test proves (a) at build time.
// GOTCHA: If a module needs data owned by another module, it goes through
// Contracts (sync read) or an integration event (async). No cross-module
// DbContext access, no shared tables.
// INTERVIEW: "Monolith vs microservice?" → start with modular monolith
// unless you have a specific scaling, team, or deployment reason to split.
// → Roadmap: Ay 1 · Faz 1 — Clean Architecture + Proje 1 İskeleti
public interface INucumModule
{
    ModuleManifest Manifest { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
    Task MigrateAsync(IServiceProvider services, CancellationToken ct);
}
