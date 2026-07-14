# CRM Deals feature — reference research

This artifact captures the design-input research for the **Deals feature slice** in closeloop:
the pipeline Kanban board view, the deal list/table view, the deal detail record (fields, related
contacts, stage history, activities), stage-progression mechanics, revenue forecasting signals,
and deal health indicators ("rotting").

The domain object (`Deal`, `Pipeline`, `PipelineStage`, `StageTransition`) is settled in
`.devclaw/research/domain-model.md`. This artifact focuses on **surface-layer design**: how deals
are surfaced in the Kanban and list views, what the detail record exposes, and what write
affordances the API must support.

---

## Feature scope (synthesis target)

| Surface | What is designed here |
|---|---|
| **Kanban board** | Column-per-stage layout, deal card fields, drag-to-advance semantics, stage totals |
| **List / table view** | Column set, filter by stage/owner/close date, sort model, pagination |
| **Detail view** | Fields shown, stage history timeline, related contacts/activities, inline edit |
| **Stage progression** | `PATCH /deals/{id}/stage` endpoint shape; pipeline-membership validation |
| **Forecasting signals** | Close date, win probability per stage, overdue detection |
| **Deal health** | "Rotting" (no activity in N days) indicator on Kanban cards and list rows |

---

## Sources consulted

- **Salesforce** — `Opportunity` standard object documentation in the Salesforce Object Reference
  (public developer docs); `StageName` picklist, `CloseDate`, `Amount`, `Probability`,
  `ForecastCategory` fields; `GET /services/data/vXX/sobjects/Opportunity/{id}` and the
  Salesforce Kanban view for Opportunities in Lightning Experience. Examined via Salesforce
  Trailhead documentation, the Salesforce SOAP Schema Browser, and the public Lightning Experience
  Kanban documentation.

- **HubSpot** — CRM API v3 public documentation for Deals: `GET /crm/v3/objects/deals` (list
  with cursor pagination and `properties` projection), `GET /crm/v3/objects/deals/{id}` (detail
  with stage, pipeline, associations expansion), `PATCH /crm/v3/objects/deals/{id}` (partial
  update including stage change), the HubSpot Pipelines API (`GET /crm/v3/pipelines/deals`,
  `GET /crm/v3/pipelines/deals/{pipelineId}/stages`), and HubSpot's "Deal Rotting" feature
  (configured per pipeline as a day threshold). Examined via `developers.hubspot.com` public API
  reference and the HubSpot CRM deals board UI.

- **Pipedrive** — REST API v1 reference for the `Deals` object:
  `GET /v1/deals` (list with `filter_id`, `stage_id`, `pipeline_id`, `sort`, `start`, `limit`),
  `GET /v1/deals/{id}` (detail with embedded `stage_id`, `pipeline_id`, person, organization,
  activity counts), `PUT /v1/deals/{id}` (update including stage change), the Kanban board
  documented in the Pipedrive help centre, and the deal "Expected Close Date" and "Won Time" /
  "Lost Time" fields. Examined via `developers.pipedrive.com` public API docs and the Pipedrive
  sales pipeline help documentation.

