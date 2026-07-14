# CRM Activities feature — reference research

This artifact captures the design-input research for the **Activities feature slice** in closeloop:
the activity log endpoint, the per-record activity feed (embedded in contact/company/deal detail
responses), upcoming/overdue task surfacing, and the interaction between `ActivityType` and the
unified timeline view.

The domain object (`Activity`, `ActivityType` enum) is settled in `.devclaw/research/domain-model.md`
and implemented in `backend/Domain/Entities/Activity.cs`. This artifact focuses on **surface-layer
design**: how activities are queried, listed, filtered, and surfaced in related-record contexts;
which fields belong in the API response shape; and what write affordances the log endpoint must
support.

Key implementation decisions that this artifact builds on:
- `Activity` uses three nullable anchor FKs (`ContactId`, `CompanyId`, `DealId`) with an
  exactly-one-anchor invariant (see AGENTS.md Key decisions). The research-doc's discriminated-pair
  design was overridden by the FK pattern.
- `ActivityType` is a closed enum (`Note, Call, Email, Meeting, Task, StageChange`) rather than
  the open string registry the domain-model research preferred. `StageChange` was added beyond
  the research doc's five seed values to capture pipeline stage transitions as first-class audit
  events.

---

## Feature scope (synthesis target)

| Surface | What is designed here |
|---|---|
| **Activity log (global)** | `GET /activities` with anchor-filter, type-filter, date-range, pagination |
| **Per-record feed** | Embedded `recentActivities` slice (last 5) in contact/company/deal detail responses |
| **Task surface** | Upcoming tasks (due date, done flag) surfaced as a filtered sub-view of activities |
| **Log affordance** | `POST /activities` — shape, required fields, anchor validation |
| **Edit/delete** | `PATCH /activities/{id}` for note/subject correction; soft-complete for tasks |
| **Last-touched signal** | `lastActivityAt` derived field on contact, company, deal endpoints |

---

## Sources consulted

- **Salesforce** — `Task` and `Event` standard objects in the Salesforce Object Reference (public
  developer docs); `WhoId` (Contact or Lead polymorphic FK) and `WhatId` (any SObject FK) fields;
  `ActivityHistory` and `OpenActivity` related lists on Contact, Account, and Opportunity detail
  records; `GET /services/data/vXX/sobjects/Task` and `/Event`; the Lightning Experience Activity
  Timeline component. Examined via Salesforce Trailhead documentation, SOAP Schema Browser, and
  the Lightning Experience developer guide.

- **HubSpot** — CRM API v3 public documentation for Activities/Engagements: the modern Activities
  object (`GET /crm/v3/objects/activities`, engagement types: `CALL`, `EMAIL`, `MEETING`, `NOTE`,
  `TASK`), the legacy Engagements API (`POST /engagements/v1/engagements` — noted for comparison),
  Activity associations (`GET /crm/v3/objects/activities/{id}/associations/contacts`), and
  HubSpot's `hs_last_contacted` and `hs_last_activity_date` computed properties on Contact and
  Company records. Examined via `developers.hubspot.com` public API reference and the HubSpot CRM
  timeline UI.

- **Pipedrive** — REST API v1 reference for the `Activities` object:
  `GET /v1/activities` (list with `type`, `user_id`, `start_date`, `end_date`, `done`, `start`,
  `limit`), `POST /v1/activities` (create with `subject`, `type`, `due_date`, `due_time`,
  `person_id`, `org_id`, `deal_id`, `note`), `PUT /v1/activities/{id}` (update including
  `done: true` to mark complete), and `/v1/activityTypes` (configurable activity type registry).
  Examined via `developers.pipedrive.com` public API docs and the Pipedrive Activities calendar
  view documentation.

