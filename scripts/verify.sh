#!/usr/bin/env bash
set -euo pipefail

# Verify clean-architecture layering: Domain must have no outward project references.
refs=$(dotnet list backend/Domain/Domain.csproj reference 2>&1)
# When references exist the output contains lines ending with ".csproj" (no trailing period).
# The "no references" message ends with ".csproj." (trailing period after the project path).
if echo "$refs" | grep -qE '\.csproj$'; then
  echo "ERROR: Domain project has outward project references (violates clean-arch):"
  echo "$refs"
  exit 1
fi

# Full solution build in Release mode.
dotnet build closeloop.sln --configuration Release

# Run all unit tests (Domain unit tests + Infrastructure model tests).
dotnet test closeloop.sln --configuration Release --no-build

# Run Angular frontend unit tests.
(cd frontend && npm install --prefer-offline && npx ng test --watch=false)
