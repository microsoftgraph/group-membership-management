# TeamsChannel Sync — Technical Specification

**Status:** Implemented · **Feature flag:** `enableTeamsChannel` (default `false`) · **Scope:** Microsoft Teams shared / undefined channel destinations

This document is the technical specification for the **GMM sync-for-channels** scenario: synchronizing the membership of a Microsoft Teams **channel** (shared / undefined) from one or more GMM source queries. It defines the data model, message contracts, component responsibilities, control flow, error handling, configuration, security, and telemetry.

**Related documents**
- [`TeamsChannelSync.md`](./TeamsChannelSync.md) — customer-facing setup / onboarding.
- [`teamschannel-architecture.md`](./teamschannel-architecture.md) — architecture overview with diagrams.
- [`TeamsChannelServiceAccountSetup.md`](../../Service/GroupMembershipManagement/Repositories.TeamsChannel/Documentation/TeamsChannelServiceAccountSetup.md) — service-account provisioning.
- [`TeamsChannelTypeDestinationSchema.md`](../../Service/GroupMembershipManagement/Hosts/TeamsChannelMembershipObtainer/Documentation/TeamsChannelTypeDestinationSchema.md) — destination query schema.

---

## 1. Overview

GMM normally keeps an **Entra ID group** in sync with a set of source queries. The TeamsChannel scenario extends this so the **destination is a Teams channel** instead of a security group.

Two things make a channel different from a group:

1. **Reading current membership.** A channel's membership lives under the team's Graph object (`/teams/{teamId}/channels/{channelId}/members`), not on a group. A dedicated obtainer, **TeamsChannelMembershipObtainer**, reads it and presents it to the shared aggregation pipeline as the *destination part*.
2. **Writing membership.** Channel members are `conversationMember` resources, added/removed one at a time through channel-specific Graph endpoints. A dedicated **TeamsChannelUpdater** applies the computed delta.

Everything in between — source obtaining, aggregation, threshold safety checks — is **the shared GMM pipeline**, unchanged.

### 1.1 Goals
- Keep a shared / undefined (flexible) Teams channel's membership equal to the result of a GMM source query.
- Reuse the existing obtain → aggregate → threshold pipeline.
- Fail safe: never write membership to a channel GMM does not own or cannot validate.

### 1.2 Non-goals
- **Standard and private channels** are out of scope (rejected at validation).
- TeamsChannel as a **source** part. Currently destination-only.
- **Bulk Graph writes.** Adds/removes are issued one member per Graph call (see §6.3).

---

## 2. Terminology

| Term | Meaning |
| --- | --- |
| **Destination part** | The membership of the channel *as it currently is* (read by the obtainer). |
| **Source part(s)** | Membership computed from the job's source queries (who *should* be in the channel). |
| **Delta** | `Add` set (source − destination) and `Remove` set (destination − source) produced by the aggregator. |
| **Service account** | A licensed user account GMM uses for channel Graph operations (channel member writes require delegated auth, not app-only). |
| **Part metadata** | `TotalParts` / `CurrentPart` / `IsDestinationPart` / `Exclusionary`, carried on each Service Bus message. |

---

## 3. Data model

### 3.1 Destination query schema
A TeamsChannel destination is expressed as a one-element destination array on the `SyncJob`:

```json
[
  {
    "type": "TeamsChannelMembership",
    "value": { "objectId": "<TEAM_GROUP_ID_GUID>", "channelId": "<CHANNEL_ID_STRING>" }
  }
]
```

- `objectId` — the **team's** backing M365 group id.
- `channelId` — the channel id (e.g. `19:...@thread.tacv2`).

### 3.2 Key types

