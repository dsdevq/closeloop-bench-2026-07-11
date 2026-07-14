# CRM Pipelines feature — reference research

This artifact captures the design-input research for the **Pipelines feature slice** in closeloop:
pipeline CRUD, stage ordering and management, win-probability per stage, multiple-pipeline support,
default pipeline semantics, and the forecasting/reporting API surface built on top of pipeline
structure.

The domain objects (`Pipeline`, `PipelineStage`) are settled in `.devclaw/research/domain-model.md`
and implemented in `backend/Domain/Entities/Pipeline.cs` and `PipelineStage.cs`. This artifact
focuses on **surface-layer design**: the API contract for pipeline and stage management, how
pipelines are selected in the deal board context, what aggregate/reporting data the pipeline
endpoints should expose, and how stage reordering and archival are handled.

---

## Feature scope (synthesis target)

| Surface | What is designed here |
|---|---|
| **Pipeline CRUD** | `GET /pipelines`, `POST /pipelines`, `PATCH /pipelines/{id}`, `DELETE /pipelines/{id}` |
| **Stage management** | `POST /pipelines/{id}/stages`, `PATCH /pipelines/{id}/stages/{stageId}`, stage reorder |
| **Default pipeline** | Which pipeline is shown by default on the deal board; how to change it |
| **Pipeline metrics** | Per-pipeline and per-stage deal counts + aggregate amounts in `GET /pipelines/{id}` |
| **Stage archival** | Retiring a stage without deleting deals that reference it |
| **Deal board integration** | How the deal board `GET /deals?pipelineId={id}&view=board` integrates with pipeline/stage data |

---

## Sources consulted

- **Salesforce** — `Opportunity.StageName` picklist field and Sales Process configuration in
  Salesforce Setup; `RecordType` + `Sales Process` combination as the mechanism for per-record-type
  stage subsets; `GET /services/data/vXX/sobjects/OpportunityStage` (picklist metadata object);
  Salesforce Forecasting categories (`ForecastCategory` field on Opportunity). Examined via
  Salesforce Trailhead documentation, SOAP API Schema Browser, and Salesforce Help articles on
  "Sales Processes and Stages".

- **HubSpot** — CRM API v3 Pipelines documentation:
  `GET /crm/v3/pipelines/{objectType}` (list pipelines for an object type, e.g. `deals`),
  `POST /crm/v3/pipelines/{objectType}` (create pipeline),
  `GET /crm/v3/pipelines/{objectType}/{pipelineId}/stages` (list stages),
  `POST /crm/v3/pipelines/{objectType}/{pipelineId}/stages` (create stage),
  `PATCH /crm/v3/pipelines/{objectType}/{pipelineId}/stages/{stageId}` (update stage including
  `metadata.probability` and `displayOrder`), and the concept of a "default" pipeline set via
  the CRM object settings API. Examined via `developers.hubspot.com` public API reference.

- **Pipedrive** — REST API v1 reference for Pipelines and Stages:
  `GET /v1/pipelines` (list with `order_nr` per pipeline), `POST /v1/pipelines` (create),
  `PUT /v1/pipelines/{id}` (update including `is_active` soft-archive flag),
  `GET /v1/stages` (list stages with `pipeline_id`, `order_nr`, `rotten_flag`, `rotten_days`),
  `POST /v1/stages` (create stage), `PUT /v1/stages/{id}` (update stage order, win probability,
  rotten config), and `GET /v1/pipelines/{id}/deals` (deals summary per stage). Examined via
  `developers.pipedrive.com` public API docs and the Pipedrive pipeline configuration UI
  documentation.

- **Attio** — Public API v2 documentation for Lists (Attio's pipeline construct):
  `GET /v2/lists` (list all pipeline-shaped lists), `GET /v2/lists/{list_id}/stages` (stages for
  a list), `PATCH /v2/lists/{list_id}/stages/{stage_id}` (update stage name or order),
  and Attio's "milestone stages" concept (stages that mark a significant gate vs. a normal
  progression step). Examined via `developers.attio.com` API reference and Attio's public product
  changelog on "List stages".

- **Zoho CRM** — Developer documentation on Stage and Probability configuration in the Deals
  module: `GET /crm/v2/settings/stages?module=Deals` (stage picklist values with probability
  and forecast category); `PUT /crm/v2/settings/stages` (update stage probabilities); and Zoho's
  "Stage Probability" concept where each stage has a `probability` (0–100) and a `forecast_type`
  (`Pipeline`, `Best Case`, `Commit`, `Closed`). Examined via `www.zoho.com/crm/developer/docs`
  public API reference and Zoho CRM help centre documentation on sales forecasting.

---

## Borrowed

