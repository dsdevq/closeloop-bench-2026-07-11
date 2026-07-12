# AGENTS.md — closeloop-bench

## Stack

- **.NET 9** (pinned via `global.json` at repo root — use SDK 9.0.315)
- Clean-architecture .NET solution
- **Angular 21** frontend (`frontend/`) — standalone components, Vitest test runner via `@angular/build:unit-test`

## Repository layout

```
closeloop.sln                   # solution file (repo root)
global.json                     # pins SDK to net9.0
frontend/                       # Angular 21 SPA
  angular.json                  # workspace config; fileReplacements in development config only
  src/
    app/app.ts                  # root standalone component (imports RouterOutlet)
    app/app.html                # <router-outlet /> only — no welcome-page content
    app/app.spec.ts             # Vitest spec; tests: create + router-outlet presence
    environments/               # environment.ts = production; environment.development.ts = dev
backend/
  Domain/           Domain.csproj           classlib  — no outward project refs
    Common/         Entity.cs               abstract base class (Id: Guid, protected init)
    Entities/       Company.cs              domain aggregates (Company, Contact, Pipeline,
                                            PipelineStage, Deal, Activity, ActivityType)
  Domain.Tests/     Domain.Tests.csproj     xUnit tests for Domain layer
    Entities/       CompanyTests.cs         entity invariant tests
  Infrastructure/   Infrastructure.csproj   classlib  — refs Domain + Npgsql.EFCore.PG 9.x
    CrmDbContext.cs                         EF Core DbContext (6 DbSets)
    CrmDbContextFactory.cs                  IDesignTimeDbContextFactory — env-sourced conn string for migrations
    Configurations/ IEntityTypeConfiguration<T> per aggregate (applied via ApplyConfigurationsFromAssembly)
    Migrations/     EF Core migrations (InitialCreate) — generated, not executed at build time
  Infrastructure.Tests/ Infrastructure.Tests.csproj  xUnit — refs Infrastructure + EF Core InMemory
    CrmDbContextModelTests.cs              exercises OnModelCreating; asserts FK/cascade semantics
  Api/              Api.csproj              web app   — refs Infrastructure
        Program.cs  minimal API host + CrmDbContext DI registration (Npgsql)
```

## Clean-architecture layering

```
Api → Infrastructure → Domain
```

**Domain must never reference Infrastructure or Api.** Enforce via `dotnet list backend/Domain/Domain.csproj reference` — should always return "no Project to Project references".

## Build & verify

```bash
dotnet build closeloop.sln --configuration Release   # full solution build
dotnet test closeloop.sln --configuration Release    # build + run unit tests
cd frontend && ng test --watch=false                  # Angular unit tests (Vitest)
```

## verify_cmd

```bash
bash scripts/verify.sh
```

`scripts/verify.sh` checks that Domain has no outward project references (clean-arch enforcement), then runs `dotnet build closeloop.sln --configuration Release`, then runs `dotnet test --no-build` against the full solution, then runs `ng test --watch=false` in `frontend/`. Test layers covered: **Domain unit tests** (`backend/Domain.Tests`), **Infrastructure model tests** (`backend/Infrastructure.Tests`), **API integration tests** (`backend/Api.Tests`), and **Angular unit tests** (`frontend/src/app/app.spec.ts`, Vitest via `@angular/build:unit-test`).

## Research citation convention

Feature research artifacts live under `.devclaw/research/<feature>.md`. Every such file must
follow the template defined in **`.devclaw/research/README.md`**, which specifies three required
sections:

- `## Sources consulted` — which reference CRMs (Salesforce, HubSpot, Pipedrive, Attio, Zoho)
  were examined and how.
- `## Borrowed` — what specific pattern/design was adopted and from which CRM.
- `## Rejected & why` — alternatives considered and the argued reason for not using them.

When creating a new research artifact, copy the section headings from that README verbatim and fill
them in. Do not omit or rename a section.

## Domain entity conventions

All domain entities extend `Domain.Common.Entity` which provides `Id` (Guid, `protected init`).
Entities use a **private constructor + static `Create` factory** pattern to enforce invariants at construction time.
`Domain.Tests` is an xUnit project referencing only Domain — no Infrastructure or Api.
`ImplicitUsings` does not pull in Xunit; add `using Xunit;` explicitly in every test file.

## EF Core migrations

To add a new migration, install the global tool once (`dotnet tool install --global dotnet-ef --version 9.*`), then from the repo root:

```bash
dotnet ef migrations add <Name> --project backend/Infrastructure/Infrastructure.csproj --output-dir Migrations
```

`CrmDbContextFactory` provides the design-time context without a live DB. Connection string falls back to env var `ConnectionStrings__DefaultConnection` (double-underscore = colon separator in env vars), then to a local placeholder if absent.

To apply migrations against a running Postgres instance:

```bash
dotnet ef database update --project backend/Infrastructure/Infrastructure.csproj
```

