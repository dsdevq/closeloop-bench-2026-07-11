# CRM Companies feature — reference research

This artifact captures the design-input research for the **Companies feature slice** in closeloop:
the company list view, the company detail record (including related contacts, deals, and activities),
deduplication/merge affordances, and the sub-resource API pattern for related records.

The domain object itself (fields, cardinality, Company-Contact junction) is settled in
`.devclaw/research/domain-model.md`. This artifact focuses on **surface-layer design**: how the
Company record is presented, what related data is surfaced inline vs. sub-resource, and what
write affordances the API must support.

---

## Feature scope (synthesis target)

| Surface | What is designed here |
|---|---|
| **List view** | Column set, default sort, search-by-name, filter operators, pagination contract |
| **Detail view** | Fields shown, inline-related counts, sub-resource pattern for contacts/deals/activities |
| **Merge/deduplication** | Domain-as-key for duplicate detection; merge endpoint shape |
| **Write affordances** | PATCH partial update, DELETE restrictions (no orphaning deals) |

---

## Sources consulted

- **Salesforce** — `Account` standard object documentation in the Salesforce Object Reference
  (public developer docs); `GET /services/data/vXX/sobjects/Account/{id}` including related-list
  sub-queries via the REST API Composite resource; the Account Hierarchy UI (`ParentId` self-FK
  on Account for parent/subsidiary modelling); the Lightning Experience Account detail page with
  the standard Related Lists (Contacts, Opportunities, Activities, Cases). Examined via Salesforce
  Trailhead documentation and the Salesforce SOAP API Schema Browser.

- **HubSpot** — CRM API v3 public documentation for Companies objects:
  `GET /crm/v3/objects/companies` (list with cursor pagination), `GET /crm/v3/objects/companies/{id}`
  (detail with `properties` and `associations` expansion), `GET /crm/v3/objects/companies/{id}/associations/contacts`
  and `/deals` (association sub-resources), `POST /crm/v3/objects/companies/merge`
  (merge two company records into one canonical record). Examined via `developers.hubspot.com`
  public API reference and the HubSpot CRM UI live company record.

- **Pipedrive** — REST API v1 reference for the `Organizations` object:
  `GET /v1/organizations` (list with `filter_id`, `sort`, `start`, `limit`),
  `GET /v1/organizations/{id}` (detail with embedded summary counts: `open_deals_count`,
  `won_deals_count`, `lost_deals_count`, `activities_count`),
  `GET /v1/organizations/{id}/persons` and `/deals` and `/activities` (paginated sub-resources
  for related records). Examined via `developers.pipedrive.com` public API docs and the
  Pipedrive Organization detail UI in their help centre.

- **Attio** — Public API v2 documentation: `GET /v2/objects/companies/records` (list with
  `attributes[]` projection, filter, sort), `GET /v2/objects/companies/records/{record_id}`
  (detail with related attribute values), the Companies record detail UI (attribute panel + activity
  feed), Attio's "Record relations" documentation covering People and Deals linked to Companies.
  Examined via `developers.attio.com` API reference and Attio's public product changelog.

- **Zoho CRM** — Developer documentation for the Accounts module (Zoho's term for companies):
  `GET /crm/v2/Accounts` (list with `fields`, `sort_by`, `sort_order`, `page`, `per_page`),
  `GET /crm/v2/Accounts/{id}` (detail), related sub-module queries
  (`GET /crm/v2/Accounts/{id}/Contacts`, `/Deals`, `/Activities`). Examined via
  `www.zoho.com/crm/developer/docs` public API reference and Zoho CRM help centre UI
  documentation for the Accounts module.

---

## Borrowed

### 1. HubSpot's `domain` field as the canonical deduplication key

- **What**: When creating or importing a company, the `domain` field (e.g. `acme.com`) acts as
  the deduplication key. If a new company record has the same `domain` as an existing one, the
  API returns a 409 with the conflicting record's ID rather than creating a duplicate. On import
  and PATCH, domain-matching determines whether to create or update. This mirrors the email-key
  deduplication used for contacts (see `.devclaw/research/contacts.md` Borrowed §7).
- **From**: HubSpot CRM API v3 — the Companies deduplication documentation; HubSpot's "Unique
  identifier" setting for the Company object; the `POST /crm/v3/objects/companies` endpoint
  response shape when a domain conflict is detected.
- **Why it fits**: The `Company` entity already carries a `Domain: string?` field. Treating a
  non-null domain as a unique tenant-scoped identifier provides a stable natural key for sync
  integrations and import flows. Sales teams routinely import company lists from LinkedIn or CSV
  exports where domain is the reliable deduplication column. The field is nullable so private
  companies without a public web presence can still be created without a domain.

### 2. Pipedrive's inline summary counts on the list endpoint

- **What**: The Company list response includes `openDealsCount`, `wonDealsCount`, `lostDealsCount`,
  `contactsCount`, and `activitiesCount` as computed integer fields on each list item. These
  counts are precomputed (not fetched via sub-resources) so the list view can render a deal-count
  chip on each company card without N additional requests.
- **From**: Pipedrive REST API v1 — `GET /v1/organizations` response; each organization object
  includes `open_deals_count`, `won_deals_count`, `lost_deals_count`, `people_count`,
  `activities_count` as integer fields on the list item.
- **Why it fits**: The Company list view's primary value proposition is "at a glance, which
  accounts are active?" A company card showing `3 open deals · 12 contacts` lets the sales rep
  prioritise without clicking into each record. Including these counts on the list endpoint avoids
  N+1 round-trips, and they can be computed efficiently in a single SQL GROUP BY query joined to
  the list page.

### 3. Pipedrive's sub-resource pattern for paginated related records

- **What**: Related contacts, deals, and activities are each exposed as independent paginated
  sub-resources under the company:
  `GET /companies/{id}/contacts?limit=20&after=<cursor>`,
  `GET /companies/{id}/deals?limit=20&after=<cursor>`,
  `GET /companies/{id}/activities?limit=20&after=<cursor>`.
  Each sub-resource uses the same cursor-pagination envelope adopted from HubSpot
  (see `.devclaw/research/contacts.md` Borrowed §3). The parent company detail endpoint
  (`GET /companies/{id}`) embeds only the first N items (a "preview slice") of each related
  collection; the sub-resource endpoints serve full paginated browsing.
- **From**: Pipedrive REST API v1 — `GET /v1/organizations/{id}/persons`,
  `GET /v1/organizations/{id}/deals`, `GET /v1/organizations/{id}/activities`; each is a
  standalone paginated endpoint with `start`, `limit`, and `additional_data.pagination` in the
  response. Attio's `GET /v2/objects/companies/records/{record_id}` pattern (embedded attributes
  on the record, separate endpoints for related records) reinforces the same split.
