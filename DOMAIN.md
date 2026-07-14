# DOMAIN.md — closeloop CRM domain model v1

This document synthesizes the five core domain entities — **Contacts**, **Companies**, **Deals**,
**Activities**, and **Pipelines** — covering the shipped C# domain layer, what was borrowed from
reference CRMs, what was rejected and why, and how the entities relate to each other.

Primary research artifacts (read these for source-level detail):
- `.devclaw/research/domain-model.md` — cross-cutting domain structure decisions
- `.devclaw/research/contacts.md` — Contacts surface-layer design
- `.devclaw/research/companies.md` — Companies surface-layer design
- `.devclaw/research/deals.md` — Deals surface-layer design
- `.devclaw/research/activities.md` — Activities surface-layer design
- `.devclaw/research/pipelines.md` — Pipelines surface-layer design

---

## Implementation status

| Entity | Domain layer | EF Core config | API endpoints |
|---|---|---|---|
| **Contact** | `backend/Domain/Entities/Contact.cs` | `ContactConfiguration.cs` | **SHIPPED** (`/contacts` GET + POST) |
| **Company** | `backend/Domain/Entities/Company.cs` | `CompanyConfiguration.cs` | **API pending** |
| **Deal** | `backend/Domain/Entities/Deal.cs` | `DealConfiguration.cs` | **API pending** |
| **Activity** | `backend/Domain/Entities/Activity.cs` | `ActivityConfiguration.cs` | **API pending** |
| **Pipeline** | `backend/Domain/Entities/Pipeline.cs` | `PipelineConfiguration.cs` | **API pending** |
| **PipelineStage** | `backend/Domain/Entities/PipelineStage.cs` | `PipelineStageConfiguration.cs` | **API pending** |

All six domain entities and their EF Core configurations are shipped. The `InitialCreate` migration
captures all six tables. Only the Contacts API feature (`backend/Api/Features/Contacts/`) has REST
endpoints; the Companies API feature (`backend/Api/Features/Companies/`) is now also implemented.
Deals, Activities, and Pipelines are **modeled, implementation pending** at the API layer.

---

## Entity models

### Contact — shipped (domain + API)

**Shipped fields** (`backend/Domain/Entities/Contact.cs`):

| Field | Type | Constraints |
|---|---|---|
| `Id` | `Guid` | PK, `protected init` (from `Entity` base) |
| `Name` | `string` | required, max 200 |
| `Email` | `string` | required, max 320, validated via `MailAddress`, unique per tenant |
| `Phone` | `string?` | optional, max 50 |
| `CompanyId` | `Guid?` | FK → Company, `SetNull` on company delete |

**Factory invariants**: `Name` and `Email` required and trimmed; `Email` validated via
`System.Net.Mail.MailAddress`. Created via `Contact.Create(name, email, phone, companyId)`.

**Borrowed from reference CRMs** (`.devclaw/research/contacts.md`, `.devclaw/research/domain-model.md`):

- **HubSpot**: Company-as-first-class object; Contact linked to Company via association rather
  than Contact being a sub-record of Account. Research proposed a `ContactCompanyLink` junction
  with `IsPrimary`; the shipped entity uses a simpler single `CompanyId` FK (see Implementation
  divergences below).
- **HubSpot**: `email` as the canonical deduplication key for CSV import (upsert semantics).
- **HubSpot**: Cursor-based pagination (`after` + `limit`) for the contacts list endpoint.
- **Salesforce**: Compact-layout "highlight" projection for list cards (`GET /contacts/{id}?view=compact`).
- **Pipedrive**: Orthogonal filter + sort + projection query parameters on list endpoint.
- **Attio**: `PATCH` partial-update for inline field editing (only changed fields in body).
- **Zoho**: `POST /contacts/actions/mass_update` batch-update pattern.

**Rejected**:
- **Salesforce Lead object** — pre-qualification limbo state; a Contact without a Deal already
  models an unqualified prospect. Full rejection rationale in `.devclaw/research/domain-model.md`
  Rejected §A and `.devclaw/research/contacts.md` Rejected §A.
- **Pipedrive multi-email typed array** — single `Email` string covers SMB workflows;
  multi-value array would require a `ContactEmail` child collection and complicate deduplication
  (`.devclaw/research/contacts.md` Rejected §C).
- **Zoho tabbed Info/Timeline split** — forces navigation overhead; unified detail with embedded
  `recentActivities` preferred (`.devclaw/research/contacts.md` Rejected §E).
- **HubSpot separate `POST /contacts/search`** — filter encoded as query params on `GET /contacts`
  keeps the surface minimal (`.devclaw/research/contacts.md` Rejected §B).

