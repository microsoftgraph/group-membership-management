# Update auto-approval

With `IsAutoApprovalForConfigurationUpdatesEnabled` enabled, WebApi classifies an
`Update` as configuration-only when the final normalized query is unchanged and
every non-query patch path is allowlisted. Non-allowlisted changes still require
manual review. With the setting disabled, updates retain the existing
`PendingReview` behavior.

| Pre-edit status | Configuration-only update |
|---|---|
| `Idle`, `ThresholdExceeded` | WebApi saves an `Update` audit row, sets `PendingAutoApproval`, and queues `Origin = ConfigUpdate`. AutoApprover sets `Idle` unconditionally, writes `UpdateAutoApproved`, and emits the update `JobReviewed` metric. |
| Other error states (including `NestedGroupsFound` and `ManagerNotFound`), `CustomerPaused`, `DeveloperPaused`, `SubmissionRejected` | WebApi saves configuration with the status unchanged and a plain `Update` audit row. No queue message, approval metric, or notification. |
| `New`, `All` (filter value only) | `PendingReview`. |
| `InProgress`, `StuckInProgress`, `PendingReview`, `PendingConfiguration`, `PendingAutoApproval` | Update rejected with HTTP 412, unchanged. |

`ConfigUpdate` skips query-rule evaluation because WebApi already established that
the query did not change. The message carries `ConfigurationQuery`, the persisted
query snapshot. If it is missing or no longer matches, the consumer logs a warning
and ignores the message without changing the job, auditing an approval, or
evaluating a newer submission under the older requestor's identity.
Query-changing submissions retain `Origin = Update` and
the existing query-rule evaluation. Both update origins leave scheduling fields
untouched; only onboarding can push a past `StartDate` out by 24 hours.

Only the AutoApprover host records actual automatic approvals. Queued
configuration updates no longer send WebApi's synchronous approval notification.
If its queue repository is unavailable or enqueueing fails, WebApi falls back to
`PendingReview`. Status-preserving configuration edits do not require the queue.
The consumer still requires `PendingAutoApproval` before processing, so messages
for jobs that have changed status are ignored.

## Deployment order

Deploy AutoApprover before WebApi produces `ConfigUpdate` messages. Existing wire
values remain `Onboarding = 0`, `Update = 1`; `ConfigUpdate = 2` is appended.
Messages without `Origin` still default to onboarding. Older consumers do not
understand the new unconditional configuration-approval path; do not run them
against a queue containing `ConfigUpdate` messages. Disable configuration updates
and drain these messages before rolling back AutoApprover.