| Type | Location | Notes |
| --- | --- | --- |
| `AzureADTeamsChannel` | `Models` | `{ ObjectId (group), ChannelId }`. |
| `AzureADTeamsUser` | `Models` | A channel member: `ObjectId` + `MembershipAction` (`Add`/`Remove`). |
| `ChannelSyncInfo` | `TeamsChannelMembershipObtainer.Service.Contracts` | Obtainer input: `SyncJob`, `TotalParts`, `CurrentPart`, `Exclusionary`, `IsDestinationPart`. |
| `TeamsGroupMembership` | `Models.ServiceBus` | Membership payload exchanged via blob: `Destination`, `SourceMembers`, `RunId`, `SyncJobId`, `Exclusionary`, `Query`, plus split metadata (`MembersPerChunk = 3765`). |

### 3.3 Relevant `SyncStatus` values

| Value | Int | Meaning |
| --- | --- | --- |
| `TeamsChannelNotDestination` | 15 | Part flagged as TeamsChannel but not marked as the destination part. |
| `StandardTeamsChannel` | 18 | Channel resolved to a `standard` or `private` type — not supported. |
| `TeamsChannelError` | 19 | One or more member add/remove operations permanently failed. |
| `NotOwnerOfDestinationGroup` | 7 | GMM service account is not an owner of the team/channel. |

---

## 4. Components & responsibilities

| Component | Trigger | Responsibility |
| --- | --- | --- |
| **JobTrigger** | Timer, every 5 min | Detect due `TeamsChannelMembership` jobs; resolve channel; verify ownership/writeability; on initial sync enqueue the onboarding **started** email; split into parts onto Service Bus. |
| **Source obtainers** | Service Bus | Produce source membership blobs (unchanged from group sync). |
| **TeamsChannelMembershipObtainer** | Service Bus (sub `TeamsChannelMembership`) | Validate channel; read current channel members; upload destination blob; request aggregation. |
| **MembershipAggregator** | Service Bus | Union source parts, diff against destination part, apply thresholds, route delta. |
| **TeamsChannelUpdater** | Timer, every 30 s | Drain updater queue; apply add/remove delta to channel via Graph; update status; on initial sync enqueue the onboarding **completion** email (Notifier sends it). |
| **Repositories.TeamsChannel** | — | Graph wrapper for all channel operations. `DisabledTeamsChannelRepository` injected when flag off. |

---

## 5. Control flow

### 5.1 JobTrigger (routing & gatekeeping)
For a due job with `Destination` type `TeamsChannelMembership`:

1. Resolve the destination `AzureADTeamsChannel` (group id + channel id).
2. `TeamsChannelExistsAndGMMCanWriteToItAsync`:
   - `TeamsChannelExistsAsync` — channel still exists.
   - **Conditional ownership check** — if `GMMHasChannelReadWriteAllPermissions` is `false`, run `CheckGMMIsChannelOwner` → `IsServiceAccountOwnerOfChannelAsync`. If GMM holds `ChannelMember.ReadWrite.All` application permission, the owner check is skipped.
   - On failure → mark job `NotOwnerOfDestinationGroup` and stop.
3. Standardize the destination string and split the job into source parts + **one destination part** (`IsDestinationPart = true`).
4. **Initial sync only** (`LastRunTime == MinValue`): enqueue an onboarding **"started"** email (`SyncStartedNotification`) to the Service Bus notification queue (`JobTriggerService.SendEmailAsync`); the **Notifier** sends the actual email.
5. Publish parts to the Service Bus topic. The destination part lands on the `TeamsChannelMembership` subscription.

### 5.2 TeamsChannelMembershipObtainer (read destination)
Service Bus–triggered durable orchestration:

1. **Validate** (`VerifyChannelAsync`):
   - Missing `GroupId`/`ChannelId` → `Error`.
   - Not the destination part → `TeamsChannelNotDestination` (15).
   - `GetChannelTypeAsync == "standard"` / `"private"` → rejected (`StandardTeamsChannel` (18)).
   - `shared` / undefined (flexible) → proceed.
2. **Read members** (`ReadUsersFromChannelAsync`) — page `/teams/{group}/channels/{id}/members`, `excludeOwners = true`.
3. **Upload** the destination membership blob (`TeamsGroupMembership` JSON).
4. **Request aggregation** (`MakeMembershipAggregatorRequestAsync`).