---

### Company — implemented (backend model + migration + API + tests, frontend component + spec)

**Shipped fields** (`backend/Domain/Entities/Company.cs`):

| Field | Type | Constraints |
|---|---|---|
| `Id` | `Guid` | PK |
| `Name` | `string` | required, max 200 |
| `Domain` | `string?` | optional, max 255; deduplication key when non-null |
| `Industry` | `string?` | optional, max 100 |
| `OwnerId` | `Guid` | required, non-empty |

**Factory invariants**: `Name` and `OwnerId` required and validated. Created via
`Company.Create(name, domain, industry, ownerId)`.

**Borrowed from reference CRMs** (`.devclaw/research/companies.md`,
`.devclaw/research/domain-model.md`):

- **HubSpot**: `domain` (e.g. `acme.com`) as the canonical deduplication key — a 409 on creation
  if a company with the same domain already exists, enabling import/sync deduplication.
- **HubSpot**: Company as a first-class named object distinct from "Account"; Contact linked via
  association, not as a sub-record. Named `Company` (not `Organization`) to match HubSpot/Attio/Zoho
  terminology and SMB user mental model (see `.devclaw/research/domain-model.md` Rejected §F).
- **Pipedrive**: Inline summary counts (`openDealsCount`, `contactsCount`, `activitiesCount`) on
  the list response to enable deal-count chips on company cards without N+1 requests.
- **Pipedrive**: Sub-resource pattern for paginated related records:
  `GET /companies/{id}/contacts`, `/deals`, `/activities`.
- **HubSpot**: `primaryContact` embedded in company detail response (symmetric counterpart to
  `primaryCompany` on contact detail).
- **Attio**: Unified cross-anchor activity feed on company detail — includes activities anchored
  to associated Contacts and Deals, not only direct `CompanyId` activities.

**Rejected**:
- **Salesforce Account hierarchy** (`ParentId` self-FK) — parent/subsidiary modelling is a
  mid-market/enterprise concern; SMB target has no hierarchy UI spec
  (`.devclaw/research/companies.md` Rejected §A).
- **HubSpot full merge workflow** — field-level merge resolution requires significant product
  scope; domain-dedup-key check prevents most duplicates at creation
  (`.devclaw/research/companies.md` Rejected §B).
- **Zoho tabbed Related Lists view** — same reasoning as Contacts: inline embedded preview plus
  sub-resource pagination preferred over tab navigation
  (`.devclaw/research/companies.md` Rejected §C).
- **Attio attribute-only model** (no first-class `Name` field) — generic attribute map eliminates
  compile-time field safety and complicates EF Core configuration
  (`.devclaw/research/companies.md` Rejected §E).

---

### Deal — modeled, API implementation pending

**Shipped fields** (`backend/Domain/Entities/Deal.cs`):

| Field | Type | Constraints |
|---|---|---|
| `Id` | `Guid` | PK |
| `Amount` | `decimal` | required, non-negative, precision (18, 4) |
| `PipelineId` | `Guid` | required FK → Pipeline, `Restrict` delete |
| `PipelineStageId` | `Guid` | required FK → PipelineStage, `Restrict` delete |
| `CompanyId` | `Guid?` | optional FK → Company, `SetNull` on company delete |
| `ContactId` | `Guid?` | optional FK → Contact, `SetNull` on contact delete |

**Domain methods**: `AdvanceTo(PipelineStage stage)` validates the target stage belongs to the
same pipeline as the deal before updating `PipelineStageId`.

> **Fields not yet in entity**: `Title: string` and `CloseDate: DateOnly?` are specified in
> the research model (`.devclaw/research/deals.md` Borrowed §5) and required for the Kanban
> card and overdue-detection features. They are API-layer requirements to be added when the
> Deals API feature is implemented.

**Borrowed from reference CRMs** (`.devclaw/research/deals.md`,
`.devclaw/research/domain-model.md`):

- **Pipedrive**: Kanban-board-first view (column per stage, deal card with Title + Amount +
  Company + Contact + CloseDate badge); per-stage deal counts and aggregate amounts in column
  headers.
- **HubSpot**: Per-stage `winProbability` (0–100) driving `weightedAmount` for revenue
  forecasting; sentinel values `0 = Closed Lost`, `100 = Closed Won`.
- **HubSpot**: Deal-rotting indicator — no-activity threshold per pipeline; `isRotting: bool`
  computed as `MAX(Activity.OccurredAt WHERE DealId = this.Id) < now() - threshold`.
