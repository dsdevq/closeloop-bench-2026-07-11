# CRM Notifications feature — reference research

This artifact captures the design-input research for the **Notifications feature slice** in
closeloop: the in-app notification feed, activity-driven trigger taxonomy, mention surface on
activity notes, deal-health alerting (rotting), task-due reminders, and the background dispatch
model that connects domain events to notification records.

The core domain objects (`Contact`, `Company`, `Deal`, `Activity`, `Pipeline`) are settled in
`.devclaw/research/domain-model.md` and implemented in `backend/Domain/Entities/`. This artifact
focuses on **surface-layer design**: the `Notification` entity shape, the `NotificationTrigger`
enum, the `INotificationDispatcher` interface contract, the background hosted service for
time-based triggers, and the API endpoints that expose the notification feed to the client.

This slice covers **research only**. Implementation follows in a separate PR once this artifact is
merged.

---

## Feature scope (synthesis target)

| Surface | What is designed here |
|---|---|
| **Notification entity** | `Notification` domain entity: fields, factory invariants, read affordance |
| **Trigger taxonomy** | `NotificationTrigger` enum: six named triggers derived from domain events |
| **Dispatch interface** | `INotificationDispatcher` contract: how API handlers fan out after SaveChanges |
| **Background job** | `DealRottingNotificationJob`: hosted service scanning for stale deals |
| **API endpoints** | `GET /notifications`, `PATCH /notifications/{id}/read`, `POST /notifications/read-all` |
| **Mention surface** | @mention syntax in `Activity.Note` text parsed on write; mention notification fired |

---

## Domain design (implementation contract)

### Notification entity

```csharp
// backend/Domain/Entities/Notification.cs
// Extends Domain.Common.Entity (provides Id: Guid, protected init)
public sealed class Notification : Entity
{
    public Guid RecipientUserId { get; private init; }
    public NotificationTrigger Trigger { get; private init; }
    public string Title { get; private init; }
    public string? Body { get; private init; }
    public Guid? RelatedEntityId { get; private init; }
    public NotificationEntityType? RelatedEntityType { get; private init; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private init; }

    private Notification() { }  // EF Core

    public static Notification Create(
        Guid recipientUserId,
        NotificationTrigger trigger,
        string title,
        string? body = null,
        Guid? relatedEntityId = null,
        NotificationEntityType? relatedEntityType = null)
    {
        if (recipientUserId == Guid.Empty)
            throw new ArgumentException("RecipientUserId is required", nameof(recipientUserId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required", nameof(title));
        if (relatedEntityId.HasValue != relatedEntityType.HasValue)
            throw new ArgumentException("RelatedEntityId and RelatedEntityType must both be set or both null");

        return new Notification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = recipientUserId,
            Trigger = trigger,
            Title = title,
            Body = body,
            RelatedEntityId = relatedEntityId,
            RelatedEntityType = relatedEntityType,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public void MarkRead() => IsRead = true;
}
```

### Trigger taxonomy

```csharp
// backend/Domain/Entities/NotificationTrigger.cs
public enum NotificationTrigger
{
    DealAssigned,      // deal.OwnerId changed to this user (via PATCH /deals/{id})
    DealStageChanged,  // deal advanced to a new stage (PATCH /deals/{id}/stage)
    DealRotting,       // deal has no activity for > pipeline.RottingThresholdDays (background job)
    ContactAssigned,   // contact.OwnerId changed to this user (via PATCH /contacts/{id})
    ActivityMention,   // @mention pattern matched in Activity.Note on POST /activities
    TaskDue,           // Task Activity with DueAt within 24 h (background job)
}

// backend/Domain/Entities/NotificationEntityType.cs
public enum NotificationEntityType
{
    Contact,
    Company,
    Deal,
    Activity,
}
```

### Dispatch interface

```csharp
// backend/Domain/Interfaces/INotificationDispatcher.cs
// Sits at the Domain boundary; Infrastructure provides the concrete implementation.
public interface INotificationDispatcher
{
    Task DealAssignedAsync(Deal deal, Guid previousOwnerId, CancellationToken ct = default);
    Task DealStageChangedAsync(Deal deal, PipelineStage toStage, CancellationToken ct = default);
    Task ContactAssignedAsync(Contact contact, Guid previousOwnerId, CancellationToken ct = default);
    Task ActivityMentionAsync(Activity activity, IReadOnlyList<Guid> mentionedUserIds, CancellationToken ct = default);
}
```

API endpoint handlers inject `INotificationDispatcher` and call the appropriate method after
`await dbContext.SaveChangesAsync()`. The dispatcher creates `Notification` records in the same
`CrmDbContext` via a second SaveChanges (separate transaction — eventual consistency acceptable
for notifications; they are informational, not transactional).

### Background hosted service