### 5.3 MembershipAggregator (shared)
Waits for **all** parts of the run, then:
1. Download & union source parts (honor exclusionary parts).
2. Diff source vs destination part → `Add` / `Remove` delta.
3. Apply owner **threshold** percentages; over threshold → hold for review / notify instead of writing.
4. Emit a `MembershipHttpRequest` routed by destination type to the **TeamsChannelUpdater** queue.

> **Multi-lane / MessageSplitter routing applies to `GroupMembership` destinations only** (gated on `MembershipType == GroupMembership`). TeamsChannel jobs always route directly to the updater regardless of delta size.

### 5.4 TeamsChannelUpdater (write destination)
A **singleton** `QueueMessageOrchestratorFunction` (kept alive by a 30 s timer + `ContinueAsNew`) drains the updater queue:

1. Read next message → `OrchestratorFunction`.
2. Read job, resolve group/channel, download & deserialize the aggregated `TeamsGroupMembership`.
3. Split delta by `MembershipAction` into **Add** and **Remove** sub-orchestrations.
4. Each sub-orchestration (`TeamsChannelUpdaterSubOrchestratorFunction`) processes members in **chunks of `_batchSize = 100`** (in-memory orchestration chunking — **not** a Graph `$batch`), calling `TeamsUpdaterFunction` → repository. Within a chunk the repository issues **one Graph call per user**, so a chunk of 100 = 100 sequential `POST`/`DELETE` calls (see §6.3).
5. On **initial sync** (`LastRunTime == FromFileTimeUtc(0)`) enqueue the onboarding **completion** email to the Service Bus notification queue (the **Notifier** sends it), pairing with the **started** email from JobTrigger (§5.1).
6. Set final status: `Idle` on full success, `TeamsChannelError` (19) if any operation permanently failed.
7. Emit `SyncComplete` telemetry.

---

## 6. Microsoft Graph contract

### 6.1 API calls in order

| Phase | Operation | Graph call |
| --- | --- | --- |
| JobTrigger | Channel exists | `GET /teams/{teamId}/channels/{channelId}` |
| JobTrigger | Ownership (conditional) | `GET /teams/{teamId}/channels/{channelId}/members` (owner check) |
| JobTrigger / Updater | Display names | `GET` group name; `$batch` for channel display names (`GetTeamsChannelNamesAsync`) |
| Obtainer | Channel type | `GET /teams/{teamId}/channels/{channelId}` → `membershipType` |
| Obtainer | Read members | `GET /teams/{teamId}/channels/{channelId}/members` (paged) |
| Updater | Add member | `POST /teams/{teamId}/channels/{channelId}/members` (one per user) |
| Updater | Remove member | `DELETE /teams/{teamId}/channels/{channelId}/members/{membershipId}` (one per user) |

> **Cue:** the **ownership** call is *conditional* — skipped when GMM holds `ChannelMember.ReadWrite.All` application permission (§6.2). The **add/remove** calls fire *once per user* per chunk (§6.3); all other calls fire once per run.

### 6.2 Required permissions
- **Channel writes** require **delegated** auth via the **service account** (app-only is not supported for channel member writes). Credentials come from `TeamsGraphCredentials` + `teamsChannelServiceAccount*` secrets.
- If GMM is granted `ChannelMember.ReadWrite.All` *application* permission (`TeamsChannel:IsChannelReadWriteApplicationPermissionGranted = true`), the JobTrigger owner-check is skipped, but the service account is still used for member writes.

### 6.3 Chunking (no Graph `$batch` for writes)
- **No Graph `$batch` for member writes.** The "chunk of 100" is in-memory orchestration chunking; the repository loops **one `POST`/`DELETE` per user**.
- The only `$batch` usage is `GetTeamsChannelNamesAsync` (display-name lookups).
- Graph *does* support bulk `POST /teams/{id}/channels/{channel}/members/add` (up to 200/request) for **adds only**; removes have no bulk action. This is a known optimization opportunity, not currently implemented.

