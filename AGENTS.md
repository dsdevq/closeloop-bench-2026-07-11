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
    Migrations/     EF Core migrations (InitialCreate, AddNotifications, AddOwnerAndDealFields) — generated, not executed at build time
  Infrastructure.Tests/ Infrastructure.Tests.csproj  xUnit — refs Infrastructure + EF Core InMemory
    CrmDbContextModelTests.cs              exercises OnModelCreating; asserts FK/cascade semantics
  Api/              Api.csproj              web app   — refs Infrastructure
        Program.cs  minimal API host + CrmDbContext DI registration (Npgsql)
    Features/
      Contacts/     ContactsEndpoints.cs, ContactDtos.cs  (GET list/detail, POST create) — includes OwnerId
      Companies/    CompaniesEndpoints.cs, CompanyDtos.cs (GET list/detail, POST create)
      Deals/        DealsEndpoints.cs, DealDtos.cs        (GET list/detail, POST create)
      Pipelines/    PipelinesEndpoints.cs, PipelineDtos.cs (GET list/detail, POST create with stages)
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

The CI workflow (`.github/workflows/ci.yml`) adds a second `docker-integration` job that builds the production Docker image and runs a smoke test against a real Postgres service using `DATABASE_URL` — this validates the full end-to-end container path and the `DATABASE_URL` precedence branch that unit tests cannot exercise.

## Docker

A multi-stage root `Dockerfile` builds the full stack:

1. **node-build** — `node:22-alpine`; installs deps with `npm ci`, runs `ng build --configuration production`; output lands at `dist/frontend/browser/`.
2. **dotnet-build** — `mcr.microsoft.com/dotnet/sdk:9.0.315` (exact version from `global.json`); restores and publishes `backend/Api/Api.csproj` to `/publish`.
3. **runtime** — `mcr.microsoft.com/dotnet/aspnet:9.0`; copies published API + Angular bundle into `wwwroot/`.

### MVP scope decisions — named deferrals

**Angular UI for Deals, Pipelines, Activities, and Notifications is intentionally absent.**
The backend API endpoints for all four features are shipped (REST handlers, DTOs, EF Core
persistence, notification dispatch), but no Angular components, routes, or services exist for
them yet. `frontend/src/app/` contains only `contacts/` and `companies/` components. This is a
documented MVP deferral, not an oversight — the UI work for these features is a separate,
larger follow-up. Do not add Angular UI for these features without a dedicated scope decision.

**Hardcoded `ownerId` in `companies.component.ts` and `contacts.component.ts`** — both POST
forms send `ownerId: '00000000-0000-0000-0000-000000000001'` as a hardcoded placeholder because
no authentication or owner-resolution mechanism exists yet. This is a temporary MVP stub; the
correct fix is to supply a real owner ID from the authenticated session once auth is added.
See the auth note below.

**No authentication or identity layer exists.** The API is fully unauthenticated. User identity
is not verified server-side — the `ownerId` in contact/company/deal create requests is accepted
as-is from the client body. `GET /notifications` and notification ownership checks also use a
`userId` query parameter supplied by the caller with no verification. **Before building any
user-scoped feature** (notifications inbox, per-user dashboards, assignment rules), an auth
layer must be added to replace the stub `ownerId` approach. Options: ASP.NET Core JWT bearer
middleware, session cookie, or a thin identity proxy in front of the API.

### Intentionally deferred endpoints

`GET /contacts`, `GET /companies`, `GET /activities`, and `GET /deals` are **implemented** as
row-capped lists (max 200 rows, ordered by name/title ascending for contacts/companies/deals, by
`OccurredAt` descending for activities). They accept **no filter, sort, or pagination query
parameters**. All four are scoped deferrals:

- Cursor pagination envelope (`?limit=&after=` / `paging.next.after`) is deferred pending
  PostgreSQL/Npgsql keyset-predicate validation — see Key decisions above.
- Filter + sort query parameters (`?filter=`, `?sort=`) are deferred.
- Inline related-record embedding (primaryCompany on contact detail, primaryContact on company
  detail, sub-resource endpoints) is deferred — see `.devclaw/research/contacts.md` and
  `.devclaw/research/companies.md` for per-section status markers.

Before adding any of the above, update this note, the relevant research artifact, and the
`verify_cmd` test gate if new test layers are added.

### Deduplication of legacy Tests project