```csharp
// backend/Infrastructure/Jobs/DealRottingNotificationJob.cs
// IHostedService registered in Program.cs; runs every 6 hours.
// For each active deal: if MAX(Activity.OccurredAt WHERE DealId = deal.Id) < now - pipeline.RottingThresholdDays
// AND no DealRotting notification exists for this deal in the last 24 h:
//   → create Notification for deal.OwnerId with Trigger = DealRotting
// Also: for each Task Activity with DueAt BETWEEN now AND now+24h WHERE IsDone = false:
//   → create Notification for activity.OwnerId with Trigger = TaskDue
```

### API endpoints

```
GET  /notifications                    list for authenticated user, ordered CreatedAt DESC
                                       query params: ?isRead=false (default: all), ?limit=50&cursor=...
PATCH /notifications/{id}/read         mark one notification as read (idempotent)
POST  /notifications/read-all          mark all unread notifications for the user as read
```

Response shape for `GET /notifications`:

```json
{
  "items": [
    {
      "id": "uuid",
      "trigger": "DealStageChanged",
      "title": "Deal 'Acme Q3 Renewal' moved to Negotiation",
      "body": null,
      "relatedEntityId": "uuid",
      "relatedEntityType": "Deal",
      "isRead": false,
      "createdAt": "2026-07-17T09:00:00Z"
    }
  ],
  "nextCursor": "...",
  "unreadCount": 3
}
```

---

## Sources consulted

- **HubSpot** — In-app notification center (bell icon, Lightning Notifications API); CRM webhook
  subscriptions documentation: `deal.propertyChange` (fires when `dealstage` or `hubspot_owner_id`
  changes), `contact.propertyChange` (fires when `hubspot_owner_id` changes), `contact.creation`,
  `deal.creation` subscription types; HubSpot's "Notifications API" (`GET /notifications/v1/notifications`,
  `POST /notifications/v1/notifications/{id}/read`); and the HubSpot task-due reminder behavior
  (tasks with due dates surface in the notification center as "Task due today"). Examined via
  `developers.hubspot.com` webhook subscriptions documentation, Notifications API reference, and
  the HubSpot CRM in-app notification center UI help articles.

- **Salesforce** — Salesforce Chatter @mention behavior on Feed Items and Comments (mentioning a
  workspace member in a Chatter post or comment fires an in-app notification and email to that
  user); Salesforce Process Builder "Send Custom Notification" action
  (`CustomNotificationType` object, `CustomNotification` invocable action) for firing structured
  in-app notifications when a record field changes (e.g. `Opportunity.StageName` changes to
  "Closed Won"); the Lightning Experience Notification Builder (Flow-based notification actions)
  and `ConnectApi.ChatterFeeds` for mention parsing. Examined via Salesforce Trailhead documentation
  ("Send Custom Notifications with Flow"), Salesforce Developer docs on `CustomNotificationType`,
  and Salesforce Help articles on Chatter @mentions.

- **Pipedrive** — Pipedrive's "Deal Rotting" notification: pipeline-level `rotten_days` config
  triggers an in-app alert and optional email to the deal owner when no activity is logged within
  the threshold; Pipedrive's activity-due reminder (in-app notification when a linked Activity
  reaches its `due_date`); Pipedrive webhook events documentation for `updated.deal` (includes
  `stage_id` in the `current` and `previous` payloads, enabling stage-change detection); and
  `GET /v1/notifications` (user-specific notification list with `read_flag`). Examined via
  `developers.pipedrive.com` webhook documentation, Pipedrive help articles on "Deal rotting",
  and the Pipedrive in-app notification UI.

- **Attio** — Attio's @mention in Notes: typing `@<member name>` in a Note (Activity) body sends
  an in-app notification to the mentioned workspace member; Attio's "Record following": users can
  subscribe to a record (Person, Company, List Entry) and receive a notification when any attribute
  changes or when a new note/task is added; `GET /v2/inbox` (Attio's notification inbox API,
  returning unread mention events and follower updates). Examined via `developers.attio.com`
  API reference and Attio's public product changelog post "Mentions in notes and tasks" (2023).

---

## Borrowed

### 1. HubSpot's named-trigger taxonomy: six discrete trigger types (not a generic event bus)

- **What**: The `NotificationTrigger` enum encodes exactly six named triggers:
  `DealAssigned`, `DealStageChanged`, `DealRotting`, `ContactAssigned`, `ActivityMention`,
  `TaskDue`. Each trigger maps to a specific domain state transition that closeloop's API already
  owns (deal stage advance, assignment change, activity creation). The enum is closed — it does
  not accept arbitrary plugin-defined event types. The `INotificationDispatcher` interface exposes
  one method per trigger (not a generic `Dispatch(object event)` signature), so callers are
  type-checked at compile time.
