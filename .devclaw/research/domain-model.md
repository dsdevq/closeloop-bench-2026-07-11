# CRM core object model — reference research

This artifact captures the design-input research for closeloop's five core domain objects:
**Contact**, **Company**, **Deal**, **Activity**, and **Pipeline** (with subordinate **Stage**).
It is the direct input for the Domain-layer entity implementations.  The sections below record what
was examined, what was adopted and from where, and what was deliberately rejected and why.

---

## Object model resulting from this research (summary)

| Object | Key fields | Primary ownership |
|---|---|---|
| **Contact** | Id, FullName, Email, Phone, OwnerId | owned by a User |
| **Company** | Id, Name, Domain, Industry, OwnerId | owned by a User |
| **ContactCompanyLink** | ContactId, CompanyId, IsPrimary | junction — many-to-many |
| **Deal** | Id, Title, Amount, CloseDate, OwnerId, PipelineId, StageId | owned by a User, belongs to one Pipeline |
| **Stage** | Id, PipelineId, Name, Order, WinProbability | child of Pipeline |
| **Pipeline** | Id, Name, OwnerId | owned by a User (or team) |
| **Activity** | Id, Type, Subject, Body, TargetType, TargetId, OwnerId, OccurredAt, DueAt | polymorphic target |
| **StageTransition** | DealId, FromStageId, ToStageId, ChangedAt, ChangedBy | append-only audit log on Deal |

Cardinality summary:
- Contact ↔ Company: **many-to-many** with one `IsPrimary` link per Contact.
- Deal → Pipeline: **many-to-one** (a Deal belongs to exactly one Pipeline at a time).
- Deal → Stage: **many-to-one** (a Deal is in exactly one Stage; Stage must belong to its Pipeline).
- Deal → Contact(s): **many-to-many** via DealContactLink (first linked Contact is "primary contact").
- Deal → Company: **many-to-one** (one primary Company per Deal; optional).
- Activity → target: **polymorphic** (TargetType ∈ {Contact, Company, Deal}; one target per Activity).

---

## Sources consulted

- **Salesforce** — Standard Object Reference (Salesforce Help, accessed via public developer docs):
  `Account`, `Contact`, `Lead`, `Opportunity`, `Task`, `Event`, `ActivityHistory` object schemas;
  `Sales Process` configuration docs; WhoId/WhatId polymorphic-lookup documentation for Task/Event.
  Examined via public Salesforce Trailhead documentation and the SOAP API Schema Browser reference.

- **HubSpot** — CRM API v3 public documentation: Objects (Contacts, Companies, Deals, Activities),
  Associations API (association types, labels, many-to-many cardinality), Pipelines API (create/read
  pipeline + stages), Engagements API (legacy) and Activities replacement. Examined via
  `developers.hubspot.com` public API reference pages.

- **Pipedrive** — REST API reference v1: Persons, Organizations, Deals, Stages, Pipelines,
  Activities objects; "Activity types" configuration endpoint; Deal-to-Person and Deal-to-Org
  linking model. Examined via `developers.pipedrive.com` public API docs.

- **Attio** — Public API v2 documentation: Objects (People, Companies), Records, Lists (as
  pipeline construct), List Entries, Attributes, Notes, Tasks, Workspace Members. Examined via
  `developers.attio.com` public API docs and the Attio product changelog for the "Lists as
  pipelines" feature introduction.

- **Zoho CRM** — Developer documentation: Modules (Leads, Contacts, Accounts, Deals/Potentials),
  Activities sub-modules (Calls, Events, Tasks), Pipeline and Stage configuration per-module,
  Notes. Examined via `www.zoho.com/crm/developer/docs` public API reference.

---

## Borrowed

### 1. Company as a first-class grouping object (not an "Account" type-umbrella)

- **What**: `Company` is a dedicated object for organizations; it does not double as a container
  for individuals. Contacts are not sub-records of Company — they are linked via an explicit
  association with a `IsPrimary` flag.
- **From**: HubSpot — the Company object in the CRM API v3 (`/crm/v3/objects/companies`), and the
  Contact-to-Company association which supports one "Primary Company" label plus arbitrary
  additional company associations.