`backend/Tests/Domain/DealTests.cs` and `backend/Tests/Domain/PipelineTests.cs` were the legacy
duplicate test files. They have been deleted; their tests now live canonically in
`backend/Domain.Tests/Entities/DealTests.cs` and `backend/Domain.Tests/Entities/PipelineTests.cs`.

`backend/Tests/Api/ContactsEndpointsTests.cs` was deleted earlier (its 4 tests were ported into
`backend/Api.Tests/Features/Contacts/ContactsEndpointsTests.cs` with their original method names
before deletion, to satisfy the test-integrity gate).

The `backend/Tests/` directory and its empty `Tests.csproj` have been fully removed from the
repository and from `closeloop.sln`. No source files were ever migrated out — the project was
always empty after the duplicate test files were deleted.

### Docker — deploy gesture

The canonical one-liner to run the full stack:

```bash
docker build -t closeloop .
docker run -p 8080:8080 \
  -e DATABASE_URL="Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<pass>" \
  closeloop
```

`Program.cs` checks `DATABASE_URL` first, then falls back to `ConnectionStrings__DefaultConnection` (the ASP.NET Core double-underscore env-var form). The app auto-applies EF Core migrations on startup via `db.Database.Migrate()` (guarded by `db.Database.IsRelational()` so InMemory tests are unaffected). Static files (Angular bundle) are served from `wwwroot/` via `UseDefaultFiles()` + `UseStaticFiles()`.

See `.devclaw/research/deploy-shape.md` for the borrowed-vs-rejected rationale.

### Notification dispatcher — all four methods wired

All four `INotificationDispatcher` methods in `Infrastructure/Services/NotificationDispatcher.cs`
create real `Notification` rows and call `SaveChangesAsync`:

| Method | Recipient | Trigger | Caller |
|---|---|---|---|
| `DealAssignedAsync` | `deal.OwnerId` (new owner) | `DealAssigned` | `PATCH /deals/{id}/stage` (only when owner changes) |
| `DealStageChangedAsync` | `deal.OwnerId` | `DealStageChanged` — title includes stage name | `PATCH /deals/{id}/stage` (always) |
| `ContactAssignedAsync` | `contact.OwnerId` (new owner) | `ContactAssigned` | `PATCH /contacts/{id}/owner` (only when owner actually changes) |
| `ActivityMentionAsync` | each mentioned user ID | `ActivityMention` | `POST /activities` |

All four dispatcher methods are now wired to real, reachable callers. The key guard: ownership-change
dispatchers (`DealAssignedAsync`, `ContactAssignedAsync`) are only fired when the new owner differs
from the previous owner — re-PATCHing with the same owner is a no-op that returns 200 without
creating a spurious notification.

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
| `.devclaw/research/notifications.md` | Notification entity, trigger taxonomy, dispatch model, mention surface | merged |
| `.devclaw/research/deploy-shape.md` | Deploy shape, Dockerfile multi-stage build, DATABASE_URL precedence | merged |
| `.devclaw/research/search.md` | Cross-object global search, grouped results, TotalHits semantics | merged |

The `notifications.md` artifact defines: `Notification` entity (`Id`, `RecipientUserId`, `Trigger`,
`Title`, `Body`, `RelatedEntityId`, `RelatedEntityType`, `IsRead`, `CreatedAt`); `NotificationTrigger`
enum (four values: `DealAssigned`=0, `DealStageChanged`=1, `ContactAssigned`=3, `ActivityMention`=4;
`DealRotting`=2 and `TaskDue`=5 were removed as unconsumed — explicit int values kept to preserve DB mapping); `NotificationEntityType` enum (`Contact`, `Company`, `Deal`,
`Activity`); `INotificationDispatcher` interface (four methods — one per event-driven trigger);
and three API endpoints (`GET /notifications`, `PATCH /notifications/{id}/read`,
`POST /notifications/read-all`). Design borrows from HubSpot's named-trigger taxonomy, Attio's
@mention surface, and Pipedrive's pipeline-scoped rotting notification. Salesforce's configurable
rule engine, HubSpot's webhook-first push model, Attio's record-following subscription, and
Pipedrive's email fallback are all explicitly rejected (see artifact for argued reasoning).

