# closeloop

closeloop is a CRM built by synthesizing best-in-class patterns from a reference set of five
production CRMs: Salesforce, HubSpot, Pipedrive, Attio, and Zoho. Every major feature is
grounded in a structured research artifact under `.devclaw/research/`, so design decisions are
traceable to the source products rather than invented from scratch.

## Stack

- **.NET 9** backend (SDK 9.0.315, clean-architecture layers: Domain → Infrastructure → Api)
- **Angular 21** frontend (standalone components, Vitest unit tests)
- **PostgreSQL 16** (EF Core / Npgsql)

## Getting started

```bash
docker compose up          # starts the API and Postgres; frontend served from the API image
bash scripts/verify.sh     # build + run all backend and frontend tests
```
