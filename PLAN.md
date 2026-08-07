## Destination

closeloop is a production CRM whose every feature is grounded in explicit, argued synthesis from the
reference set (Salesforce, HubSpot, Pipedrive, Attio, Zoho). Each shipped feature carries a research
artifact, a borrowed-vs-rejected judgment, and senior-quality .NET 9 / Angular 21 / PostgreSQL code.
Done = every foundation milestone checked + done-gate green.

## Decisions so far

- Stack: .NET 9 Minimal API / Angular 21 / PostgreSQL / EF Core 9 — owner-decided, not re-litigated.
- All domain entities use private-constructor + static `Create` factory to enforce invariants.
- Research artifacts live under `.devclaw/research/<feature>.md`; template enforced by `.devclaw/research/README.md`.
- `NotificationTrigger` is a closed enum with explicit integer values (0,1,3,4) to preserve DB row
  mapping after `DealRotting`=2 and `TaskDue`=5 were removed; new triggers extend with the next explicit int.
- All four `INotificationDispatcher` methods create real `Notification` rows. Callers (see table below):
  - `DealAssigned`=0 → `DealAssignedAsync` → `PATCH /deals/{id}/stage`, conditioned on `ownerChanged`
    (DealsEndpoints.cs lines 134-135; only fires when new OwnerId differs from current owner).
  - `DealStageChanged`=1 → `DealStageChangedAsync` → `PATCH /deals/{id}/stage`, unconditionally
    (DealsEndpoints.cs line 133; fires on every valid stage-change request).
  - `ContactAssigned`=3 → `ContactAssignedAsync` → `PATCH /contacts/{id}/owner`, conditioned on
    `req.OwnerId != contact.OwnerId` — same-owner re-PATCH is a no-op (ContactsEndpoints.cs line 87).
  - `ActivityMention`=4 → `ActivityMentionAsync` → `POST /activities`, after SaveChanges, when
    parsed mention UUIDs are non-empty (ActivitiesEndpoints.cs line 74).
- Salesforce configurable rule engine rejected (over-complexity); HubSpot webhook-first push rejected
  (infra dependency); Attio record-following rejected (premature); Pipedrive email fallback deferred.
  Full argued rationale in `.devclaw/research/notifications.md`.

## Milestones

- [x] M1 Stack skeleton — .NET 9 Minimal API + EF Core + PostgreSQL wired, CI green
- [x] M2 Core domain model — Contact, Company, Deal, Activity, Pipeline, Stage entities + migrations
- [x] M3 REST API surface — CRUD endpoints for all five core objects, owner fields, pipeline/stage
- [x] M4 Research convention — `.devclaw/research/README.md` template + eight merged artifacts
- [x] M5 Notifications — entity, four-trigger enum, dispatcher, three API endpoints, all callers wired
- [x] M6 Deploy shape — root Dockerfile (multi-stage), DATABASE_URL precedence, CI test job
- [x] M7 Done-gate coherence — all prior drift findings resolved, done-gate green

## Tasks — M7 (done)

- [x] Resolve all done-gate drift findings (PR #11)
- [x] Add GET /activities and close clause-3 doc/param gaps (PR #10)
- [x] Validate cross-pipeline stage on POST, log StageChange activity on PATCH (PR #12)
- [x] Write PLAN.md re-propose-done assessment with verifiable four-trigger evidence

## Out of scope

- Angular 21 frontend shell beyond what the done-gate requires (deferred; backend API is the gate)
- Email/SMS notification delivery (explicitly rejected — see notifications.md §Rejected D)
- Salesforce-style configurable notification rules (rejected — see notifications.md §Rejected A)
- Deal rotting trigger (DealRotting=2 removed; integer slot reserved, not reused)
- Task-due trigger (TaskDue=5 removed; integer slot reserved, not reused)