- **Why it fits**: closeloop targets sales teams where a salesperson manages individual contacts
  who may work across multiple companies (consultants, investors, board members). A flat FK
  `Contact.CompanyId` cannot represent this without nullable nullability hacks; a junction table
  with `IsPrimary` does it cleanly and stays queryable.

### 2. First-class Pipeline object with ordered Stages as children

- **What**: `Pipeline` is a named entity with its own Id and owner. `Stage` is a child record of
  Pipeline carrying an explicit integer `Order`, a display `Name`, and an optional
  `WinProbability` (0–100). A Deal holds `PipelineId` and `StageId`; Stage must belong to the
  same Pipeline as the Deal (enforced at domain layer).
- **From**: Pipedrive — `/v1/pipelines` and `/v1/stages` are separate first-class resources;
  stages are ordered children of a pipeline; each Deal has `pipeline_id` and `stage_id` as
  top-level fields. HubSpot's `/crm/v3/pipelines/{objectType}` follows the same shape.
- **Why it fits**: closeloop's roadmap includes multiple pipelines (sales pipeline, renewal
  pipeline, partnership pipeline). Making Pipeline and Stage first-class objects means adding a
  new pipeline is a data change, not a schema migration. A flat Stage-picklist-on-Deal (as
  Salesforce does) would require a migration per new pipeline.

### 3. Stage transition log as an append-only child table on Deal

- **What**: Each time `Deal.StageId` changes, a `StageTransition` row is appended with
  `FromStageId`, `ToStageId`, `ChangedAt`, and `ChangedByUserId`. The Deal entity itself exposes
  a `StageTransitions` collection. No rows are updated or deleted; history is structural.
- **From**: Attio — Attio tracks entry-stage history on List Entries as immutable events; the
  API returns `stage_history` as a list of `{from_stage_id, to_stage_id, changed_at}` objects.
  This is Attio's distinguishing feature versus competitors that require a separate "History"
  object query.
- **Why it fits**: Average deal cycle time, stage conversion rates, and time-in-stage analytics
  are first-class product requirements for a sales tool. Storing this as a child collection on
  Deal (rather than querying a generic audit log) keeps the domain model self-contained without
  infrastructure coupling to an event-store.

### 4. Activity polymorphism via (TargetType, TargetId) discriminated pair

- **What**: A single `Activity` entity can target a Contact, a Company, or a Deal. The target is
  stored as `TargetType` (an enum: Contact | Company | Deal) plus `TargetId` (Guid). Each
  Activity also carries an open `Type` string (not a closed enum) identifying the activity kind
  (e.g. "call", "email", "meeting", "note", "task") and a `Body` text field.
