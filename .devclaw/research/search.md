# Search — cross-object global search

## Sources consulted

- **Salesforce** — SOSL (Salesforce Object Search Language) REST API and "Global Search" surface in
  Lightning Experience. Examined via public Salesforce REST API Developer Guide (SOSL reference,
  `POST /search` endpoint) and Salesforce Help articles on global search bar behaviour.
  SOSL returns grouped result sets (`searchRecords` per type) plus a `searchResultsMetadata` block
  with per-type result counts that can drive "see all" navigation.

- **HubSpot** — CRM Search API (`POST /crm/v3/objects/{objectType}/search`) and the global
  omnibox in the HubSpot web app. Examined via HubSpot developer docs and the CRM API changelog.
  Each search endpoint returns `{ total, results }` where `total` is the uncapped database count and
  `results` contains the paged window. The app cross-stitches per-type endpoints to build a grouped
  typeahead. The `total` field is explicitly documented as the full match count, not the page size.

- **Pipedrive** — `/api/v1/itemSearch` cross-object search endpoint. Examined via Pipedrive API
  v1 public documentation. Returns a `data.items` array of results across Persons, Organizations,
  Deals, and Activities in a single response. Includes `additional_data.pagination.more_items_in_collection`
  (boolean) rather than a numeric total count.

- **Attio** — Per-object `POST /v2/objects/{slug}/records/query` and the workspace global search
  bar. Examined via Attio public API v2 docs and the Attio public changelog. No cross-object search
  endpoint exists in the API; global search is UI-only and not surfaced via REST.

- **Zoho CRM** — `GET /crm/v2/search?word={term}` (keyword search) and `?criteria=` (filter
  syntax). Examined via Zoho CRM API v2 public reference. Per-module only — no cross-module search
  endpoint. Requires a separate request per module type.

---

## Borrowed

### §1 — Grouped cross-type response with a single query parameter (Pipedrive / Salesforce)

**What**: `GET /search?q={term}` returns a single JSON object with three typed sub-arrays
(`contacts`, `companies`, `deals`), each capped at 10 results, plus a `totalHits` integer that
is the sum of uncapped match counts across all three types.

**From**: Pipedrive `itemSearch` (cross-object grouping into typed arrays in one request) and
Salesforce SOSL (grouped result sets with per-type metadata used to power "see all" links).

**Why it fits**: closeloop's MVP only has three searchable object types. A single endpoint with
grouped results avoids three round-trips for the typeahead case, matches the interaction pattern
users expect from Pipedrive-class CRMs, and requires no client-side merging logic.

### §2 — True uncapped `totalHits` for "see all" affordance (HubSpot)

**What**: The `totalHits` field is computed from three separate `CountAsync()` database queries
(one per type), each **without** a row limit. The result lists are capped at 10; `totalHits` is
the sum of the three counts. This means when 15 contacts match a query, the response returns 10
contacts in the list and `totalHits: 15` (or higher if other types also match), giving the client
accurate signal to offer a "see all Contacts" link scoped to a single type.

**From**: HubSpot CRM Search API — each per-type response carries `total` (the database count,
not the page count) alongside the paged `results`. HubSpot's global search typeahead uses this
`total` to decide whether to render "See all N contacts" below the capped list.

**Why it fits**: Without a true count, a client that receives 10 contacts cannot distinguish
"there are exactly 10 matches" from "there are 15 matches and I truncated 5". The extra
`CountAsync()` per type adds three lightweight `SELECT COUNT(*)` round-trips but makes the "see
all" UX implementable without a second search request.

### §3 — Case-insensitive substring search via `ToLower().Contains()` (MVP pragmatism)

**What**: Search predicates use `.ToLower().Contains(term)` in LINQ, translated to
`LOWER(column) LIKE '%term%'` in SQL. Applied across:
- Contacts: `Name` + `Email`
- Companies: `Name` + `Domain`
- Deals: `Title`

**From**: Standard pattern across all five reference CRMs for their basic keyword search
(Salesforce SOSL `FIND {term}`, HubSpot `?query=`, Pipedrive `term`, Zoho `?word=`).

**Why it fits**: Substring LIKE search is universally understood and requires no extra
infrastructure (no full-text index, no search engine). PostgreSQL's `pg_trgm` GIN index or
`tsvector` full-text would give better relevance ranking but add schema complexity the MVP does
not yet justify. This is an explicit deferral — see §Rejected A below.

---

## Rejected & why

### §Rejected A — PostgreSQL full-text search / `tsvector` (too heavy for MVP)

**What was considered**: Using PostgreSQL's built-in `tsvector`/`tsquery` for full-text search,
accessed via EF Core's `EF.Functions.ToTsVector()` and `EF.Functions.ToTsQuery()` helpers.
Would support stemming, relevance ranking, and GIN index acceleration.

**Source**: Attio (uses Typesense behind their API), Salesforce SOSL (uses an internal search
index), and the EF Core PostgreSQL provider documentation.

**Reason rejected**: Requires a migration to add `tsvector` generated columns and a GIN index
per table, plus a separate `?search_config=english` decision. The EF Core InMemory provider
does not support `ToTsVector`, making integration tests impossible without Testcontainers/real
Postgres. The MVP's dataset size (hundreds of records) does not justify the extra complexity.
Upgrade path is a documented future migration.

### §Rejected B — Per-object search endpoints with client-side merge (Zoho / Attio pattern)

**What was considered**: Exposing separate search endpoints per object type
(`GET /contacts/search?q=`, `GET /companies/search?q=`, `GET /deals/search?q=`) and letting
the client merge them for the global typeahead.

**Source**: Zoho CRM (`GET /crm/v2/search?module=Contacts&word=`), Attio (per-object record
query).

**Reason rejected**: Requires three HTTP round-trips for a global typeahead — adds 3× latency
and client-side complexity. Pipedrive and Salesforce both prove that a single cross-object
endpoint is the correct ergonomic for a "type something, see everything" UX. Per-object search
endpoints can be added later if advanced filtering per type is needed; the global endpoint
covers 95% of the typeahead use case.

### §Rejected C — Pipedrive boolean `more_items_in_collection` instead of a numeric count

**What was considered**: Returning a boolean `hasMore` flag (Pipedrive's
`additional_data.pagination.more_items_in_collection`) instead of a numeric `totalHits` count.

**Source**: Pipedrive `itemSearch` response schema.

**Reason rejected**: A boolean only tells the client "there are more results" — it cannot drive
"see all 15 contacts" text. HubSpot's numeric `total` is strictly more informative and does not
increase implementation cost (both require the same `CountAsync()` call; the boolean version
just discards the count). `totalHits` is adopted as the field name because it is self-explanatory
and maps directly to what the value represents.
