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
    Entities/       Company.cs              domain aggregates
  Domain.Tests/     Domain.Tests.csproj     xUnit tests for Domain layer
    Entities/       CompanyTests.cs         entity invariant tests
  Infrastructure/   Infrastructure.csproj   classlib  — refs Domain
  Api/              Api.csproj              web app   — refs Infrastructure
        Program.cs  minimal API host
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

`scripts/verify.sh` checks that Domain has no outward project references (clean-arch enforcement), then runs `dotnet build closeloop.sln --configuration Release`, then runs `dotnet test --no-build` against the full solution. Test layers covered: **Domain unit tests** (`backend/Domain.Tests`).

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