- **Attio**: `stageHistory` array on deal detail — projected from the append-only `StageTransition`
  log (`.devclaw/research/domain-model.md` Borrowed §3); includes `{ fromStageId, toStageId,
  enteredAt, exitedAt, durationDays }`.
- **Salesforce**: `CloseDate` field with overdue flagging — `isOverdue: bool = CloseDate < today
  AND stage WinProbability ∉ {0, 100}`.

> **StageTransition entity**: AGENTS.md Key decisions references an append-only `StageTransition`
> table (`DealId, FromStageId, ToStageId, ChangedAt, ChangedBy`). A `StageTransition` domain
> entity is not yet present in `backend/Domain/Entities/`; it is a pending addition required for
> the Deals API and stage-history feature.

**Rejected**:
- **Salesforce multi-currency** (`CurrencyIsoCode`) — SMB target operates in a single currency;
  `Amount` is a plain `decimal` (`.devclaw/research/deals.md` Rejected §A).
- **HubSpot line-items** (product catalogue on deals) — out-of-scope product surface for MVP
  (`.devclaw/research/deals.md` Rejected §B).
- **Zoho Blueprint stage-gate workflow** — configurable mandatory-field gates are an admin
  configuration concern; `AdvanceTo` enforces the hard pipeline-membership invariant only
  (`.devclaw/research/deals.md` Rejected §C).
- **Attio deal-as-list-entry with no first-class Title** — `Title: string` is required for
  Kanban card labels and cross-entity search (`.devclaw/research/deals.md` Rejected §E).

---

### Activity — modeled, API implementation pending

**Shipped fields** (`backend/Domain/Entities/Activity.cs`):

| Field | Type | Constraints |
|---|---|---|
| `Id` | `Guid` | PK |
| `Type` | `ActivityType` (enum) | required |
| `Note` | `string` | required (empty string if omitted), max 2000 |
| `OccurredAt` | `DateTime` | required |
| `ContactId` | `Guid?` | anchor FK → Contact, `Restrict` delete |
| `CompanyId` | `Guid?` | anchor FK → Company, `Restrict` delete |
| `DealId` | `Guid?` | anchor FK → Deal, `Restrict` delete |

**`ActivityType` enum** (`backend/Domain/Entities/ActivityType.cs`):
`Note | Call | Email | Meeting | Task | StageChange`

**Factory invariants**: Exactly one of `ContactId`, `CompanyId`, `DealId` must be non-null;
enforced in `Activity.Create(type, note, occurredAt, contactId, companyId, dealId)`. Anchor FKs
use `DeleteBehavior.Restrict` to prevent nulling the sole anchor.

> **Fields not yet in entity**: `DueAt: DateTime?` and `IsDone: bool` are required for the Task
> sub-type (`.devclaw/research/activities.md` Borrowed §3). They are pending additions when the
> Activities API feature is implemented. The invariant will be: `DueAt` valid only when
> `Type == Task`.

**Borrowed from reference CRMs** (`.devclaw/research/activities.md`,
`.devclaw/research/domain-model.md`):

- **Salesforce**: `WhoId`/`WhatId` polymorphic anchor scoping — adapted to three typed nullable
  FKs (ContactId, CompanyId, DealId) rather than the research doc's original discriminated-pair
  `(TargetType, TargetId)` design. The FK-per-anchor pattern was chosen to mirror Deal's FK
  pattern and enable typed EF Core navigation; see AGENTS.md Key decisions.
- **HubSpot**: `lastActivityAt` (any type) and `lastContactedAt` (Call/Email/Meeting only)
  derived fields on Contact, Company, and Deal detail responses — computed via
  `MAX(Activity.OccurredAt)` at query time, no domain-layer change required.
- **Pipedrive**: `dueDate` + `isDone` for Task sub-type — pending addition; `GET /activities?type=Task&isDone=false` will surface open task list.
- **Attio**: Unified chronological activity feed on record detail — all `ActivityType` values
  merged, sorted by `OccurredAt` DESC, truncated to last 5 in embedded `recentActivities` slice.
- **HubSpot**: Auto-logged `StageChange` activity when a Deal advances — auto-inserted in the
  same `SaveChanges` transaction that updates `Deal.PipelineStageId`. This is why `StageChange`
  was added to the `ActivityType` enum beyond the research doc's original five values.

**Rejected**:
- **Zoho split Calls/Events/Tasks modules** — single `Activity` entity with `Type` discriminator
  avoids three controllers, three DTOs, and client-side merge for timeline
  (`.devclaw/research/activities.md` Rejected §A; `.devclaw/research/domain-model.md` Rejected §D).