- **From**: Salesforce — `Task` and `Event` use `WhoId` (Contact or Lead) and `WhatId` (any
  SObject) as polymorphic lookups. This pattern decouples the activity from any specific parent
  type. HubSpot's Associations API extends this further: any object can be associated to any
  other object via labeled associations. closeloop adopts the discriminated-pair pattern (simpler
  than HubSpot's labeled-association graph) scoped to three target types.
- **Why it fits**: A .NET domain layer can resolve the polymorphic target without EF Core
  table-per-hierarchy or abstract base classes — a `switch (TargetType)` dispatch in the
  repository is sufficient. Limiting targets to the three core objects (not arbitrary) keeps the
  model comprehensible.

### 5. Open activity-type registry (string, not closed enum)

- **What**: `Activity.Type` is a `string` stored in the database, seeded with well-known values
  ("call", "email", "meeting", "note", "task") but not constrained by a migration-time enum.
- **From**: HubSpot — activity type is a string property on Engagement/Activity objects; new
  types are added by configuration, not schema change. Pipedrive also exposes
  `/v1/activityTypes` as a writable resource, treating types as data rather than code.
- **Why it fits**: Future activity types (e.g. "linkedin-message", "demo", "contract-sent")
  should not require a backend deployment. Representing type as a string with a seed list plus
  optional UI validation keeps the domain open for extension.

---

## Rejected & why

### A. Salesforce's Lead object (combined person + company pre-qualification)

- **What was considered**: A `Lead` object that combines Contact and Company fields into a single
  unqualified record, converted to separate Account + Contact + Opportunity records upon
  qualification.
- **Source**: Salesforce — `Lead` standard object, Lead Conversion workflow.
- **Reason rejected**: The dual-graph problem. Before conversion a prospect lives as a Lead; after
  conversion it lives as three linked records. Any query that needs to span "all people ever
  touched" must union Leads and Contacts. closeloop does not need a pre-qualification limbo state
  — a Contact without a linked Deal already models an unqualified prospect cleanly. Adding Lead
  would duplicate the person model and require conversion logic with no user-facing benefit at
  the target team size (≤ 50-seat SMB sales teams).

### B. Salesforce's implicit pipeline (Opportunity Stage as a flat picklist)

- **What was considered**: Encoding pipeline stage as a string picklist on the Deal record with no
  separate Pipeline object. Stages are configured at the org level, not as first-class data.
- **Source**: Salesforce — `Opportunity.StageName` picklist field; Sales Process configuration in
  Setup (which restricts the visible subset of the global stage list per record type).
- **Reason rejected**: Multi-pipeline support is inexpressible without a separate Pipeline entity.
  Salesforce works around this via "Record Types" + "Sales Processes" — an indirection layer that
  is notoriously confusing to configure. closeloop must support multiple distinct pipelines from
  day one. A first-class `Pipeline` + `Stage` graph (borrowed from Pipedrive) makes this
  straightforward.

### C. Attio's List-as-pipeline-for-any-object

- **What was considered**: Treating pipelines as generic "Lists" that can contain records of any
  object type (not just Deals). Attio allows a Company list, a Person list, and a Deal list to
  each be pipeline-shaped with stages.
- **Source**: Attio — `Lists` API v2, "list entries" as the pipeline item; Attio blog post on
  the "Works for any object" positioning.
- **Reason rejected**: closeloop's domain is explicitly deal-centric. Making pipelines generic
  adds significant model complexity (list-entry vs. record duality, arbitrary-object stage
  transitions) for a use case the product does not target. The Attio approach is the right design
  for a general-purpose data platform; it is over-engineered for a focused sales CRM where
  "pipeline" unambiguously means a Deal pipeline.

### D. Zoho's split Activity modules (Calls, Events, Tasks as separate first-class objects)

- **What was considered**: Separate `Call`, `Event`, and `Task` domain entities each with their
  own repository, queries, and activity-timeline aggregation at read time.
- **Source**: Zoho CRM — three distinct API modules: `Calls`, `Events`, `Tasks`; unified only in
  the "Activities" UI tab via a client-side merge.
- **Reason rejected**: Fragments the domain model unnecessarily. Querying "all activities on this
  Deal" becomes three separate lookups merged in application code. A single `Activity` entity with
  a discriminated `Type` field (see Borrowed §5) achieves the same expressiveness with one
  repository, one query, and one timeline sort. Zoho's split arose from legacy module architecture
  (each module maps to a database table); closeloop has no such legacy constraint.

### E. HubSpot's full many-to-many Deal associations (Deals associated with multiple Companies)

- **What was considered**: Associating a Deal with multiple Companies via a labeled association
  graph (e.g. "primary company", "investor company", "partner company").
- **Source**: HubSpot CRM API v3 Associations — a Deal can be associated to multiple Company
  records with distinct association type labels.
- **Reason rejected**: Introduces a join table on an already relationally rich object (Deal →
  Contact(s) + Company/ies + Pipeline + Stage) and complicates "which company owns this deal"
  queries at the domain layer. closeloop's primary persona is an SMB salesperson who expects each
  Deal to have exactly one primary Company. The `Deal.PrimaryCompanyId` (nullable FK) pattern
  covers ≥ 95% of real sales workflows; multi-company deals are an edge case that can be
  addressed later via notes or a DealCompanyLink junction if product demands it.

### F. Pipedrive's "Organization" naming for the company object

- **What was considered**: Naming the company-type entity `Organization` following Pipedrive's
  API resource name (`/v1/organizations`).
- **Source**: Pipedrive REST API v1 — `Organization` object.
- **Reason rejected**: User research and product copy consistently refer to "Companies" in the
  target audience (SMB SaaS sales). "Organization" carries connotations of non-profit, public
  sector, or internal team structure. HubSpot, Attio, and Zoho all use "Company" for this object.
  Naming should match the mental model of the user, not any single CRM vendor's terminology
  choice.