- **From**: HubSpot CRM — the HubSpot webhook subscription system defines discrete named event
  types (`deal.propertyChange`, `contact.propertyChange`, `deal.creation`, etc.) rather than a
  generic change feed; the HubSpot Notifications API similarly groups in-app notifications by
  named category. HubSpot's task-due reminder is a first-class trigger, not derived from a generic
  activity-changed event.
- **Why it fits**: closeloop's MVP has exactly six notification-worthy events corresponding to
  domain state changes the API layer already controls. A closed enum keeps the `INotificationDispatcher`
  interface stable and its callers obvious — every endpoint that mutates a relevant field either
  calls a dispatcher method or does not. The six triggers cover the two primary ownership-change
  events, the deal-health signal (rotting), the collaboration surface (mention), the pipeline
  progression event (stage change), and the task management surface (task due) without requiring
  a generalised event-sourcing infrastructure.

### 2. Attio's @mention surface: `@username` syntax in `Activity.Note` parsed on write

- **What**: When a `POST /activities` or `PATCH /activities/{id}` request includes `Note` text
  containing `@<userId>` patterns, the API endpoint parses the mention list, resolves each to a
  `RecipientUserId`, and passes the list to `INotificationDispatcher.ActivityMentionAsync`.
  The dispatcher creates one `Notification` record per mentioned user with
  `Trigger = ActivityMention`, `RelatedEntityId = activity.Id`, `RelatedEntityType = Activity`,
  and `Title = "@<actor> mentioned you in a note on <anchor entity name>"`. Mention parsing
  occurs in the application layer (the endpoint handler), not the domain entity, so `Activity.Note`
  remains a plain `string?` with no mention-awareness in the domain.
- **From**: Attio — @mention in Notes fires a notification to the named workspace member; the
  `GET /v2/inbox` API returns mention events as a distinct notification type with `actor`,
  `target_record`, and `note_id` fields. Attio's mention model is user-directed (mention a team
  member, not a CRM record); closeloop's adoption focuses on this user-directed collaboration
  dimension.
- **Why it fits**: The `Activity.Note` field is already a plain `string?` on the `Activity`
  entity — no schema change is needed. Mention parsing at the endpoint layer keeps the domain
  entity simple and avoids coupling the domain to a user-directory concept. The `@<userId>` format
  (UUID rather than display name) is unambiguous and avoids display-name collision; the client
  renders the `@username` UI overlay from the mention list in the response.

### 3. Pipedrive's deal-rotting notification with pipeline-level threshold

- **What**: Each `Pipeline` carries a `RottingThresholdDays: int?` field (null = rotting disabled
  for that pipeline). A background `DealRottingNotificationJob` hosted service runs every 6 hours.
  For each active deal, it evaluates `MAX(Activity.OccurredAt WHERE DealId = deal.Id)` against
  `now() - pipeline.RottingThresholdDays`. If the deal has crossed the threshold and no
  `DealRotting` notification exists for it within the last 24 hours, the job creates a Notification
  for `deal.OwnerId` with `Trigger = DealRotting` and
  `Title = "Deal '<deal.Title>' has had no activity for <N> days"`. The 24-hour deduplication
  guard prevents the job from firing a new notification on every 6-hour scan cycle for the same
  stale deal.
- **From**: Pipedrive — the pipeline-level `rotten_days` configuration and the in-app "deal
  rotting" notification that fires to the deal owner. Pipedrive also surfaces the rotting flag on
  the Kanban card (already borrowed in `.devclaw/research/deals.md` Borrowed §3); this item
  concerns the notification delivery, not the card indicator.
- **Why it fits**: The `RottingThresholdDays` field on `Pipeline` is consistent with the Deals
  research artifact's `isRotting` flag, which already references this threshold. Adding
  `RottingThresholdDays: int?` to `Pipeline` and `PipelineConfiguration` in EF Core is a
  one-field additive migration. The background job reuses the `MAX(Activity.OccurredAt)` SQL
  aggregation already cited in the deals research; the Notification entity deduplication guard
  is a `COUNT(*) WHERE DealId = ? AND Trigger = DealRotting AND CreatedAt > now()-24h > 0`
  guard query. No domain-layer change beyond the field addition is needed.

---

## Rejected & why

### A. Salesforce's configurable rule engine (Process Builder / Flow "Send Custom Notification")

- **What was considered**: A configurable automation layer where an administrator (or developer)
  defines notification rules: "when Opportunity.StageName changes to X, send a Custom Notification
  to the record owner". Rules are stored as configuration objects (`CustomNotificationType`,
  Flow records) and evaluated at runtime by a workflow engine. closeloop's equivalent would be a
  `NotificationRule` entity (`{ Trigger, FilterField, FilterValue, RecipientRole }`) with a rule
  evaluation engine at the API layer.