- **Why it fits**: The Company detail view needs a "preview" of related contacts and deals (e.g.
  top 3 by recency) to render above the fold, but the full paginated list belongs to a secondary
  tab or expandable section. Embedding the first 3–5 related items in the parent detail response
  keeps the initial page load to one request; the sub-resource endpoints support infinite scroll
  or "load more" in the UI without overloading the parent detail payload.

### 4. HubSpot's `primaryContact` embedding in company detail

- **What**: The company detail response embeds the primary associated contact as a nested object
  — `primaryContact: { id, name, email, phone }` — so the company card and header can render the
  key person without a second round-trip. Additional contacts are accessible via the
  `/companies/{id}/contacts` sub-resource. This is the symmetric counterpart to the
  `primaryCompany` embedding on the contact detail (see `.devclaw/research/contacts.md`
  Borrowed §2).
- **From**: HubSpot CRM API v3 — `GET /crm/v3/objects/companies/{id}?associations=contacts`
  expands associated contact records; the HubSpot Company detail UI shows the "Primary Contact"
  in the right-hand panel.
- **Why it fits**: The Contact-Company junction already carries `IsPrimary`
  (see `.devclaw/research/domain-model.md` Borrowed §1). The API should project this as
  `primaryContact` (embedded name+email) and surface additional contacts via sub-resource. This
  enables the company list and card views to show a human name alongside the company name — the
  primary "who to call at Acme Corp" — without a separate lookup.

### 5. Attio's unified activity feed on company detail (cross-anchor aggregation)

- **What**: The company detail record's activity feed shows not only activities where `CompanyId`
  equals the company, but also activities anchored to Contacts or Deals that are associated with
  the company. The feed merges all three anchor types and sorts by `OccurredAt` descending.
  The first 5 items are embedded in the detail response; full browsing is available via
  `GET /companies/{id}/activities`.
- **From**: Attio Companies record view — the activity timeline on a Company record surfaces
  Notes, Tasks, and Calls logged against any linked Person (contact) or pipeline entry (deal)
  associated with the company, not just activities logged directly against the company object.
  Attio's public API v2 `GET /v2/objects/companies/records/{id}` and the related Attio changelog
  post "Unified record timelines" (2024).
- **Why it fits**: A sales rep reviewing an account before a call wants to see all recent
  touchpoints — calls made to individual contacts at that company, stage changes on live deals —
  in one feed. Scoping the company activity feed to only direct-anchor activities (where
  `Activity.CompanyId` is set) would miss the majority of sales interactions, which are
  anchored to the contact or deal. The cross-anchor merge is a read-side aggregation (SQL UNION
  or application-layer merge) that does not change the domain model.

---

## Rejected & why

### A. Salesforce's Account hierarchy (ParentId self-FK for parent/subsidiary modelling)

- **What was considered**: Adding a nullable `ParentCompanyId` FK on the Company entity so that
  subsidiary companies can be linked to a parent organisation (e.g. "Acme UK" → "Acme Corp"),
  enabling roll-up aggregate views (total deals for the parent group). This is Salesforce's
  Account Hierarchy feature.
- **Source**: Salesforce — `Account.ParentId` self-referential FK; Account Hierarchy view in
  Lightning Experience; `GET /sobjects/Account/{id}?relationships=ChildAccounts` for fetching
  the hierarchy tree.