- **Salesforce Task/Event binary split** — two-type model (`Task` = future, `Event` = calendar)
  forces `Type` picklist to encode semantically distinct activity kinds; the six-value enum
  provides richer dispatch (`.devclaw/research/activities.md` Rejected §B).
- **HubSpot legacy Engagements API v1** — nested `engagement + metadata` union payload is
  deprecated; the unified Activities object in CRM API v3 is the adopted reference
  (`.devclaw/research/activities.md` Rejected §C).
- **Pipedrive multi-participant activities** — would require relaxing the exactly-one-anchor
  invariant or a `ActivityParticipant` junction; MVP records meeting participants in the `Note`
  text (`.devclaw/research/activities.md` Rejected §D).
- **Attio separate Note and Task object types** — single `Activity` entity with `Type`
  discriminator plus `DueAt`/`IsDone` on `Task` sub-type achieves the same semantics without
  two DbSets (`.devclaw/research/activities.md` Rejected §E).

---

### Pipeline + PipelineStage — modeled, API implementation pending

**Shipped fields** (`backend/Domain/Entities/Pipeline.cs`):

| Field | Type | Constraints |
|---|---|---|
| `Id` | `Guid` | PK |
| `Name` | `string` | required, max 200 |
| `Stages` | `IReadOnlyList<PipelineStage>` | computed: `_stages.OrderBy(s => s.Order)` |

`_stages` is a `private readonly List<PipelineStage>` backing field. EF Core is configured to
populate it directly via `PropertyAccessMode.Field` because `Stages` returns a computed sorted
copy, not the field itself.

**Domain methods**: `AddStage(name, order, winProbability)` enforces no duplicate `Order` values
within a pipeline.

**Shipped fields** (`backend/Domain/Entities/PipelineStage.cs`):

| Field | Type | Constraints |
|---|---|---|
| `Id` | `Guid` | PK |
| `PipelineId` | `Guid` | required FK → Pipeline, `Cascade` on pipeline delete |
| `Name` | `string` | required, max 200 |
| `Order` | `int` | required, non-negative, unique within pipeline |
| `WinProbability` | `int?` | optional, 0–100; `0 = Closed Lost`, `100 = Closed Won` |

**Factory invariants**: `PipelineStage.Create` is `internal` — only callable from
`Pipeline.AddStage`. Validates name non-empty, order non-negative, winProbability in [0, 100].

**Borrowed from reference CRMs** (`.devclaw/research/pipelines.md`,
`.devclaw/research/domain-model.md`):

- **Pipedrive**: First-class `Pipeline` resource with `Name` and explicit stage `Order` integer;
  stage reordering via `PATCH /pipelines/{id}/stages/{stageId}` normalises sibling orders
  (`.devclaw/research/domain-model.md` Borrowed §2).
- **HubSpot**: Per-stage `winProbability` (0–100); sentinels `0 = Closed Lost`, `100 = Closed Won`
  drive the `isOverdue` deal computation without a separate `IsClosed` bool field.
- **HubSpot**: Per-pipeline metrics embedded in `GET /pipelines/{id}` — `totalDeals`,
  `openDeals`, `totalAmount`, `weightedAmount`, plus per-stage `dealCount`/`stageAmount`/
  `weightedAmount` — computed via SQL GROUP BY (`.devclaw/research/pipelines.md` Borrowed §3).
- **Pipedrive**: `isActive` soft-archive flag for retiring a stage without orphaning deals — deals
  in an inactive stage remain valid; hard-DELETE blocked if `dealCount > 0`; EF Core already
  enforces this via `DeleteBehavior.Restrict` on Deal → PipelineStage
  (`.devclaw/research/pipelines.md` Borrowed §4).
- **Attio**: `isMilestone: bool` on stage — visual gate marker for the pipeline board; purely
  additive `bool` column, defaults `false` (`.devclaw/research/pipelines.md` Borrowed §5).

**Rejected**:
- **Salesforce implicit pipeline** (`StageName` as flat picklist on Deal) — multi-pipeline support
  requires a first-class Pipeline entity; Salesforce's Record Types + Sales Processes workaround
  is notoriously complex (`.devclaw/research/domain-model.md` Rejected §B;
  `.devclaw/research/pipelines.md` Rejected §A).
- **Zoho `forecastType` per stage** (`Pipeline/BestCase/Commit/Closed`) — orthogonal to stage
  order; `winProbability` scalar already encodes the commitment spectrum; premature before the
  basic forecasting surface exists (`.devclaw/research/pipelines.md` Rejected §B).