- **Attio** — Public API v2 documentation: `Notes` and `Tasks` as separate Attio object types
  (`GET /v2/notes`, `GET /v2/tasks`); the unified activity feed on record detail (Notes + Tasks
  merged in the record's right-hand panel); `PATCH /v2/tasks/{task_id}` with `is_completed`
  flag. Examined via `developers.attio.com` API reference and Attio's public product changelog
  post "Notes and Tasks" (2023).

- **Zoho CRM** — Developer documentation for Activities sub-modules: `Calls`
  (`GET /crm/v2/Calls`), `Events` (`GET /crm/v2/Events`), `Tasks` (`GET /crm/v2/Tasks`) as
  three distinct first-class modules; the Zoho CRM Activities tab (which client-side merges the
  three modules into one chronological view); `GET /crm/v2/Contacts/{id}/Activities` (combined
  activities related list). Examined via `www.zoho.com/crm/developer/docs` public API reference
  and Zoho CRM help centre documentation for the Activities section.

---

## Borrowed

### 1. Salesforce's `WhoId`/`WhatId` polymorphic anchor pattern, adapted to typed nullable FKs

- **What**: The Activity log endpoint (`GET /activities`) supports anchor-scoping via query
  parameters: `?contactId={id}`, `?companyId={id}`, or `?dealId={id}`. Exactly one anchor
  parameter filters the feed to activities anchored to that record. The response envelope tags
  each activity with its anchor type and ID (`anchorType: "Contact" | "Company" | "Deal"`,
  `anchorId: uuid`) so the client can render the correct "linked to" label without a join.
  This mirrors Salesforce's WhoId/WhatId dispatch adapted to the FK-per-anchor model already in
  the domain.
- **From**: Salesforce — `Task.WhoId` (Contact or Lead; polymorphic person FK) and `Task.WhatId`
  (any SObject; polymorphic entity FK); the `ActivityHistory` related list on Contact/Account/
  Opportunity, each scoped to that record's WhoId or WhatId references. The FK-per-anchor
  implementation choice (three nullable FKs instead of discriminated pair) was made in the
  domain layer (AGENTS.md Key decisions); this borrowed item applies Salesforce's scoping
  semantics to that implementation.
- **Why it fits**: The `Activity` table's three nullable FK columns (`ContactId`, `CompanyId`,
  `DealId`) map directly to three query-parameter filters on `GET /activities`. A partial index
  per FK column (`WHERE ContactId IS NOT NULL`, etc.) makes each anchor-scoped query an
  index-range scan rather than a full-table scan. This is the same pattern the contact and
  company detail views use to populate their embedded `recentActivities` slice.

### 2. HubSpot's `lastActivityAt` / `lastContactedAt` derived property on parent records

- **What**: The contact, company, and deal detail API responses each include a `lastActivityAt:
  DateTime?` read-only field, computed as `MAX(Activity.OccurredAt) WHERE anchorId = this.Id`.
  For contacts and companies, a stricter `lastContactedAt: DateTime?` is also computed from only
  outbound-touch types (`Call`, `Email`, `Meeting`) — excluding internal `Note` and `Task` entries
  that do not represent actual contact. These two fields power the "Last contacted N days ago"
  label in the list view and the rotting indicator on deal cards.
- **From**: HubSpot CRM — `hs_last_activity_date` (any activity touching the record) and
  `hs_last_contacted` (only Calls, Emails, or Meetings) are computed properties on Contact and
  Company objects, kept current by HubSpot's property-compute pipeline. HubSpot surfaces both
  in the contact list view and as configurable columns on the company table.
- **Why it fits**: These are pure read-side aggregations: one SQL `MAX()` with a type filter.
  They do not require a domain-layer change — they are computed at query time in the repository
  layer and projected into the API response DTO. The distinction between `lastActivityAt`
  (any type) and `lastContactedAt` (outbound-touch types only) is meaningful: a `Note`
  added by the rep is internal; a `Call` is actual customer engagement.

### 3. Pipedrive's `dueDate` + `isDone` fields on tasks (activity sub-type with schedule semantics)

- **What**: An `Activity` of type `Task` carries two additional optional fields: `dueDate:
  DateTime?` (when the task should be completed) and `isDone: bool` (defaults false). A `PATCH
  /activities/{id}` with `{ "isDone": true }` marks the task complete without changing other
  fields. The global `GET /activities` endpoint accepts `?type=Task&isDone=false` to surface the
  user's open task list and `?type=Task&isDone=false&dueBefore=today` to surface overdue tasks.
- **From**: Pipedrive REST API v1 — every Activity carries `due_date`, `due_time`, and `done`
  fields; `GET /v1/activities?done=0` returns pending activities; `GET /v1/activities?done=1`
  returns completed ones. Pipedrive's activity model treats all types (call, meeting, task, etc.)
  as schedulable — every activity can have a due date and a done flag. closeloop narrows this to
  `Task` type only because calls/meetings have an `OccurredAt` (past), not a due date (future).