- **Attio** — Public API v2 documentation for Lists (Attio's pipeline construct): list entries
  `GET /v2/lists/{list_id}/entries`, `PATCH /v2/lists/{list_id}/entries/{entry_id}` (stage
  update), the `stage_history` array on list entries (each entry records `from_stage_id`,
  `to_stage_id`, `changed_at`), and Attio's board view for List Entries. Examined via
  `developers.attio.com` API reference and Attio's public changelog post on "List stages and
  stage history" (2023).

- **Zoho CRM** — Developer documentation for the Deals module (Potentials in older Zoho docs):
  `GET /crm/v2/Deals` (list), `GET /crm/v2/Deals/{id}` (detail including Stage, Probability,
  Expected Revenue, Close Date fields), `POST /crm/v2/Deals/{id}/actions/convert`
  (won/lost marking), and Zoho Blueprint (stage-gate workflow constraints requiring mandatory
  fields or approvals before advancing). Examined via `www.zoho.com/crm/developer/docs` public
  API reference and Zoho CRM help centre documentation for the Deals module.

---

## Borrowed

### 1. Pipedrive's Kanban board as the primary pipeline view, with column-per-stage totals

- **What**: The default Deal view is a Kanban board where each column corresponds to one pipeline
  stage. Each deal card shows: Title, Amount (formatted), associated Company name, associated
  Contact name, and a Close Date badge (red if overdue). Each column header shows the stage name,
  count of deals in stage, and aggregate `Amount` sum for the stage. The board is scoped to one
  pipeline at a time; a pipeline selector at the top switches context. Drag-and-drop a card
  between columns fires `PATCH /deals/{id}/stage` with the target stage ID.
- **From**: Pipedrive — the Pipedrive Pipeline view is the canonical Kanban-first sales board;
  their API's deal response includes `stage_id`, `pipeline_id`, `title`, `value`, `person_name`,
  `org_name` as top-level flat fields (not nested objects), enabling efficient card rendering. The
  column aggregate (`deals_summary` in Pipedrive's `GET /v1/deals` response) provides per-stage
  totals without a separate request.
- **Why it fits**: The Deal entity's `PipelineId`/`PipelineStageId` FK pair directly supports the
  column assignment. The `Pipeline.Stages` collection's `Order` field provides deterministic
  left-to-right column ordering. Embedding flat Company/Contact name strings on the deal card
  response (projected at read time, not FK-resolved by the client) keeps the board rendering to
  one API call: `GET /deals?pipelineId={id}&view=board`.

### 2. HubSpot's per-stage win probability driving revenue forecasting

- **What**: Each pipeline stage carries a `WinProbability` (0–100) that propagates to the deal
  as a derived `weightedAmount = Amount × (WinProbability / 100)`. The deals list and
  forecasting view shows both the raw Amount and the weighted Amount. The `WinProbability` on a
  stage is an editable field on the Stage resource, not computed from historical won-rate data.
- **From**: HubSpot CRM API v3 — each stage in `GET /crm/v3/pipelines/deals/{pipelineId}/stages`
  has a `metadata.probability` property (0.0–1.0). Deals inherit `hs_deal_stage_probability`
  from the stage; the HubSpot forecast view uses this to project weighted revenue per stage.
- **Why it fits**: The `PipelineStage.WinProbability: int?` field is already in the domain model
  (`.devclaw/research/domain-model.md` Borrowed §2). The API detail response for a deal should
  compute and surface `weightedAmount` as a read-only derived field so the forecast board can
  sum weighted amounts per stage without client-side computation. The stage's `WinProbability`
  is writeable via `PATCH /pipelines/{id}/stages/{stageId}`.

### 3. HubSpot's "rotting" indicator: no-activity threshold per pipeline, flagged on deal cards

- **What**: Each pipeline can be configured with a "deal rotting" threshold in days (e.g. 14).
  Any deal in that pipeline that has had no new `Activity` anchored to it for more than the
  threshold days is considered "rotting" and carries a `isRotting: true` flag in the API response
  and a visual indicator (a decaying icon in HubSpot's UI) on the Kanban card. The flag is
  computed server-side from `max(Activity.OccurredAt) where DealId = this.Id` vs. `now()`.
- **From**: HubSpot CRM — "Deal Rotting" feature, configurable in Pipeline settings; the
  `hs_deal_stage_probability_shadow_del` and `notes_last_updated` properties; HubSpot's public
  blog post "What is deal rotting in HubSpot CRM" and the pipeline settings documentation.
- **Why it fits**: "Which deals have gone cold?" is a primary manager-level CRM question. The
  `Activity` table already stores `OccurredAt` and `DealId`. A simple SQL aggregation
  (`MAX(OccurredAt) WHERE DealId = ?`) can determine last-activity date at query time and compare
  to the pipeline's rotting threshold. Adding `lastActivityAt: DateTime?` and `isRotting: bool`
  to the deal list/board response costs one join and requires no domain-layer change.

### 4. Attio's `stageHistory` array on deal detail for time-in-stage analytics

- **What**: The deal detail response includes a `stageHistory` array:
  `[{ fromStageId, toStageId, enteredAt, exitedAt, durationDays }]` — one entry per stage
  transition. This is projected from the append-only `StageTransition` child collection
  (see `.devclaw/research/domain-model.md` Borrowed §3) and lets the UI render a timeline of
  the deal's journey through the pipeline stages without building a separate timeline query.
- **From**: Attio public API v2 — `GET /v2/lists/{list_id}/entries/{entry_id}` returns a
  `stage_history` array with `{ from_stage_id, to_stage_id, changed_at }` per transition; Attio's
  board view uses this to compute "time in current stage" displayed on the entry card.
- **Why it fits**: The `StageTransition` table is append-only (`DealId`, `FromStageId`,
  `ToStageId`, `ChangedAt`, `ChangedBy` — see AGENTS.md Key decisions). Projecting this into a
  `stageHistory` array in the deal detail response is a pure read-side aggregation. "Days in
  current stage" can be computed as `now() - stageHistory.last().enteredAt`, surfaced as
  `daysInCurrentStage: int` on the deal card to warn reps when a deal is stalled even before
  it crosses the rotting threshold.

### 5. Salesforce's `CloseDate` field with overdue flagging in the list and Kanban view

- **What**: Every Deal has a `CloseDate: DateOnly?` representing the expected close. In the list
  and Kanban views, deals whose `CloseDate` is in the past and whose stage is not Won/Lost are
  flagged as overdue with a red date badge. The API deal response includes `isOverdue: bool`
  computed as `CloseDate < today AND status ∉ {Won, Lost}`. A default sort of
  `CloseDate ASC NULLS LAST` surfaces the most urgent deals first in the table view.
- **From**: Salesforce — `Opportunity.CloseDate` (required on every Opportunity); the Lightning
  Kanban view applies a red background to overdue opportunity cards; the Salesforce list view
  allows sorting by CloseDate. Pipedrive's `expected_close_date` field on Deals follows the same
  pattern with visual urgency cues in the pipeline view.
- **Why it fits**: The `Deal` entity currently carries no close-date field; this should be added
  as `CloseDate: DateOnly?` (optional, nullable, because not all early-stage deals have a commit
  date). Adding `isOverdue` as a computed property in the API response (no domain-layer enum for
  won/lost yet — treat as "has CloseDate in past and is not in a stage whose WinProbability = 100
  or = 0") gives the list view its urgency signal without changing the domain invariants.

---

## Rejected & why

### A. Salesforce's multi-currency Amount (Opportunity `CurrencyIsoCode` + converted amounts)

- **What was considered**: Adding a `CurrencyIsoCode` field to the Deal entity so that deals in
  different currencies (USD, EUR, GBP) can be stored with their native currency, with a
  system-wide base currency and conversion rates applied at reporting time. Salesforce's Opportunity
  carries both `Amount` (in deal currency) and `Amount_converted` (in org base currency).
- **Source**: Salesforce — `Opportunity.CurrencyIsoCode` and `Amount` with the Salesforce
  Multi-Currency feature; `GET /sobjects/Opportunity/{id}` with currency fields.
- **Reason rejected**: Multi-currency requires a currency-rates table, a base-currency org
  setting, conversion at read time for aggregates, and UI currency selectors. closeloop's target
  customer (SMB sales teams ≤ 50 seats) overwhelmingly operates in a single currency. The `Amount`
  field remains a plain `decimal` (no currency code); a currency layer can be introduced as a
  schema migration when internationalisation is a product requirement.

### B. HubSpot's line-items-on-deals model (Products associated to Deals with quantity and price)

- **What was considered**: Associating product line items to a deal so that `Amount` is computed
  as `SUM(quantity × unitPrice)` across the deal's associated products, rather than being a
  manually entered number. HubSpot exposes `GET /crm/v3/objects/line_items` and links them to
  deals via the Associations API.
- **Source**: HubSpot CRM API v3 — Line Items object; `POST /crm/v3/objects/line_items` with
  `hs_product_id`, `quantity`, `price`; association between Deal and Line Items.
- **Reason rejected**: Product catalogue management is a separate product surface (SKUs, pricing
  tiers, discount rules) that is not in scope for a focused sales CRM MVP. The `Deal.Amount` field
  as a manually entered total is the correct abstraction for SMB sales where deals are often
  negotiated as a single custom number, not itemised. Line items can be added in a CPQ-adjacent
  feature sprint if the product expands toward product-led sales.

### C. Zoho Blueprint: stage-gate workflow constraints requiring mandatory fields before advancing

- **What was considered**: Configurable per-stage mandatory fields — e.g. "a deal cannot advance
  from Proposal to Negotiation without a non-null `Amount`" — enforced by a state-machine workflow
  engine (Zoho's "Blueprint"). The Blueprint fires validation at stage-change time and returns an
  error listing missing required fields if the gate is not cleared.
- **Source**: Zoho CRM — Blueprint feature under CRM configuration; `POST /crm/v2/Deals/{id}/actions/convert`
  and stage-transition validation; Zoho CRM developer documentation on "Blueprint transitions".
- **Reason rejected**: Stage-gate rules are a CRM administrator configuration concern, not a
  domain invariant. closeloop's current domain layer validates that a new stage belongs to the
  same pipeline as the deal (the `AdvanceTo` method in `Deal.cs`) — that is the correct hard
  invariant. Soft "please fill in Amount before proceeding" guidance is better delivered as
  frontend UX (a warning tooltip) than a backend gate that blocks API calls. A configurable
  workflow engine at the API level requires a rule-storage schema, an evaluation engine, and a
  configuration UI — disproportionate complexity for MVP.

### D. Pipedrive's deal "activity count" displayed as a deal summary metric instead of a feed

- **What was considered**: Surfacing activity engagement as a single integer counter
  (`activitiesCount: 5`) on the deal card rather than surfacing recent activities by type.
  Pipedrive's Kanban card shows `Activities: 3` as a plain count; the Pipedrive deal detail
  shows a count-by-type breakdown (2 calls, 1 email).
- **Source**: Pipedrive REST API v1 — `GET /v1/deals/{id}` includes `activities_count`,
  `done_activities_count`, `undone_activities_count`; deal Kanban cards show an activity badge
  count.
- **Reason rejected**: A raw count is less informative than type-aware recency. "2 activities"
  does not tell the rep whether the last touch was a cold-outreach note two weeks ago or a hot
  meeting yesterday. The preferred pattern (Borrowed §3: rotting indicator + Attio's
  `lastActivityAt`) conveys the same staleness signal with meaningful semantics. The detail view
  embeds the last 5 activities by type (Note, Call, Email, Meeting, Task) as actual records, which
  is richer than a count. A count badge can be derived client-side from the embedded `activities`
  slice without a separate API field.

### E. Attio's deal-as-list-entry with no first-class Title field (all metadata as entry attributes)

- **What was considered**: Treating deals as entries in a pipeline-shaped "List" with no built-in
  Title field — all metadata (company, amount, contact) stored as configurable entry attributes
  with no required schema. Attio List Entries have no hard-coded `title`; the display name is
  derived from a configured "name attribute".
- **Source**: Attio public API v2 — `GET /v2/lists/{list_id}/entries/{entry_id}` returns
  `data.values` as an attribute map; no top-level `title` field; Attio List documentation on
  "Display attribute" configuration.
- **Reason rejected**: The `Deal` entity needs a human-readable title for display in Kanban cards,
  search results, and activity log references ("Stage changed on Deal: Acme Corp Renewal Q3").
  Without a first-class `Title: string` field the API caller must know which attribute slug maps
  to the display name, creating coupling to workspace configuration. The closeloop domain model
  has a defined, compile-time-safe `Title` field. Removing it in favour of an attribute map would
  regress type safety and make cross-entity search (e.g. "deals matching 'Acme'") require a
  generic attribute-value query instead of a simple column predicate.
