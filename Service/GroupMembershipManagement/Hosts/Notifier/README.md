# Notifier Function

The Notifier function processes notification messages and sends emails to group owners about sync job events (threshold violations, sync started/completed, inactive jobs, etc.).

## Architecture

```
Producers (7 services) ──► Service Bus Topic: "notifications"
                                └── Subscription: "notifier" [Filter: 1=1]
                                        └── StarterFunction (ServiceBusTrigger)
                                                ├── Check suppression → Defer if disabled
                                                └── Process → OrchestratorFunction → Send email
```

### Message Flow

1. Producers (GraphUpdater, MembershipAggregator, WebApi, etc.) send messages to the `notifications` topic with `ApplicationProperties["MessageType"]` set.
2. The `notifier` subscription (catch-all filter `1=1`) receives all messages.
3. `StarterFunction` triggers and checks suppression:
   - If `NotificationType.Disabled` is `true` → **defers** the message (retained in subscription).
   - If `SkipEmailNotifications` config is `true` → **defers** the message.
   - Otherwise → starts the `OrchestratorFunction` to process and send email.
4. `ReplayDeferredNotificationsFunction` (timer, every 5 min) checks for re-enabled types and replays deferred messages.

## Notification Suppression

### Suppressing a Notification Type

To suppress a specific notification type without losing messages:

```sql
UPDATE NotificationTypes SET Disabled = 1 WHERE Name = 'ThresholdNotification';
```

**Effect:** Messages of that type are deferred (not consumed, not lost). They remain in the Service Bus subscription and are tracked in the `DeferredNotifications` database table.

### Unsuppressing (Replay)

To re-enable a notification type and deliver accumulated messages:

```sql
UPDATE NotificationTypes SET Disabled = 0 WHERE Name = 'ThresholdNotification';
```

**Effect:** The `ReplayDeferredNotificationsFunction` (runs every 5 minutes) will:
1. Detect the type is no longer disabled
2. Retrieve deferred messages by sequence number from the subscription
3. Re-send them to the topic for normal processing
4. Clean up the `DeferredNotifications` table

### Available Notification Types

| Enum Value | Name |
|-----------|------|
| 0 | ThresholdNotification |
| 1 | SyncStartedNotification |
| 2 | SyncCompletedNotification |
| 3 | DestinationNotExistNotification |
| 4 | SourceNotExistNotification |
| 5 | NotOwnerNotification |
| 6 | NotValidSourceNotification |
| 7 | NoDataNotification |
| 8 | NormalThresholdNotification |
| 9 | InactiveSyncJobNotification |
| 10 | GuestUserFailureNotification |
| 11 | SubmissionRejectedNotification |
| 12 | JobPurgingWarningNotification |
| 13 | SubmissionApprovedNotification |
| 14 | NestedGroupsFoundNotification |

## Monitoring

### Application Insights Queries

**Deferred messages by type:**
```kusto
customEvents
| where name == "NotificationDeferred"
| summarize count() by tostring(customDimensions.MessageType), tostring(customDimensions.Reason)
| order by count_ desc
```

**Replayed messages:**
```kusto
customEvents
| where name == "NotificationsReplayed"
| project timestamp, MessageType = tostring(customDimensions.MessageType), Count = toint(customDimensions.Count)
| order by timestamp desc
```

**Currently deferred messages (DB query):**
```sql
SELECT MessageType, COUNT(*) AS PendingCount, MIN(DeferredAt) AS OldestDeferred
FROM DeferredNotifications
GROUP BY MessageType;
```

### Alerts

- Monitor `DeferredNotifications` table row count — high accumulation may indicate a forgotten suppression.
- Messages have a 14-day TTL in the subscription. Deferred messages that exceed TTL will expire and be lost.

## Configuration

| Setting | Description | Default |
|---------|-------------|---------|
| `serviceBusNotificationsTopic` | Service Bus topic name | `notifications` |
| `serviceBusNotificationsSubscription` | Subscription name | `notifier` |
| `serviceBusFailedNotificationsQueue` | Queue for failed email sends | `failedNotifications` |
| `notifierReplaySchedule` | CRON for replay function | `0 */5 * * * *` (every 5 min) |
| `SkipEmailNotifications` | Global email suppression (defers messages) | `false` |

## Migration from Queue to Topic

### Zero-Downtime Migration Steps

1. **Deploy infrastructure** — Bicep creates the `notifications` topic + `notifier` subscription. The old queue still exists.
2. **Deploy Notifier** — New trigger binds to topic subscription. Old queue messages drain naturally.
3. **Verify** — Confirm Notifier is processing from the topic subscription.
4. **Remove old queue** — Once drained, delete the `notifications` queue resource.

### Key Points
- `ServiceBusSender` works identically for queues and topics — producers need zero code changes.
- The `serviceBusNotificationsQueue` Key Vault secret now points to the topic name for backward compatibility.
- The `failedNotifications` queue remains unchanged.

## Project Structure

```
Hosts/Notifier/
├── Function/
│   ├── Starter/StarterFunction.cs          # Topic trigger, suppression check
│   ├── Orchestrator/OrchestratorFunction.cs # Routes by message type
│   ├── Activity/
│   │   ├── SendNotification/               # Generic email send
│   │   ├── SendThresholdNotification/      # Threshold-specific email
│   │   ├── ReplayDeferredNotifications/    # Timer-triggered replay
│   │   └── ...
│   ├── Program.cs                          # DI setup
│   └── host.json                           # autoCompleteMessages: false
├── Services.Notifier/                      # Business logic
├── Services.Notifier.Contracts/            # Interfaces + LogMessages
├── Services.Notifier.Tests/               # Unit tests
└── Infrastructure/compute/template.bicep   # Deployment
```