- **Why it fits**: The `Activity` entity currently has `OccurredAt` (past event timestamp) but no
  forward-looking `DueAt`. Adding `DueAt: DateTime?` and `IsDone: bool` to the domain entity (and
  EF Core configuration) is a minimal, non-breaking extension. The invariant: `DueAt` is only
  valid when `Type == Task`; `OccurredAt` remains required for all types (set to `now()` for tasks
  when they are completed or to an explicit past date for logged historical events). The domain
  `Activity.Create` factory should accept `dueAt: DateTime?` and validate it is only set for
  `Task` type.

### 4. Attio's unified activity feed on record detail (all types merged, sorted by `OccurredAt`)

- **What**: The activity feed embedded in contact, company, and deal detail records is a single
  chronological list merging all `ActivityType` values — `Note`, `Call`, `Email`, `Meeting`,
  `Task`, `StageChange` — sorted by `OccurredAt` descending. Each item renders a type-specific
  icon (note pad, phone, envelope, calendar, checkbox, pipeline arrow) and the `Note` field as
  the body text. The feed is truncated to the last 5 items in the embedded `recentActivities`
  slice; full paginated browsing is available via `GET /activities?contactId={id}`.
- **From**: Attio record detail UI — the right-hand activity feed on People, Company, and List
  Entry records shows Notes and Tasks merged in one chronological stream with type-distinguishing
  icons; no tab separation by type. Attio's `GET /v2/notes` and `GET /v2/tasks` responses follow
  the same attribute-map shape, enabling client-side merge-sort on `created_at`.
- **Why it fits**: The `Activity` entity's closed `ActivityType` enum provides the icon dispatch
  signal. A single query `SELECT * FROM Activities WHERE anchor = ? ORDER BY OccurredAt DESC
  LIMIT 5` is more efficient than separate queries per type. The unified feed is the primary
  "what happened with this contact/deal?" surface in the UI; fragmenting it by type (as Zoho
  does) forces the rep to tab-hop to reconstruct a chronological picture.

### 5. HubSpot's `StageChange` activity auto-logged on deal stage advance

- **What**: When a Deal's stage is advanced via `PATCH /deals/{id}/stage`, the API automatically
  appends an `Activity` of type `StageChange` with the system note
  `"Stage changed from <fromStageName> to <toStageName>"`, anchored to the deal's `DealId`, and
  `OccurredAt = now()`. This auto-logged entry appears in the deal's activity feed and in the
  company/contact feeds that aggregate across associated deals, giving a timeline of pipeline
  progress without any manual note from the rep.
- **From**: HubSpot CRM — stage changes on Deals are automatically logged as Engagement/Activity
  records of type `STAGE_CHANGE` in the deal's timeline; these appear in the deal's "Activity"
  tab alongside manually logged calls and notes. HubSpot's `hs_activity_type` = `STAGE_CHANGE`
  is a first-class engagement type, not a custom note.
- **Why it fits**: `ActivityType.StageChange` is already in the closed enum (AGENTS.md Key
  decisions notes it was "added beyond the research doc's five seed values because pipeline stage
  transitions are the primary audit event in a deal-centric CRM"). The stage-advance endpoint
  should auto-insert the StageChange activity inside the same EF Core `SaveChanges` transaction
  that updates `Deal.PipelineStageId` and appends the `StageTransition` row. No manual rep
  action is required; the timeline is kept current automatically.

---

## Rejected & why

### A. Zoho's split Calls / Events / Tasks as separate first-class API modules

- **What was considered**: Exposing three separate endpoints — `GET /calls`, `GET /events`,
  `GET /tasks` — each backed by a distinct entity (or at minimum a separate EF Core DbSet),
  with the unified Activities view assembled client-side.
- **Source**: Zoho CRM — three distinct modules in the CRM API: `Calls`, `Events`, `Tasks`, each
  with its own list/get/create/update endpoints; the Zoho Activities UI tab merges them
  client-side via three concurrent API calls.
- **Reason rejected**: Already argued at the domain level (`.devclaw/research/domain-model.md`
  Rejected §D). The domain ships a single `Activity` entity with a discriminated `Type` field.
  Splitting the API surface into three modules would require three controllers, three DTOs, and
  three sets of tests for what is structurally one entity. The `GET /activities?type=Call` query
  parameter achieves the same filtering with one endpoint. Three concurrent API calls to assemble
  a timeline is an anti-pattern when one query suffices.

### B. Salesforce's closed `Task` / `Event` binary split with no first-class `Note` or `Call` type

- **What was considered**: Using only two activity types — `Task` (scheduled, has due date) and
  `Event` (calendar appointment, has start/end time) — and encoding everything else (notes, call
  logs, emails) as `Task` records with a custom `Type` picklist. This is Salesforce's native
  model: `ActivityHistory` shows Task and Event records; calls and emails are Tasks with
  `Type = "Call"` or `"Email"`.
- **Source**: Salesforce — `Task` and `Event` standard objects; `Task.Type` picklist values
  (`Call`, `Email`, `Other`); `ActivityHistory` as a virtual related list combining both.
- **Reason rejected**: Representing fundamentally different activity kinds (a timestamped call log
  vs. a future-dated task with a done flag) as the same entity distinguished only by a picklist
  value forces every read query to filter by `Type` to distinguish past-event semantics from
  future-task semantics. The `ActivityType` enum's explicit values (`Note`, `Call`, `Email`,
  `Meeting`, `Task`) provide a richer dispatch surface for icon rendering, filtering, and
  `lastContactedAt` computation without additional metadata ambiguity.