### 1. Pipedrive's first-class Pipeline resource with `orderNr` + stage `order` as explicit integer fields

- **What**: `Pipeline` is a named first-class resource (`GET /pipelines/{id}`) with its own `Id`,
  `Name`, and an `orderNr` (display order among multiple pipelines, used to determine which
  pipeline appears first in the selector). Each `Stage` child resource carries an `Order: int`
  (0-based, or 1-based — closeloop uses 0-based matching `PipelineStage.Order`). Stage order
  changes are made via `PATCH /pipelines/{id}/stages/{stageId}` with a new `order` value;
  the endpoint normalises sibling stage orders to remove gaps (i.e. shifting affected stages up
  or down) within the same pipeline transaction.
- **From**: Pipedrive REST API v1 — `Pipeline.order_nr` field; `Stage.order_nr` field; Pipedrive
  API documentation explicitly states that updating a stage's `order_nr` shifts sibling stages
  automatically. The domain model's `Pipeline` + `PipelineStage` with `Stage.Order: int` directly
  maps this (see `.devclaw/research/domain-model.md` Borrowed §2).
- **Why it fits**: The `Pipeline._stages` backing field (ordered via `Stages.OrderBy(s => s.Order)`)
  already encodes order as a persistent integer. Normalising sibling orders on a stage-move is a
  repository-layer operation (UPDATE SET order = order ± 1 WHERE pipelineId = ? AND order BETWEEN
  ? AND ?) — no domain invariant changes needed. `Pipeline.orderNr` can be added as a field if
  multi-pipeline reordering becomes a UI requirement; for MVP, pipelines can be listed by
  `CreatedAt` ascending (first created = default).

### 2. HubSpot's `metadata.probability` per stage, exposed as `winProbability: int (0–100)`

- **What**: Each stage in the pipeline management API carries `winProbability: int` (0–100).
  The `GET /pipelines/{id}` response embeds stage metadata including `winProbability` so the
  pipeline configuration UI can render a probability ladder for the pipeline. The stage-update
  endpoint `PATCH /pipelines/{id}/stages/{stageId}` accepts `{ winProbability: int }` to let
  admins calibrate the forecast model. Two sentinel values have semantic meaning: `winProbability
  = 0` marks a terminal-lost stage (e.g. "Closed Lost"); `winProbability = 100` marks a
  terminal-won stage (e.g. "Closed Won"). These two sentinels drive the `isOverdue` deal
  computation (see `.devclaw/research/deals.md` Borrowed §5).
- **From**: HubSpot CRM API v3 — `GET /crm/v3/pipelines/deals/{pipelineId}/stages` response;
  each stage object includes `metadata: { probability: "0.4" }` (a decimal string, 0.0–1.0);
  HubSpot's pipeline stage documentation notes that `probability = 1.0` marks "Closed Won" and
  `probability = 0.0` marks "Closed Lost" by convention.
- **Why it fits**: `PipelineStage.WinProbability: int?` is already in the domain model (nullable,
  0–100). Making the 0 and 100 sentinels load-bearing for the won/lost distinction avoids
  introducing a separate `IsClosed` / `IsWon` bool field on PipelineStage — the probability
  already encodes this semantically. The API should validate `winProbability ∈ [0, 100]` (already
  enforced by `PipelineStage.Create`) and document the 0/100 conventions.

### 3. HubSpot's per-pipeline deal-count and aggregate-amount metrics embedded in `GET /pipelines/{id}`

- **What**: The `GET /pipelines/{id}` response includes a `metrics` object computed at query time:
  ```json
  {
    "metrics": {
      "totalDeals": 47,
      "openDeals": 31,
      "wonDeals": 12,
      "lostDeals": 4,
      "totalAmount": 1250000.00,
      "weightedAmount": 487500.00
    },
    "stages": [
      {
        "id": "...", "name": "Proposal", "order": 2, "winProbability": 40,
        "dealCount": 8, "stageAmount": 320000.00, "weightedAmount": 128000.00
      },
      ...
    ]
  }
  ```
  This allows the pipeline configuration view and the manager forecast board to display a
  full pipeline health snapshot in one request without querying the deals endpoint separately.
- **From**: HubSpot CRM — the Pipelines API and the HubSpot forecast board both surface per-stage
  deal counts and amounts; HubSpot's `GET /crm/v3/pipelines/deals/{pipelineId}` paired with
  the deals search API is used to power the HubSpot forecast view. Pipedrive's
  `GET /v1/pipelines/{id}/deals` also returns a `deals_summary` per stage.