- **HubSpot multi-pipeline-per-deal** (move a deal between pipelines) — semantically ambiguous
  because `StageTransition` history accumulates against stage IDs in the original pipeline;
  clean product behaviour is close in old pipeline, open new deal in target pipeline
  (`.devclaw/research/pipelines.md` Rejected §C).
- **Attio list-as-pipeline-for-any-object** — general-purpose pipeline over arbitrary object
  types is correct for a data platform and over-engineered for a deal-centric sales CRM
  (`.devclaw/research/domain-model.md` Rejected §C; `.devclaw/research/pipelines.md` Rejected §E).

---

## Entity relationships

```
Company ──< ContactCompanyLink >── Contact
               (IsPrimary flag)

Pipeline ──< PipelineStage
               (Order: int, WinProbability: int?)

Deal >── Pipeline        (DeleteBehavior.Restrict)
Deal >── PipelineStage   (DeleteBehavior.Restrict — stage must belong to deal's pipeline)
Deal >── Company?        (DeleteBehavior.SetNull)
Deal >── Contact?        (DeleteBehavior.SetNull)

Activity >── Contact?   (DeleteBehavior.Restrict — exactly-one-anchor invariant)
Activity >── Company?   (DeleteBehavior.Restrict — exactly-one-anchor invariant)
Activity >── Deal?      (DeleteBehavior.Restrict — exactly-one-anchor invariant)

StageTransition >── Deal           (append-only audit log)
StageTransition >── PipelineStage  (FromStageId, ToStageId — pending entity)
```

**Cardinality summary**:

| Relationship | Cardinality | Notes |
|---|---|---|
| Company ↔ Contact | Many-to-many | Via `ContactCompanyLink` with `IsPrimary` (research model); shipped entity uses simpler `Contact.CompanyId` FK — see divergences |
| Pipeline → PipelineStage | One-to-many | Cascade delete; stage `Order` unique within pipeline |
| Deal → Pipeline | Many-to-one | A deal belongs to exactly one pipeline; Restrict delete |
| Deal → PipelineStage | Many-to-one | Stage must belong to deal's pipeline; Restrict delete |
| Deal → Company | Many-to-one (opt.) | Primary company for the deal; SetNull on delete |
| Deal → Contact | Many-to-one (opt.) | Primary contact for the deal; SetNull on delete |
| Activity → Contact/Company/Deal | Exactly-one-anchor | Exactly one of three nullable FKs set; Restrict delete |
| Deal → StageTransition | One-to-many | Append-only; pending entity |

---

## Implementation divergences from research models

The following areas are where the shipped C# domain entities diverge from the research model in
`.devclaw/research/domain-model.md`. These divergences are recorded in AGENTS.md Key decisions
and are intentional:

1. **Contact-Company junction**: The research doc proposes a `ContactCompanyLink` many-to-many
   junction with `IsPrimary`. The shipped `Contact.cs` uses a single nullable `CompanyId` FK
   (simpler, covers SMB workflows). The junction table and `IsPrimary` flag are not yet
   implemented; the contacts API and feature research (`contacts.md` Borrowed §2) reference the
   junction — this is a gap to close when the Contacts and Companies API features are developed
   together.

2. **Activity polymorphism**: The research doc proposes a discriminated pair `(TargetType: enum,
   TargetId: Guid)`. The shipped entity uses three typed nullable FKs (`ContactId`, `CompanyId`,
   `DealId`). Rationale: mirrors Deal's FK pattern; enables typed EF Core navigation and
   per-anchor partial indexes without a `switch (TargetType)` dispatch.

3. **Deal.Title / Deal.CloseDate**: Named in the research model; not yet on the entity. Required
   for Kanban card rendering and overdue detection. Must be added with the Deals API feature.

4. **Activity.DueAt / Activity.IsDone**: Required for the Task sub-type; not yet on the entity.
   Must be added with the Activities API feature.

5. **StageTransition entity**: Referenced in AGENTS.md Key decisions and `.devclaw/research/domain-model.md`
   Borrowed §3. No `StageTransition.cs` entity exists in `backend/Domain/Entities/` yet. Required
   for the Deals API and stage-history timeline feature.

6. **ActivityType as closed enum**: The research doc (`domain-model.md` Borrowed §5) favours an
   open string registry. The shipped implementation uses a closed `ActivityType` enum
   (`Note/Call/Email/Meeting/Task/StageChange`). Rationale: strict domain model preferred for the
   domain layer; new types require a deployment but gain compile-time safety. The `StageChange`
   value was added beyond the research doc's five seed values to capture pipeline stage transitions
   as first-class audit events.