**Current wiring state**: All four dispatcher methods are integrated. `PATCH /deals/{id}/stage`
fires `DealStageChangedAsync` (always) and `DealAssignedAsync` (only when `req.OwnerId` differs
from the current owner). `PATCH /contacts/{id}/owner` fires `ContactAssignedAsync` (only when
`req.OwnerId != contact.OwnerId` — same-owner PATCH is a no-op). `POST /activities` fires
`ActivityMentionAsync` after `SaveChanges`.

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

### Cross-object search — GET /search?q=

`GET /search?q={term}` searches Contacts (Name, Email), Companies (Name, Domain), and Deals
(Title) with a case-insensitive substring match. Returns up to 10 results per type (ordered by
Name/Title ascending) plus a `TotalHits` integer that is the **sum of uncapped `CountAsync()`
calls per type** — not derived from the capped list lengths. This distinction matters: when 15
contacts match, the response contains 10 contacts in the list and `TotalHits: 15`, giving the
client accurate signal to render "see all 15 contacts". The research artifact
(`.devclaw/research/search.md`) cites the HubSpot `total` field contract as the source of this
design, and names Salesforce SOSL / Pipedrive itemSearch as the cross-object grouping model.

**Do not change TotalHits to use list lengths.** That breaks the documented "see all" affordance
and is regression-tested by `Search_MoreThan10MatchingContacts_TotalHitsReflectsTrueCountNotCappedList`
in `backend/Api.Tests/Features/Search/SearchEndpointsTests.cs`.

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
- `NotificationTrigger` is a closed enum (four values: DealAssigned, DealStageChanged, ContactAssigned, ActivityMention). Values carry explicit integers (0, 1, 3, 4) to preserve DB row mapping after `DealRotting`=2 and `TaskDue`=5 were deleted. A new trigger is an additive enum extension with an explicit next integer — this was the explicit reason for rejecting Salesforce's configurable rule-engine model (see `.devclaw/research/notifications.md` §Rejected A).
- `INotificationDispatcher` lives at the Domain boundary; the concrete implementation sits in Infrastructure. `POST /activities` calls `ActivityMentionAsync` post-SaveChanges; the dispatcher does a second SaveChanges (eventual consistency, acceptable for informational notifications). All four dispatcher methods create real `Notification` rows and are wired to real, reachable callers (see the dispatcher table above: `PATCH /deals/{id}/stage`, `PATCH /contacts/{id}/owner`, `POST /activities`).
- `@mention` syntax in `Activity.Note` is parsed in the application (endpoint) layer, not in the `Activity` domain entity — the entity stays `string?`-typed; mention resolution is an application concern injected via `INotificationDispatcher.ActivityMentionAsync`. Pattern: `@<uuid>` (UUID rather than display name). Email/SMS fallback delivery was explicitly deferred (see `.devclaw/research/notifications.md` §Rejected D).
- **ContactCompanyLink junction → Contact.CompanyId simple FK**: The domain-model research
  (`.devclaw/research/domain-model.md` Borrowed §1) proposed a `ContactCompanyLink` junction
  table with `ContactId`, `CompanyId`, and `IsPrimary` flag (borrowed from HubSpot's many-to-many
  contact-company association). The shipped implementation diverged: `Contact` carries a single
  `CompanyId: Guid?` nullable FK (a one-to-one optional reference). Reason: the junction table
  adds a second DB table, a navigation collection, and multi-company query complexity for a use
  case (one contact, multiple companies) that is rare in the SMB target segment. The simple FK
  covers ≥ 95% of real workflows; upgrading to a junction table with IsPrimary is an explicit
  future migration if product demand emerges. All research docs that referenced
  `ContactCompanyLink.IsPrimary` have been corrected to reflect the shipped FK model.
- **List endpoint row cap (200 rows, cursor pagination deferred)**: `GET /contacts`,
  `GET /companies`, `GET /activities`, and `GET /deals` each cap results at 200 rows (via
  `.Take(200)` in the LINQ query) to guard against unbounded table scans at scale. The HubSpot-style
  cursor pagination envelope (`?limit=&after=` / `paging.next.after`) described in
  `.devclaw/research/contacts.md` Borrowed §3 is DEFERRED: implementing a keyset cursor predicate
  requires validation against the PostgreSQL/Npgsql engine (EF Core InMemory translates OR-based
  keyset predicates in client-side semantics that may differ from Npgsql's SQL translation). Cursor
  pagination must be added with a real PostgreSQL integration test (Testcontainers or docker-compose
  service) before it can be marked shipped.