- **Source**: Salesforce — Process Builder "Send Custom Notification" action;
  `CustomNotificationType` standard object; Lightning Flow `customNotificationAction` invocable
  action. Salesforce help articles on "Set Up Notifications" and "Notification Builder".
- **Reason rejected**: A rule engine requires a rule-storage schema, a rule-evaluation engine,
  and a configuration UI — three additional feature slices for a problem that has six concrete
  answers in the MVP. Every one of closeloop's six notification triggers corresponds to an existing
  domain state transition that the API already owns and controls. Hard-coding six dispatcher calls
  in six endpoint handlers is less complexity, less surface area, and more auditable than a runtime
  rule evaluation loop. The closed `NotificationTrigger` enum is the rule set; expanding it later
  (a seventh trigger) is a type-safe additive change, not a schema migration of rule records.

### B. HubSpot's webhook-first external delivery model (outbound HTTP push to subscriber URLs)

- **What was considered**: Surfacing notification events as outbound webhook deliveries to a
  configurable subscriber URL (`POST <subscriberUrl>` with a JSON payload per trigger), following
  HubSpot's webhook subscriptions model (`POST /webhooks/v3/{appId}/subscriptions`). The internal
  `Notification` entity would be skipped in favour of a pure push model; consumers poll their own
  endpoint.
- **Source**: HubSpot — CRM webhook subscriptions (`deal.propertyChange`, `contact.propertyChange`
  subscription types); `POST /webhooks/v3/{appId}/subscriptions` with `subscriptionDetails.propertyName`;
  HubSpot's batched webhook delivery (up to 100 events per POST).
- **Reason rejected**: closeloop's SMB target user does not run a webhook receiver; they interact
  with the in-app notification bell. Outbound webhook delivery requires subscriber registration
  management, delivery retry logic, signature verification, and a dead-letter queue — all
  infrastructure concerns that are out of scope for an in-app notification feed. The in-app
  `Notification` entity stored in `CrmDbContext` is queryable from the same `GET /notifications`
  endpoint pattern as every other resource; no additional delivery infrastructure is needed. A
  webhook-out adapter can be layered on top of the `INotificationDispatcher` interface later
  without changing the entity or trigger taxonomy.

### C. Attio's "follow a record" subscription model (user-directed watch)

- **What was considered**: Letting users subscribe to individual records (a Contact, Company, or
  Deal) and receive notifications for any change to that record — attribute edits, new notes,
  stage changes, new associations. Attio's `POST /v2/lists/{list_id}/entries/{entry_id}/followers`
  adds the authenticated user as a follower; the `GET /v2/inbox` API then returns follower-update
  events alongside mention events.
- **Source**: Attio — "Record following" feature; `GET /v2/inbox` returning follower-update events
  with `record_id`, `changed_by`, and `changes` arrays; Attio's public changelog "Follow records
  to stay updated" (2023).
- **Reason rejected**: The "follow a record" model requires a `RecordFollower` junction entity
  (`{ UserId, EntityType, EntityId }`), a subscription management API (`POST /follows`,
  `DELETE /follows/{id}`), and a change-detection mechanism that compares attribute snapshots on
  every write to determine which followers should be notified. This is significantly more
  infrastructure than the six hard-coded triggers and produces a noisier notification feed (every
  field edit on a watched deal fires a notification). closeloop's MVP notification strategy is
  targeted: ownership changes affect the new owner, stage changes affect the deal owner, mentions
  affect named users. Broad-subscription noise is the failure mode that causes users to turn off
  notifications entirely; the six targeted triggers avoid it.

### D. Pipedrive's email/SMS fallback delivery for notification events

- **What was considered**: When a user has not read an in-app notification within a configurable
  delay (e.g. 30 minutes), sending the same notification as an email to the user's registered
  address. Pipedrive offers email notification fallback for deal-rotting alerts and activity-due
  reminders; the Pipedrive user settings UI shows per-trigger email opt-in toggles.
- **Source**: Pipedrive — user notification settings UI (`/settings/notifications`); per-trigger
  email toggles for "Deal rotting", "Activity reminder", "Deal assigned"; Pipedrive help article
  "How to manage email notifications in Pipedrive".
- **Reason rejected**: Email delivery requires an SMTP provider integration (or transactional
  email API like SendGrid), a user preference schema (`NotificationPreference` entity with
  per-trigger email-opt-in flags), and a delivery-delay scheduler. All three are separate feature
  slices. The `Notification` entity's `IsRead` field and the `PATCH /notifications/{id}/read`
  endpoint provide the in-app read signal; email fallback is a natural follow-on once the in-app
  surface is stable. Introducing email delivery in the same slice would conflate the notification
  data model with a delivery infrastructure concern that the MVP does not need.
