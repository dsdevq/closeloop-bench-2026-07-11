# CRM Contacts feature — reference research

This artifact captures the design-input research for the **Contacts feature slice** in closeloop:
the list view, the detail view (including related-record surfacing), and bulk/import affordances.
It feeds directly into the contacts-api implementation task, which will build REST endpoints for
Contact CRUD backed by the already-shipped `backend/Domain/Entities/Contact.cs`.

The domain object itself (fields, cardinality, many-to-many company link) is settled in
`.devclaw/research/domain-model.md`. This artifact focuses on **surface-layer design**: how the
Contact is presented in a list, what the detail record exposes beyond raw fields, and what
write-many affordances the API must support.

---

## Feature scope (synthesis target)

| Surface | What is designed here |
|---|---|
| **List view** | Column set, default vs. configurable columns, filter operators, sort model, pagination contract |
| **Detail view** | Fields shown, inline-edit model, associated Company/Deals/Activities surfacing |
| **Bulk/import** | Batch-update endpoint shape, CSV import deduplication strategy |

---

## Sources consulted

- **Salesforce** — Lightning Experience Contact list view and Contact detail record (Lightning
  "compact layout" top bar + related-list sidebar). Examined via Salesforce Trailhead public docs,
  the Object Reference for the `Contact` standard object, and the Salesforce REST API v58 reference
  for `GET /services/data/vXX/sobjects/Contact` and the composite request pattern.

- **HubSpot** — CRM API v3 public documentation for Contacts objects: `GET /crm/v3/objects/contacts`
  (list + pagination model), `GET /crm/v3/objects/contacts/{id}` (detail with `properties` and
  `associations` expansion), `GET /crm/v3/objects/contacts/{id}/associations/companies` (company
  association surface), and `POST /crm/v3/objects/contacts/search` (search endpoint). Examined via
  `developers.hubspot.com` public API reference and the HubSpot CRM UI live app tour.

- **Pipedrive** — REST API v1 reference for the `Persons` object: `GET /v1/persons` (list with
  `filter_id`, `sort`, `start`, `limit`), `GET /v1/persons/{id}` (detail with related deals and
  activities), `/v1/filters?type=people` (saved filter resource), and `/v1/personFields` (field
  metadata). Examined via `developers.pipedrive.com` public API docs and the Pipedrive People list
  UI documented in their help centre.

- **Attio** — Public API v2 documentation: `GET /v2/objects/people/records` (list with
  `attributes[]` projection, filter, sort), `PATCH /v2/objects/people/records/{record_id}` (partial
  attribute update), the People record detail UI (attribute sidebar + activity feed), and the
  workspace Views documentation. Examined via `developers.attio.com` API reference and Attio's
  public product changelog posts on "Record views" and "Views as saved filters".

- **Zoho CRM** — Developer documentation v2 for the Contacts module: `GET /crm/v2/Contacts` (list
  with `fields`, `sort_by`, `sort_order`, `page`, `per_page`), `GET /crm/v2/Contacts/{id}` (detail
  with Related Lists), `POST /crm/v2/Contacts/actions/mass_update` (bulk field update), and the
  CSV import API (`POST /crm/v2/Contacts/actions/import`). Examined via `www.zoho.com/crm/developer/docs`
  public API reference and the Zoho CRM help centre UI documentation for the Contacts module.

---

## Borrowed

### 1. Salesforce's compact-layout banner at the top of the contact detail

- **What**: The top section of the Contact detail record shows a small set of "highlight" fields
  (Name, Job Title, Email, Phone) in a compact horizontal strip before the full field list. This
  compact layout is a distinct named configuration separate from the full page layout. For the API
  this translates to a `summary` projection: `GET /contacts/{id}?view=compact` returns only
  highlight fields, while `GET /contacts/{id}` returns all fields.
- **From**: Salesforce Lightning Experience — "Compact Layouts" feature under Setup; `GET
  /services/data/vXX/sobjects/Contact/{id}` with `fields` parameter for field projection; and the
  UI "Highlights Panel" on Lightning record pages.
- **Why it fits**: The closeloop list view and search-result cards need a lightweight representation
  of a Contact (name + email + phone) without paying the cost of fetching all fields. A `summary`
  projection parameter makes the same endpoint usable for both card and full-detail rendering,
  matching the existing Contact entity's four fields exactly.

### 2. HubSpot's primary-company association surfaced inline in contact detail

- **What**: The Contact detail record embeds the associated primary company as a nested object —
  `primaryCompany: { id, name, domain, industry }` — rather than a bare `companyId` foreign key.
  The client does not need a second round-trip to render the company name and logo next to the
  contact. Additional (non-primary) companies are listed under an `otherCompanies` array.
- **From**: HubSpot CRM API v3 — `GET /crm/v3/objects/contacts/{contactId}?associations=companies`
  expands associated company records inline. The HubSpot Contact detail UI surfaces the "Primary
  Company" block in the right-hand association panel with company name, domain, and a click-through
  link.