- **Why it fits**: A pipeline configuration page and a manager's forecast view are two of the
  most important non-deal screens in the CRM. Computing these metrics server-side (via SQL
  GROUP BY `PipelineStageId`) prevents N+1 round-trips and is efficiently served by the same
  index that backs the deal Kanban board query. The `weightedAmount` column drives the revenue
  forecast — computed as `SUM(Amount * WinProbability / 100.0)`.

### 4. Pipedrive's `isActive` soft-archive flag for retiring a stage without orphaning deals

- **What**: A stage can be retired without deleting it by setting `isActive: false` via
  `PATCH /pipelines/{id}/stages/{stageId}`. An inactive stage is hidden from the deal board
  column selector and the pipeline configuration UI, but any deals currently in that stage remain
  valid (their `PipelineStageId` FK still resolves). The stage continues to appear in historical
  data (stage transition history, reporting). A stage with live deals cannot be hard-deleted —
  the DELETE endpoint returns 409 if `dealCount > 0`.
- **From**: Pipedrive REST API v1 — `Stage.active_flag` (boolean); Pipedrive help centre
  documentation "Archiving a pipeline stage"; Pipedrive's DELETE stage endpoint returns an error
  if the stage has open deals. HubSpot uses a similar approach: stages cannot be deleted while
  deals reference them; they must be archived first by moving all deals to another stage.
- **Why it fits**: The EF Core configuration uses `DeleteBehavior.Restrict` on the
  Deal→PipelineStage FK (AGENTS.md Key decisions), meaning a hard-DELETE of a stage with deals
  is already blocked at the database level. The soft-archive pattern (`isActive: bool` on
  `PipelineStage`) provides a clean API surface for this: soft-delete hides the stage from the
  UI, hard-delete is only permitted when `dealCount = 0`. Adding `IsActive: bool` to the
  `PipelineStage` entity is a non-breaking schema extension.

### 5. Attio's "milestone stage" concept surfaced as a visual gate in the pipeline board

- **What**: Certain stages in a pipeline are designated as "milestone" stages — visually
  distinguished in the board (e.g. a divider line, a flag icon on the stage header) to mark
  significant qualitative gates (e.g. "Demo Completed" is a milestone; "Initial Outreach" is not).
  In the API this is represented as `isMilestone: bool` on the stage. The board highlights
  deals that have crossed the most recent milestone stage, giving managers a quick count of
  "deals past the demo gate."
- **From**: Attio public API v2 — List stages include a `is_milestone` property; Attio's board
  view renders milestone stages with a distinct visual marker (a pin icon); Attio's product
  changelog post "Milestone stages" (2024).
- **Why it fits**: A `IsMilestone: bool` on `PipelineStage` is a purely additive, nullable-safe
  field (defaults `false`). It requires no domain invariant change, costs one column in the
  Stages table, and enables the board UI to render a qualitative progress marker without a
  separate "stage type" enum. For MVP the field can be set by admins via
  `PATCH /pipelines/{id}/stages/{stageId}` and exposed in the stages array on the pipeline detail
  response. Pipeline metrics (Borrowed §3) can include a `dealsPostMilestone: int` aggregate.

---

## Rejected & why

### A. Salesforce's implicit pipeline model (StageName as a flat picklist, no Pipeline entity)

- **What was considered**: Encoding pipeline stage as a string picklist on the Deal record with
  no separate Pipeline entity. The available stages are globally configured at the system level;
  per-pipeline subsets are managed via Record Types + Sales Processes.
- **Source**: Salesforce — `Opportunity.StageName` picklist; Sales Process configuration in Setup;
  `GET /sobjects/OpportunityStage` picklist metadata object.
- **Reason rejected**: Already argued at the domain level (`.devclaw/research/domain-model.md`
  Rejected §B). Without a first-class `Pipeline` entity, multi-pipeline support requires Record
  Types — an indirection layer notorious for configuration complexity. closeloop needs to support
  multiple distinct pipelines from day one (sales, renewal, partnership). The `Pipeline` entity
  is a data change, not a schema migration; adding a second pipeline is `POST /pipelines`.
  Salesforce's approach is the wrong trade-off for any CRM that needs more than one pipeline.

### B. Zoho's `forecastType` (Pipeline / Best Case / Commit / Closed) as a stage attribute

- **What was considered**: Adding a `forecastCategory` string field to each stage (values:
  `Pipeline`, `BestCase`, `Commit`, `Closed`) so that the forecasting view can segment deals
  by forecast commitment level (not just by win probability). Zoho's forecast view groups deals
  by `forecastType` across all stages, enabling a manager to distinguish "committed" revenue
  from "upside."
- **Source**: Zoho CRM — `Stage.forecast_type` field in `GET /crm/v2/settings/stages`; Zoho
  CRM forecast view documentation grouping deals by forecast category.