### C. HubSpot's legacy Engagements API (v1) as an activity model

- **What was considered**: Modelling activities after HubSpot's legacy Engagements API
  (`POST /engagements/v1/engagements`) which uses a nested `engagement` object plus a separate
  `metadata` object per type (e.g. `{ "engagement": { "type": "CALL" }, "metadata": { "toNumber": ... } }`).
  This provides rich type-specific metadata (call duration, email subject/body, meeting participants)
  as typed metadata payloads.
- **Source**: HubSpot Engagements API v1 — `POST /engagements/v1/engagements`; the
  `engagement.type` + `metadata` split; HubSpot developer documentation noting this API is
  "legacy" and replaced by the Activities object in CRM API v3.
- **Reason rejected**: HubSpot itself has deprecated this API in favour of the unified Activities
  object. The nested `engagement` + `metadata` split requires the API to accept a union-typed
  metadata payload where the schema depends on the engagement type — more complex to validate
  and document than a flat Activity DTO with a `type` field and a nullable `note` field.
  closeloop's `Activity.Note` string field is sufficient for all type-specific content in the
  MVP; rich structured metadata (call duration, email thread ID) is a future extension, not a
  day-one requirement.

### D. Pipedrive's activity participants list (multi-participant activities)

- **What was considered**: Extending the Activity entity to support multiple participants —
  contacts who were party to the same call or meeting — stored as a `participants` array on the
  activity: `[{ contactId, name, primaryContact: bool }]`. Pipedrive's
  `POST /v1/activities` accepts a `participants` array so a meeting with three attendees logs one
  activity linked to all three contacts simultaneously.
- **Source**: Pipedrive REST API v1 — `participants` field on the Activity object; Pipedrive help
  centre documentation "Add meeting participants to an activity".
- **Reason rejected**: The `Activity` entity enforces exactly-one-anchor (ContactId, CompanyId,
  or DealId). Multi-participant activities would require either relaxing this invariant (breaking
  the domain constraint) or introducing a `ActivityParticipant` junction entity. For MVP, a
  meeting with three participants is best recorded as one Activity anchored to the deal (or primary
  contact) with the participant names mentioned in the `Note` text. The one-anchor invariant
  keeps Activity queries simple (no join to a participants table) and the domain model strict.

### E. Attio's separate `Note` and `Task` as distinct object types (not subtypes of Activity)

- **What was considered**: Splitting the Activity surface into two separate first-class object
  types — `Note` (immutable past record, no due date, no done flag) and `Task` (schedulable,
  has due date, has done flag) — each with their own endpoints (`GET /notes`, `GET /tasks`),
  separate EF Core DbSets, and separate API controllers. Notes and tasks are merged only in the
  UI timeline.
- **Source**: Attio public API v2 — `GET /v2/notes` and `GET /v2/tasks` are fully separate
  endpoints with different schemas; `Note.content` is a rich-text string; `Task.is_completed` and
  `Task.deadline` fields are absent on notes. Attio's record detail merges both in the activity
  feed client-side.
- **Reason rejected**: The domain already ships a single `Activity` entity with `ActivityType`
  as a discriminator (domain-model.md Borrowed §5). Splitting into two DbSets would require
  duplicating the anchor-FK pattern, the EF Core configuration, two sets of endpoint/DTO code,
  and a client-side merge for the timeline — all for what is structurally the same record shape
  with a boolean `IsDone` and an optional `DueAt`. The Pipedrive pattern (Borrowed §3) achieves
  the task/note distinction within one entity via `Type == Task ↔ DueAt/IsDone` semantics,
  which is simpler and already compatible with the existing `Activity` entity structure.
