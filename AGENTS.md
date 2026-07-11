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
```

## verify_cmd

```bash
bash scripts/verify.sh
```

`scripts/verify.sh` checks that Domain has no outward project references (clean-arch enforcement), then runs `dotnet build closeloop.sln --configuration Release`.

## Key decisions

- `global.json` pins to `9.0.315` with `rollForward: latestPatch` so `dotnet` always resolves to .NET 9 even though .NET 10 is also installed.
- Api project uses `Microsoft.NET.Sdk.Web` so ASP.NET Core meta-package is available without an explicit PackageReference.
- `Program.cs` exposes `public partial class Program {}` so future integration-test projects can reference the entry-point assembly.
- Marker types in Domain and Infrastructure keep the classlibs non-empty and compilable from day one.