- **Reason rejected**: Forecast categories add a second categorical dimension orthogonal to
  stage order, requiring UI surfaces to display a two-axis grouping (stage × forecast category).
  closeloop's forecasting model uses `winProbability` per stage as a single scalar that already
  encodes the commitment spectrum (Borrowed §2). A `WinProbability = 80` stage is implicitly
  "Best Case"; `WinProbability = 90` is implicitly "Commit". Introducing a separate
  `forecastCategory` enum before the basic forecasting surface exists is premature complexity.
  The weighted-amount aggregate (Borrowed §3) is the correct MVP forecasting primitive.

### C. HubSpot's multi-pipeline-per-deal model (moving a deal from one pipeline to another)

- **What was considered**: Allowing a deal to be re-assigned to a different pipeline by patching
  `Deal.PipelineId` to point to a different pipeline. HubSpot supports this via
  `PATCH /crm/v3/objects/deals/{id}` with `{ "pipeline": "<newPipelineId>", "dealstage": "<stageInNewPipeline>" }`.
  A deal can move from "Sales Pipeline" to "Renewal Pipeline" without creating a new deal.
- **Source**: HubSpot CRM API v3 — `PATCH /crm/v3/objects/deals/{id}` documentation; HubSpot
  help centre article "Move a deal to a different pipeline"; HubSpot's Deals board pipeline-
  selector dropdown that allows dragging or moving a deal to a different pipeline.
- **Reason rejected**: Changing a deal's pipeline is semantically ambiguous: the deal's stage
  history (`StageTransition` log) was accumulated against the old pipeline's stage IDs; the
  new pipeline's stages are different records. Moving the deal would orphan its transition
  history (the `FromStageId`/`ToStageId` FKs point to stages in the old pipeline, which remain
  valid records but are no longer the deal's pipeline). The cleaner product behaviour is to close
  the deal in the old pipeline (mark lost or won) and open a new deal in the target pipeline.
  Supporting pipeline-switching adds edge-case complexity (what happens to StageTransitions?
  what is the initial stage in the new pipeline?) with no compelling MVP use case.

### D. Pipedrive's per-pipeline `rotten_days` configuration

- **What was considered**: Adding a `rottenDays: int?` field to the `Pipeline` entity so each
  pipeline has its own deal-rotting threshold. A pipeline for long-cycle enterprise deals might
  use 30 days; a pipeline for transactional SMB deals might use 7 days. The rotting indicator
  on deal cards (see `.devclaw/research/deals.md` Borrowed §3) would use the deal's pipeline's
  `rottenDays` threshold rather than a system-wide default.
- **Source**: Pipedrive REST API v1 — `Stage.rotten_flag` (bool) and `Stage.rotten_days` (int)
  on each stage; stages can each have independent rotting thresholds; Pipedrive help centre
  documentation on "Rotting deals".
- **Reason rejected**: Pipedrive's rotting threshold is per-stage, not per-pipeline — which makes
  it even more granular than the per-pipeline approach considered here. Granular threshold
  configuration requires admin UI surfaces (a rotting-days input per pipeline, or per stage) that
  are not in scope for MVP. The MVP rotting indicator will use a system-wide default (e.g. 14
  days, configurable via an environment variable) applied uniformly across all pipelines. Per-
  pipeline threshold configuration can be introduced via a `Pipeline.RottenDays: int?` field
  once the basic rotting feature is shipped and user feedback justifies differentiation.

### E. Attio's list-as-pipeline-for-any-object (generic pipeline not scoped to Deals)

- **What was considered**: Making the pipeline construct generic so that a Company, Contact, or
  any future object type can be put into a pipeline-shaped list with stages and transitions.
  This would allow, for example, a "Hiring Pipeline" for candidates (Contact-type entries) or a
  "Partner Onboarding Pipeline" for companies (Company-type entries), all using the same
  Pipeline + Stage + StageTransition infrastructure.
- **Source**: Attio public API v2 — `List.entry_type` field specifying which object type (People,
  Companies, custom objects) can be entries in a given list; Attio's product positioning
  "Works for any object" for list/pipeline features.
- **Reason rejected**: Already argued at the domain level (`.devclaw/research/domain-model.md`
  Rejected §C). closeloop is explicitly deal-centric. Generalising pipelines to arbitrary object
  types requires a `PipelineEntryType` discriminator, separate entry tables or polymorphic
  foreign keys, and separate board views per object type. This is correct architecture for a
  general-purpose data platform (Attio's positioning) and over-engineered for a focused sales
  CRM. The `Deal.PipelineId` FK is the correct coupling; "pipeline" in closeloop unambiguously
  means a Deal pipeline.