## Local development — PostgreSQL

A `docker-compose.yml` at repo root starts a PostgreSQL 16 container (`postgres` service, port 5432).
Credentials are read from environment variables; copy `.env.example` to `.env` and fill in values before running:

```bash
cp .env.example .env
docker compose up -d
```

Named volume `postgres_data` persists data across restarts.

## API feature conventions

Minimal API endpoints live under `backend/Api/Features/<Feature>/`:
- `<Feature>Endpoints.cs` — static class with `MapXxxEndpoints(this IEndpointRouteBuilder)` extension; registers an `app.MapGroup("/<feature>")` and maps handlers.
- `<Feature>Dtos.cs` — sealed records for request/response (never expose EF entities over the wire).
- Wire the endpoint group in `Program.cs` with `app.MapXxxEndpoints()`.

`Api.Tests` project uses `WebApplicationFactory<Program>` + `Microsoft.AspNetCore.Mvc.Testing` with EF Core InMemory. The critical DI override pattern — required because `IDbContextOptionsConfiguration<T>` (EF Core 9's hook for the `optionsAction`) is registered with `Add`, not `TryAdd`, so it must be removed explicitly before substituting InMemory:

```csharp
builder.ConfigureTestServices(services =>   // ConfigureTestServices runs AFTER Program.cs
{
    var optConfigType = typeof(IDbContextOptionsConfiguration<CrmDbContext>);
    foreach (var d in services.Where(d => d.ServiceType == optConfigType).ToList())
        services.Remove(d);
    foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<CrmDbContext>)).ToList())
        services.Remove(d);
    services.AddDbContext<CrmDbContext>(o => o.UseInMemoryDatabase("test_" + Guid.NewGuid()));
});
```

Also: `Results.ValidationProblem` must receive `statusCode: StatusCodes.Status422UnprocessableEntity` explicitly — `HttpValidationProblemDetails` sets `Status = 400` in its constructor and `??=` does not override a non-null value, so omitting `statusCode` silently returns 400.

## Key decisions

- `global.json` pins to `9.0.315` with `rollForward: latestPatch` so `dotnet` always resolves to .NET 9 even though .NET 10 is also installed.
- Api project uses `Microsoft.NET.Sdk.Web` so ASP.NET Core meta-package is available without an explicit PackageReference.
- `Program.cs` exposes `public partial class Program {}` so future integration-test projects can reference the entry-point assembly.
- Marker types in Domain and Infrastructure keep the classlibs non-empty and compilable from day one.
- `Company` (not `Organization`) aligns with HubSpot/Attio/Zoho terminology and target-user mental model (see `.devclaw/research/domain-model.md` §Rejected F).
- `Activity` polymorphic target uses three nullable FKs (`ContactId`, `CompanyId`, `DealId`) with an exactly-one-anchor invariant enforced in `Create` — not a discriminated-pair (TargetType/TargetId) as described in the research doc. The research doc's design is noted but overridden by the implementation decision to mirror Deal's FK pattern.
- `ActivityType` is a closed enum (Note, Call, Email, Meeting, Task, StageChange). The research doc favours an open string registry; the closed enum was chosen for the domain layer to keep the model strict. Assumption: StageChange added beyond the research doc's five seed values because pipeline stage transitions are the primary audit event in a deal-centric CRM.
- `CrmDbContext` uses `Npgsql.EntityFrameworkCore.PostgreSQL` version `9.0.*` (aligned with net9.0 target; v10 targets net10). Connection string sourced from `ConnectionStrings:DefaultConnection` in configuration/environment — never hardcoded.
- EF Core `IEntityTypeConfiguration<T>` classes live in `Infrastructure/Configurations/`, applied via `ApplyConfigurationsFromAssembly`. Domain entities carry no data annotations (persistence-ignorant).
- `Microsoft.EntityFrameworkCore.Design` in Infrastructure.csproj is `PrivateAssets=all` (design-time only). Explicit `Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.Relational` references pin all EF Core packages to the same `9.0.*` version, preventing MSB3277 assembly-conflict warnings from Npgsql pulling in an older `[9.0.1, 10.0.0)` dependency.
- `Pipeline._stages` is a `private readonly List<PipelineStage>` backing field. EF Core is told to use it via `Navigation(p => p.Stages).HasField("_stages").UsePropertyAccessMode(PropertyAccessMode.Field)` because the `Stages` getter returns a computed `IReadOnlyList` (OrderBy + ToList), not the field itself.
- Activity anchor FKs (ContactId/CompanyId/DealId) use `DeleteBehavior.Restrict` — not SetNull — because nulling the sole anchor would silently violate the exactly-one-anchor domain invariant that Activity.Create enforces.
- PipelineStage→Pipeline uses `DeleteBehavior.Cascade` (deleting a pipeline removes its stages). Deal→Pipeline and Deal→PipelineStage use `DeleteBehavior.Restrict` (cannot delete a pipeline or stage that has live deals).