### 6.4 Result bucketing (writes)
`AddUsersToChannelAsync` / `RemoveUsersFromChannelAsync` classify each per-user result:
- **Success** — counted as added/removed.
- **Not found** — e.g. `"Unable to resolve the recipient"` (NotFound); guest-in-shared-channel `BadRequest` — recorded, not retried.
- **Retry** — `UnknownError` / `BadGateway` → retried once by the sub-orchestrator before being recorded as a failure.

---

## 7. Configuration

| Setting | Where | Default | Purpose |
| --- | --- | --- | --- |
| `enableTeamsChannel` | feature flag (all relevant hosts) | `false` | Gates TeamsChannel support; when off, `DisabledTeamsChannelRepository` is injected and Teams secrets resolve to `not-set`. |
| `triggerSchedule` (Updater) | `TeamsChannelUpdater` bicep / settings | `0,30 * * * * *` (every 30 s) | Keeps the singleton drain orchestrator alive. |
| `TeamsChannel:IsChannelReadWriteApplicationPermissionGranted` | JobTrigger | `false` | Maps to `GMMHasChannelReadWriteAllPermissions`; skips owner check when `true`. |
| `_batchSize` | `TeamsChannelUpdaterSubOrchestratorFunction` | `100` (hardcoded) | In-memory chunk size for add/remove processing. |
| `TeamsGraphCredentials__*`, `teamsChannelServiceAccount*` | Key Vault | — | Service-account credentials for channel Graph operations. |

---

## 8. Validation, edge cases & risks

- **Channel-type check.** `VerifyChannelAsync` rejects `standard` and `private` channels; only `shared` / undefined (flexible) types proceed.
- **Ownership is mandatory unless app permission granted.** Without `ChannelMember.ReadWrite.All`, the service account must own both the team and the channel, or the job is marked `NotOwnerOfDestinationGroup`.
- **Owners excluded from reads.** Channel reads use `excludeOwners = true`, so the GMM service account (an owner) is never treated as a member to remove.
- **Adds before removes**, as separate sub-orchestrations, so counts/telemetry are tracked independently.
- **Thresholds apply** exactly as for group sync — large deltas are held for owner review rather than written.

---

## 9. Telemetry & observability

- `SyncComplete` custom event on each updater run: members added/removed, not-found counts, projected member count, and result classification (**Success / PartialSuccess / Failure**).
- Per-phase failures route through `TelemetryTrackerFunction` + `JobStatusUpdater`.
- Final job status (`Idle` / `TeamsChannelError`) is the primary health signal for a channel sync.

---

## 10. Source references

| Area | Path |
| --- | --- |
| JobTrigger routing | `Hosts/JobTrigger/Function/SubOrchestrator/SubOrchestratorFunction.cs`, `Hosts/JobTrigger/Services/JobTriggerService.cs` |
| Obtainer | `Hosts/TeamsChannelMembershipObtainer/Function/...`, `Services/.../TeamsChannelMembershipObtainerService.cs` |
| Updater | `Hosts/TeamsChannelUpdater/Function/QueueMessageOrchestrator/...`, `.../Orchestrator/OrchestratorFunction.cs`, `.../TeamsChannelUpdaterSubOrchrestrator/TeamsChannelUpdaterSubOrchestratorFunction.cs`, `.../Activity/TeamsUpdater/TeamsUpdaterFunction.cs` |
| Graph repository | `Repositories.TeamsChannel/TeamsChannelRepository.cs`, `DisabledTeamsChannelRepository.cs` |
| Contracts / models | `Models/ServiceBus/TeamsGroupMembership.cs`, `Hosts/TeamsChannelMembershipObtainer/Services.Contracts/ChannelSyncInfo.cs`, `Models/SyncStatus.cs` |
| Config | `Hosts/JobTrigger/Function/Program.cs`, `Hosts/TeamsChannelUpdater/Infrastructure/compute/template.bicep` |