- **Why it fits**: The domain model already defines `ContactCompanyLink.IsPrimary`
  (see `.devclaw/research/domain-model.md` Borrowed §1). The API should project this as
  `primaryCompany` (embedded) and `additionalCompanies[]` (array of id+name stubs) so the list
  and detail views never need a separate companies lookup for the common case.

### 3. HubSpot's cursor-based pagination for the contact list endpoint

- **What**: `GET /contacts` accepts `limit` (1–100, default 20) and `after` (opaque string cursor
  from a previous response's `paging.next.after` field). The response envelope is
  `{ results: [...], paging: { next: { after: "..." } } }`. When there is no next page `paging` is
  absent. No `total` count is returned by default (avoids full-table scans).
- **From**: HubSpot CRM API v3 `GET /crm/v3/objects/contacts` — the "Pagination" section of the
  object API reference; also used identically for Companies, Deals, and all other CRM objects in
  HubSpot.
- **Why it fits**: Cursor pagination is stable when contacts are being created concurrently (a
  normal CRM write pattern — imports, sync jobs). Offset pagination produces duplicate or skipped
  rows when the total set shifts between pages. closeloop's list endpoint will emit a keyset cursor
  derived from `(createdAt, id)` so pagination is deterministic and index-backed.

### 4. Pipedrive's orthogonal filter + sort + field-projection parameters on list endpoint

- **What**: Pipedrive's `GET /v1/persons` accepts independent query parameters: `filter_id` (ID
  of a saved filter resource), `sort` (e.g. `name ASC, update_time DESC`), `start` (offset), and
  `limit`. Field selection is handled by an additional `fields` meta-endpoint. Each parameter is
  orthogonal — any combination is valid. Saved filters are first-class resources at `/v1/filters`.
- **From**: Pipedrive REST API v1 — Persons endpoint documentation; `/v1/filters?type=people`
  resource; the Pipedrive People list UI column/filter configurator in the help centre.
- **Why it fits**: closeloop's `GET /contacts` should expose `sort` (field + direction), `filter`
  (a set of field-operator-value triples as a query param), and `fields` (comma-delimited
  projection) as independent query parameters so any combination composes. This avoids the need for
  a separate search endpoint while remaining fully URL-expressible for bookmarking and sharing.

### 5. Attio's inline-editable detail fields backed by a PATCH partial-update endpoint

- **What**: In Attio's People record view, each attribute (Name, Email, Phone, Job Title, etc.) is
  rendered as an in-place editable widget. Clicking a field activates an edit control; tabbing or
  clicking away commits the change. The network call is `PATCH /v2/objects/people/records/{id}`
  with a body containing only the changed attribute(s) — unchanged fields are omitted entirely.
  The server applies a partial merge, not a full replace.
- **From**: Attio public API v2 — `PATCH /v2/objects/people/records/{record_id}` documentation;
  Attio product changelog post "Editable record views" (2023).
- **Why it fits**: A `PATCH /contacts/{id}` endpoint that accepts a partial JSON body (any subset
  of `{ name, email, phone, companyId }`) directly enables the inline-edit interaction at zero
  additional API surface. The Contact entity's existing private setters can be complemented with an
  `Update(...)` method that accepts nullable overrides. This is strictly more correct than PUT
  semantics, which would force the client to re-send all fields to avoid nulling them out.

### 6. Zoho's mass-update action for bulk field edits on a selection of contacts

- **What**: Selecting multiple contacts in the list view and choosing "Mass Update" applies the
  same field value(s) to all selected records in one request:
  `POST /crm/v2/Contacts/actions/mass_update` with body
  `{ "data": [{"id": "...", "Owner": {...}}], "duplicate_check_fields": ["Email"] }`.
  The response includes a per-record success/failure breakdown. A separate field can be updated
  across a set of IDs in a single round-trip.
- **From**: Zoho CRM API v2 — Contacts module `mass_update` action; documented under
  "Update Multiple Records" in the Zoho CRM REST API reference.
- **Why it fits**: Sales teams routinely reassign a batch of contacts to a new owner or update
  lifecycle stage after an import. A `PATCH /contacts` batch endpoint accepting
  `{ ids: [uuid, ...], fields: { ... } }` is the REST-idiomatic equivalent and enables the list
  view's bulk-select affordance. Limiting to fields that exist on the entity (name, email, phone,
  companyId) keeps the implementation bounded.

---

## Rejected & why

### A. Salesforce's Lead object as a pre-Contact state with `/leads/convert` endpoint

- **What was considered**: Exposing a `/leads` resource for unqualified prospects, separate from
  `/contacts`, with a `POST /leads/{id}/convert` endpoint that creates a Contact (and optionally
  a Company and Deal) in one transaction. This is Salesforce's standard qualification workflow.
- **Source**: Salesforce REST API v58 — `POST /services/data/vXX/sobjects/Lead/convert`; the
  Lead standard object and Lead Conversion documented in Salesforce Help and the Trailhead
  "Sales Cloud Basics" module.
