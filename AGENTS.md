# AGENTS.md — closeloop-bench

## Stack

- **.NET 9** (pinned via `global.json` at repo root — use SDK 9.0.315)
- Clean-architecture .NET solution

## Repository layout

```
closeloop.sln                   # solution file (repo root)
global.json                     # pins SDK to net9.0
backend/
  Domain/           Domain.csproj           classlib  — no outward project refs
    Common/         Entity.cs               abstract base class (Id: Guid, protected init)
    Entities/       Company.cs              domain aggregates (Company, Contact, Pipeline,
                                            PipelineStage, Deal, Activity, ActivityType)
  Domain.Tests/     Domain.Tests.csproj     xUnit tests for Domain layer
    Entities/       CompanyTests.cs         entity invariant tests
  Infrastructure/   Infrastructure.csproj   classlib  — refs Domain + Npgsql.EFCore.PG 9.x
    CrmDbContext.cs                         EF Core DbContext (6 DbSets)
    Configurations/ IEntityTypeConfiguration<T> per aggregate (applied via ApplyConfigurationsFromAssembly)
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
```

## verify_cmd

```bash
bash scripts/verify.sh
```

`scripts/verify.sh` checks that Domain has no outward project references (clean-arch enforcement), then runs `dotnet build closeloop.sln --configuration Release`, then runs `dotnet test --no-build` against the full solution. Test layers covered: **Domain unit tests** (`backend/Domain.Tests`) and **Infrastructure model tests** (`backend/Infrastructure.Tests`).

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

## Local development — PostgreSQL

A `docker-compose.yml` at repo root starts a PostgreSQL 16 container (`postgres` service, port 5432).
Credentials are read from environment variables; copy `.env.example` to `.env` and fill in values before running:

```bash
cp .env.example .env
docker compose up -d
```

Named volume `postgres_data` persists data across restarts.

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
- `Pipeline._stages` is a `private readonly List<PipelineStage>` backing field. EF Core is told to use it via `Navigation(p => p.Stages).HasField("_stages").UsePropertyAccessMode(PropertyAccessMode.Field)` because the `Stages` getter returns a computed `IReadOnlyList` (OrderBy + ToList), not the field itself.
- Activity anchor FKs (ContactId/CompanyId/DealId) use `DeleteBehavior.Restrict` — not SetNull — because nulling the sole anchor would silently violate the exactly-one-anchor domain invariant that Activity.Create enforces.
- PipelineStage→Pipeline uses `DeleteBehavior.Cascade` (deleting a pipeline removes its stages). Deal→Pipeline and Deal→PipelineStage use `DeleteBehavior.Restrict` (cannot delete a pipeline or stage that has live deals).
