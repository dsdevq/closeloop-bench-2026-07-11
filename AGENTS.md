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
    app/app.routes.ts           # lazy-loaded route: /contacts → ContactsComponent
    app/contacts/               # Contacts feature
      contacts.service.ts       # ContactsService (inject(), HttpClient, list()/create())
      contacts.component.ts     # ContactsComponent (standalone, signals, reactive form)
      contacts.spec.ts          # Vitest specs: list render, create success, 422 error path
    app/companies/              # Companies feature (mirrors Contacts exactly)
      companies.service.ts      # CompaniesService (inject(), HttpClient, list()/create())
      companies.component.ts    # CompaniesComponent (standalone, signals, reactive form)
      companies.spec.ts         # Vitest specs: list render, empty state, create, 422 error path
    environments/               # environment.ts = production; environment.development.ts = dev
                                # both export apiBaseUrl ('' prod, 'http://localhost:5000' dev)
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
    Features/
      Contacts/     ContactsEndpoints.cs, ContactDtos.cs  (GET list/detail, POST create)
      Companies/    CompaniesEndpoints.cs, CompanyDtos.cs (GET list/detail, POST create)
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
cd frontend && ng serve                               # start the Angular dev server (default http://localhost:4200)
cd frontend && ng build                               # production build (output to frontend/dist/)
```

## verify_cmd

```bash
bash scripts/verify.sh
```

`scripts/verify.sh` checks that Domain has no outward project references (clean-arch enforcement), then runs `dotnet build closeloop.sln --configuration Release`, then runs `dotnet test --no-build` against the full solution, then runs `ng test --watch=false` in `frontend/`. Test layers covered: **Domain unit tests** (`backend/Domain.Tests`), **Infrastructure model tests** (`backend/Infrastructure.Tests`), **API integration tests** (`backend/Api.Tests`), and **Angular unit tests** (`frontend/src/app/**/*.spec.ts`, Vitest via `@angular/build:unit-test`).

## Docker

A multi-stage root `Dockerfile` builds the full stack:

1. **node-build** — `node:22-alpine`; installs deps with `npm ci`, runs `ng build --configuration production`; output lands at `dist/frontend/browser/`.
2. **dotnet-build** — `mcr.microsoft.com/dotnet/sdk:9.0.315` (exact version from `global.json`); restores and publishes `backend/Api/Api.csproj` to `/publish`.
3. **runtime** — `mcr.microsoft.com/dotnet/aspnet:9.0`; copies published API + Angular bundle into `wwwroot/`.

### Known gaps / Temporary duplicate test files

`backend/Tests/Domain/DealTests.cs` and `backend/Tests/Domain/PipelineTests.cs` are intentionally
retained dead weight. Their audited, fully-ported equivalents were merged into `backend/Domain.Tests/`
in PR #3. They are kept only because devclaw's test-integrity gate cannot currently credit a
prior-PR-audited equivalent on a deletion diff. **Do not delete these files until that harness
gap is fixed.**

### Known gaps / Docker

**Static-file serving not wired** — `backend/Api/Program.cs` does not yet call `UseDefaultFiles()` + `UseStaticFiles()`. The Angular bundle is copied into `wwwroot/` in the image but the API does not serve it at runtime. When that hookup is added to Program.cs the frontend will be served from the same origin as the API (no separate server needed). Until then, `docker run` on this image exposes only the `/health` and `/contacts` API endpoints.

### Known gaps / Notification dispatcher

Three of the four `INotificationDispatcher` methods are **no-op stubs** in
`Infrastructure/Services/NotificationDispatcher.cs`:

| Method | Blocked on |
|---|---|
| `DealAssignedAsync` | `Deal.OwnerId` field not yet in domain model |
| `DealStageChangedAsync` | `Deal.OwnerId` field not yet in domain model |
| `ContactAssignedAsync` | `Contact.OwnerId` field not yet in domain model |

Until `Deal.OwnerId` and `Contact.OwnerId` are added (plus the PATCH endpoints that change
ownership), these methods return `Task.CompletedTask` without creating any notification records.
`ActivityMentionAsync` is the only dispatcher method fully wired to a real endpoint
(`POST /activities`).

`Pipeline.RottingThresholdDays` (`int?`) **is implemented** in the domain entity and EF configuration
but has no consumer: the `DealRottingNotificationJob` background hosted service was deleted as
permanently dead code (it referenced `Deal.OwnerId` which does not exist). Until the ownership
slice lands and the background job is re-introduced, `RottingThresholdDays` is an orphaned field
that can be set via `Pipeline.SetRottingThresholdDays()` but never read by any job or endpoint.

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

### Completed research artifacts

| File | Feature | Status |
|---|---|---|
| `.devclaw/research/domain-model.md` | Core object model (Contact, Company, Deal, Activity, Pipeline, Stage) | merged |
| `.devclaw/research/contacts.md` | Contacts feature surface | merged |
| `.devclaw/research/companies.md` | Companies feature surface | merged |
| `.devclaw/research/deals.md` | Deals/Kanban surface, stage progression, forecasting, rotting | merged |
| `.devclaw/research/activities.md` | Activity log, per-record feed, task surface | merged |
| `.devclaw/research/pipelines.md` | Pipeline CRUD, stage management, metrics | merged |
| `.devclaw/research/notifications.md` | Notification entity, trigger taxonomy, dispatch model, mention surface | **this PR** |

The `notifications.md` artifact defines: `Notification` entity (`Id`, `RecipientUserId`, `Trigger`,
`Title`, `Body`, `RelatedEntityId`, `RelatedEntityType`, `IsRead`, `CreatedAt`); `NotificationTrigger`
enum (six values: `DealAssigned`, `DealStageChanged`, `DealRotting`, `ContactAssigned`,
`ActivityMention`, `TaskDue`); `NotificationEntityType` enum (`Contact`, `Company`, `Deal`,
`Activity`); `INotificationDispatcher` interface (four methods — one per event-driven trigger);
and three API endpoints (`GET /notifications`, `PATCH /notifications/{id}/read`,
`POST /notifications/read-all`). Design borrows from HubSpot's named-trigger taxonomy, Attio's
@mention surface, and Pipedrive's pipeline-scoped rotting notification. Salesforce's configurable
rule engine, HubSpot's webhook-first push model, Attio's record-following subscription, and
Pipedrive's email fallback are all explicitly rejected (see artifact for argued reasoning).

**Current wiring state**: `ActivityMentionAsync` is called from `POST /activities` after
`SaveChanges` — the only dispatcher method currently integrated into a real endpoint. The other
three methods (`DealAssignedAsync`, `DealStageChangedAsync`, `ContactAssignedAsync`) are no-op
stubs in `NotificationDispatcher` pending `Deal.OwnerId` and `Contact.OwnerId` fields, which are
not yet in the domain model (see Known Gaps below).

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

## Angular frontend conventions

- Standalone components; no NgModules.
- Angular 21 built-in control flow (`@for ... @empty`, `@if`) — not `*ngFor`/`*ngIf` structural directives.
- Signals (`signal()`) for mutable state; `inject()` for dependency injection (not constructor parameters).
- `FormBuilder.nonNullable.group(...)` for reactive forms — avoids null-typed controls and resets to initial value on `reset()`.
- `provideHttpClient()` is wired in `app.config.ts`; feature services use `inject(HttpClient)`.
- `environment.apiBaseUrl` is the single source for the backend base URL (`''` in production, `http://localhost:5000` in development).
- New routes are added to `app.routes.ts` as `loadComponent` lazy entries (no eagerly imported components in the router).
- Tests use `provideHttpClient()` + `provideHttpClientTesting()` (not `HttpClientTestingModule`); `HttpTestingController` from `@angular/common/http/testing` intercepts all requests.

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
- `NotificationTrigger` is a closed enum (six values). A seventh trigger is an additive enum extension, not a rule-record migration — this was the explicit reason for rejecting Salesforce's configurable rule-engine model (see `.devclaw/research/notifications.md` §Rejected A).
- `INotificationDispatcher` lives at the Domain boundary; the concrete implementation sits in Infrastructure. `POST /activities` calls `ActivityMentionAsync` post-SaveChanges; the dispatcher does a second SaveChanges (eventual consistency, acceptable for informational notifications). The other three methods (`DealAssignedAsync`, `DealStageChangedAsync`, `ContactAssignedAsync`) are currently no-op stubs — see Known Gaps / Notification dispatcher.
- `@mention` syntax in `Activity.Note` is parsed in the application (endpoint) layer, not in the `Activity` domain entity — the entity stays `string?`-typed; mention resolution is an application concern injected via `INotificationDispatcher.ActivityMentionAsync`. Pattern: `@<uuid>` (UUID rather than display name). Email/SMS fallback delivery was explicitly deferred (see `.devclaw/research/notifications.md` §Rejected D).