- **Reason rejected**: closeloop has no pre-qualification limbo state (this was argued and settled
  in `.devclaw/research/domain-model.md` Rejected §A). For the API surface: a Contact without an
  associated Deal already models an unqualified prospect. Introducing a parallel `/leads` endpoint
  would create two paths to create a person record, two list views for salespeople to check, and a
  conversion step that adds latency with no product benefit for a ≤ 50-seat SMB team. The
  complexity cost of the Salesforce Lead conversion pipeline (lead-to-contact field mapping,
  merge rules, duplicate detection across two objects) is not justified.

### B. HubSpot's separate `POST /contacts/search` endpoint for filtered queries

- **What was considered**: Splitting search/filter into a dedicated `POST /contacts/search` endpoint
  that accepts a JSON filter body with nested filter groups and sort descriptors, separate from the
  `GET /contacts` list endpoint used for simple enumeration.
- **Source**: HubSpot CRM API v3 — `POST /crm/v3/objects/contacts/search`; the HubSpot search
  API accepts `{ filterGroups: [...], sorts: [...], properties: [...], limit, after }` in the
  request body.
- **Reason rejected**: Two endpoints for "list contacts" creates ambiguity about which to use and
  when. HubSpot's motivation is that HTTP GET does not support a request body, making complex
  filter graphs awkward to express as query params. closeloop's filter model (field + operator +
  value triples) is simple enough to encode as query parameters (e.g.
  `?filter=email:contains:@acme.com&filter=name:starts_with:Jane`) without exceeding URL length
  limits in practice. A single `GET /contacts` endpoint with orthogonal query params (Pipedrive
  pattern, Borrowed §4) keeps the API surface minimal and HTTP-cache-friendly.

### C. Pipedrive's multi-email typed array per contact

- **What was considered**: Representing the contact's email addresses as an array of typed objects:
  `emails: [{ value: "jane@work.com", label: "work", primary: true }, { value: "jane@home.com", label: "home", primary: false }]`.
  Phones follow the same shape. This allows one contact record to carry unlimited emails and phones.
- **Source**: Pipedrive REST API v1 — `Person` object; the `email` and `phone` fields are arrays of
  `{ value, label, primary }` objects in both GET responses and POST/PUT request bodies. Pipedrive
  exposes a `label` enum of `work | home | other`.
- **Reason rejected**: The shipped `Contact` entity (`Domain.Entities.Contact`) has a single
  `Email: string` (required, validated) and `Phone: string?` (optional). Adopting a multi-value
  array would require changing the domain entity, introducing a `ContactEmail` value-object or
  child collection, and complicating the deduplication logic (which email is the canonical lookup
  key?). The single-email model covers the overwhelming majority of SMB sales workflows — one
  work email per contact — and can be extended in a follow-on schema migration if product demands
  it. Changing the entity shape to match Pipedrive now would be premature complexity.

### D. Attio's equal-weight multi-company display without a primary designation

- **What was considered**: In the contact detail view, surfacing all associated companies at the
  same visual weight — a flat list of companies with no "primary" distinction. Attio's People
  record shows all linked companies (the `companies` attribute can hold multiple values) with no
  "primary" flag; each company is navigable via a chip.
- **Source**: Attio public API v2 — `People` records `companies` attribute is a multi-value
  relation; Attio UI People record view showing a comma-separated list of linked company chips
  with no hierarchy.
- **Reason rejected**: The domain model uses `ContactCompanyLink.IsPrimary` to designate one
  primary company per contact (see `.devclaw/research/domain-model.md` Borrowed §1). The API
  detail response must reflect this by surfacing `primaryCompany` prominently and
  `additionalCompanies[]` as a secondary list. An equal-weight display would hide information the
  domain explicitly encodes and would require the frontend to independently determine which company
  to show in list-view column truncation. The HubSpot model (Borrowed §2) is adopted instead
  because it mirrors the `IsPrimary` flag directly.

### E. Zoho's tabbed Info/Timeline split in the contact detail view

- **What was considered**: Splitting the contact detail into two tabs — "Info" (all field values in
  a two-column layout) and "Timeline" (chronological activity feed) — as separate views rather than
  a unified scrollable page. The API equivalent would be separate endpoints:
  `GET /contacts/{id}` for fields and `GET /contacts/{id}/timeline` for activities.
- **Source**: Zoho CRM help centre — Contacts detail record UI with "Info", "Timeline", and
  "Related" sub-tabs; Zoho CRM API v2 `GET /crm/v2/Contacts/{id}` returns fields only, with
  activities fetched separately via `GET /crm/v2/Contacts/{id}/Activities`.
- **Reason rejected**: The tabbed split forces the user to navigate away from fields to see recent
  activity context, breaking the "glance at what happened last" use case that is primary for a
  sales rep returning to a contact before a call. Attio's unified record view (fields in left
  sidebar, activity feed in centre) and the HubSpot unified "About + Timeline" layout both outrank
  Zoho's tabbed approach in user-testing evidence (Attio product blog, HubSpot design system docs).
  For the API, `GET /contacts/{id}` should include a `recentActivities` array (last 5, embedded)
  so the detail view renders in one request; a separate `GET /contacts/{id}/activities` endpoint
  is additive for paginated activity browsing, not a replacement.