- **Reason rejected**: Parent-subsidiary account hierarchies are a mid-market/enterprise CRM
  concern. closeloop targets SMB sales teams (≤ 50 seats) where one company per record is the
  norm. Introducing a self-FK on Company adds recursive query complexity (CTE-based hierarchy
  traversal), ambiguity about which level owns a deal, and a hierarchy management UI that has no
  product spec. The `Company` entity has no `ParentCompanyId`; the feature can be added in a
  future iteration if enterprise customer needs emerge.

### B. HubSpot's full merge workflow (surviving record selection + field-level merge resolution)

- **What was considered**: A `POST /companies/merge` endpoint that merges two company records:
  the caller specifies a `primaryObjectId` (surviving record) and a `mergeObjectId` (record to
  be consumed); all contacts, deals, and activities from the consumed record are re-linked to the
  surviving record; the consumed record is soft-deleted. Field-level merge resolution (which
  field value wins) is configurable per property.
- **Source**: HubSpot CRM API v3 — `POST /crm/v3/objects/companies/merge` with
  `{ primaryObjectId, mergeObjectId }` body; HubSpot's company merge documentation covering
  property-level merge behaviour.
- **Reason rejected**: A full merge workflow with field-level resolution is significant product
  scope: it requires a UI for conflict resolution, a soft-delete model, and audit trail for
  re-linked records. For MVP, a simpler approach suffices: the deduplication key check on `domain`
  (Borrowed §1) prevents the majority of duplicate creation. If two companies must be merged, a
  sales rep can manually re-link the contacts/deals and delete the redundant record via normal
  PATCH/DELETE endpoints. The full merge endpoint can be introduced in a deduplication-focused
  sprint when the product warrants it.

### C. Zoho's tabbed Related Lists view for company detail (Info / Timeline / Related sub-tabs)

- **What was considered**: Splitting the company detail into separate tabs — "Info" for field
  values, "Timeline" for the activity feed, and "Related" for contacts/deals — with separate API
  endpoints for each tab's data (`GET /companies/{id}`, `GET /companies/{id}/activities`,
  `GET /companies/{id}/contacts`). The UI never loads related data until the user clicks the
  relevant tab.
- **Source**: Zoho CRM help centre — Accounts detail record UI with "Info", "Timeline", and
  "Related" sub-tabs; Zoho CRM API v2 where fields, activities, and related lists are separate
  endpoints with no inline embedding.
- **Reason rejected**: The same reasoning applied to the Contacts detail view (see
  `.devclaw/research/contacts.md` Rejected §E) applies here: a tabbed split forces navigation
  overhead before the rep can see the last touchpoint. The preferred pattern (Borrowed §3 and §5)
  embeds the most recent 5 activities and top 3 contacts inline in the company detail response,
  reserving sub-resource pagination for deeper browsing. One request renders the
  above-the-fold content; the sub-resources handle scroll/tab expansion.

### D. Pipedrive's "Organization follower" model (per-user follow subscriptions on records)

- **What was considered**: A `POST /companies/{id}/followers` endpoint that lets a user subscribe
  to a company record and receive notifications on any new deal, contact, or activity associated
  with it. Pipedrive's Organization follower model allows multiple users to monitor the same
  account independently of record ownership.
- **Source**: Pipedrive REST API v1 — `POST /v1/organizations/{id}/followers`,
  `GET /v1/organizations/{id}/followers`; also present on Deals and Persons.
- **Reason rejected**: Notification/subscription infrastructure is a separate product workstream
  (requiring push delivery, notification preferences, and read/unread state). Introducing a
  follower model on Company before any notification system exists creates a resource with no
  product effect. Record ownership (`OwnerId`) already determines the primary responsible user;
  a team-visibility model can be layered on top when notification infrastructure is planned.

### E. Attio's attribute-only company model (no first-class `Name` field — Name is an attribute)

- **What was considered**: Representing every company field — including Name — as a named
  attribute in a generic attribute map (`attributes: { name: { value: "Acme Corp" }, domain: { ... } }`)
  rather than as top-level typed entity fields. In Attio's model, the Company object has no
  hard-coded schema; all properties are workspace-configurable attributes.
- **Source**: Attio public API v2 — `GET /v2/objects/companies/records/{id}` returns
  `data.values` as a map of attribute slugs to typed value objects; there is no hard-coded
  `name` property, only a configured `name` attribute.
- **Reason rejected**: The `Company` entity in closeloop has fixed, typed, named fields
  (`Name`, `Domain`, `Industry`, `OwnerId`) enforced at the C# domain layer. Adopting a generic
  attribute map would require the domain layer to treat company fields as untyped key-value pairs,
  eliminating compile-time safety, breaking EF Core's strongly-typed configuration, and
  complicating API validation. Attio's approach is powerful for a flexible workspace platform but
  is the wrong trade-off for a focused domain model that intentionally limits the field set to
  the four properties proven necessary for SMB sales.
