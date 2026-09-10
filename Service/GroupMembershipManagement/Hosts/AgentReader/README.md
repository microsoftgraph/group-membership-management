# AgentReader Function

The AgentReader function populates the per-run `agents.[R]` table from Microsoft Graph so that
downstream membership processing can resolve agent identities for an Azure Data Factory pipeline run.

It is invoked by ADF once per pipeline run, after that run's agents table and index have been created.

## Architecture

```
ADF pipeline (AzureFunctionActivity, retry: 0)
        │  POST { "RunId": "<pipeline().RunId>" }
        ▼
StarterFunction ──► AgentReaderStartEntity  (one persisted marker per RunId)
                            │
                            ▼
                    OrchestratorFunction  (task hub: "AgentReader")
                            │
        ┌───────────────────┼────────────────────────┐
        ▼                   ▼                        ▼
 ValidateAgentTable   AgentReaderFunction      AgentWriterFunction
        │             (loops per Graph page)          │
        ▼                   ▼                        ▼
 AgentTableRepository  AgentGraphRepository    AgentTableRepository
 (exists/indexed/      (Repositories.          (serializable,
  empty)                GraphAgents)            content-idempotent)
                            │
                            ▼
                     GetAgentRowCount ──► final reconciliation
```

Repositories live in the shared root projects `Repositories.GraphAgents` and `Repositories.AgentsTable`,
following the same layering as `SqlMembershipObtainer`, `SqlDataChecker` and `AzureUserReader`.

## Flow

1. **ADF** posts `{ "RunId": "<pipeline().RunId>" }`. The start activity depends on the *index*-creation
   activity, not merely table creation.
2. **StarterFunction** parses the run identifier, rejecting an absent, empty, non-string, unparseable or
   all-zero value rather than substituting a default. It derives the orchestration instance id and
   returns any existing instance without restarting it. Otherwise it signals `AgentReaderStartEntity`,
   whose key is the canonical run identifier. The entity alone decides whether to schedule the load.
   The starter waits until the orchestration exists (including `Pending`) before returning
   `StatusQueryGetUri`; it does not wait for the load to finish.
3. **ValidateAgentTable** requires that `agents.[R]` already exists, that `IDX_Agents_ManagerId` is
   present and enabled, and that the table is empty. The component performs **no DDL** of any kind.
4. **AgentReaderFunction** reads one Graph page per call:
   ```
   /v1.0/users/microsoft.graph.agentUser
     ?$select=id,accountEnabled,agentIdentityBlueprintId
     &$expand=manager($select=onPremisesImmutableId)
   ```
   Before parsing, the raw `value` array is counted and compared against what the SDK materialised. An
   entry the SDK cannot represent would otherwise be dropped silently and shorten the table, so a
   mismatch fails the run. Continuation links are validated to be the same HTTPS host and path.
5. **AgentWriterFunction** persists the eligible agents for that page.
6. Steps 4-5 repeat while Graph returns `@odata.nextLink`. A repeated or blank continuation link fails
   the run.
7. **GetAgentRowCount** drives the final reconciliation.
8. **ADF** polls `statusQueryGetUri` until the orchestration leaves `Pending`/`Running`, then gates on
   `runtimeStatus == 'Completed'` as an allowlist. The worker emits the field as `StatusQueryGetUri`;
   Data Factory resolves activity output properties case-insensitively, so the pipeline expression
   matches either casing.

## Eligibility rules

`ManagerId` is sourced **only** from `manager.onPremisesImmutableId` and must fit an `Int32`. Each
rejection increments its own counter:

| Condition | Counter |
|---|---|
| No manager relationship | `FilteredNoManager` |
| Manager present, identifier absent or blank | `FilteredManagerEmployeeIdAbsent` |
| Identifier is not an integer | `FilteredManagerEmployeeIdNonNumeric` |
| Identifier is an integer but exceeds `Int32` | `FilteredManagerEmployeeIdOutOfRange` |
| Agent id missing, unparseable, or all-zero | `FilteredInvalidAgentObjectId` |

`agentIdentityBlueprintId` is read from the original Graph JSON string, preserving the exact text even
when the SDK converts GUID- or date-shaped strings to other types. Missing or null values remain null;
actual non-string JSON values fail the run. Values longer than 200 characters are truncated and counted
as `BlueprintIdTruncated`.
A missing `accountEnabled` defaults to `true` and is counted as `AccountEnabledDefaultApplied`.

> The counter names retain `...ManagerEmployeeId...` even though the value is read from
> `onPremisesImmutableId`.

## Terminal status

| Status | Meaning |
|---|---|
| `Completed` | At least one eligible agent was enumerated and every one was persisted |
| `CompletedEmpty` | The tenant legitimately has no eligible agents; zero rows is correct |
| *failure* | Enumeration was incomplete, or eligible agents were found but not all persisted |

`FilterPassingAgents` is the signal that separates an agent-free tenant from a load that wrote nothing,
which is why it is emitted for every run.

The orchestration fails rather than accepting a partial load. There are **no compensating deletes** — a
failed run leaves its table as-is, and recovery is a fresh pipeline run with a new run identifier.

## Duplicate-start protection

`agents.[R]` carries **no uniqueness constraint of any kind**. Duplicate prevention must cover both
repeated Graph work and duplicate SQL rows; an empty table is not proof that the load never ran.

The starter never calls `ScheduleNewOrchestrationInstanceAsync`. It sends `Start` to the durable entity
identified by `AgentReaderStartEntity` and the canonical `RunId`. Operations for that entity are
serialized, including requests received on different function-app instances.

The first `Start` operation queues the orchestration through the entity context's
`ScheduleNewOrchestration` API and records its boolean start marker. Both are part of the same Durable
entity operation: if the operation fails, the SDK rolls back its state and outgoing start action.
Later operations for that key observe the marker and emit no orchestration start. Do not move the
scheduling call back into the HTTP starter after a separate entity claim; that would split the durable
operation again.

| # | Measure | What it covers |
|---|---|---|
| 1 | `retry: 0` on the invoking ADF activity | ADF's automatic retry reuses the same `pipeline().RunId` |
| 2 | Durable start marker keyed on `RunId` alone | Serializes duplicate start requests and remembers completed-empty loads |
| 3 | Stable `AgentReader-{RunId}` orchestration id | Every caller polls the same execution; existing instances are returned without restarting |
| 4 | `ValidateAgentTable` requires an empty table | Rejects accidental direct reloads of already-populated tables |
| 5 | `WriteAsync` is idempotent by content | Protects rows against activity redelivery and overlapping writes |

Neither the entity key nor the orchestration id may include an invocation, message, or attempt id.
The marker is retained after successful, empty, and failed outcomes. A fresh ADF pipeline run gets a
new RunId and therefore an independent marker.

### Start confirmation and recovery

An entity signal acknowledges enqueueing, not execution. The starter checks for the orchestration every
250 ms with a 30-second deadline, honoring caller cancellation. Until the instance exists, it does not
return a successful polling response. A confirmation timeout produces HTTP `503` with
`errorCode: AgentReaderStartNotConfirmed`, logs the failure with RunId, and includes no
`StatusQueryGetUri`. Other client/storage failures propagate; there is no direct-start fallback.

Keep start markers for as long as their RunIds can be submitted again. Do not delete them on
`CompletedEmpty`, failure, or orchestration-history cleanup. If orchestration history is purged but
the marker remains, a duplicate submission fails confirmation rather than recreating the load.
Inspect the original run and use a fresh ADF pipeline run for recovery; a timeout does not establish
that queued work was canceled.

Existing instances created before this guard are still returned by the starter. The guard cannot
retroactively protect old executions whose history and start evidence have already been removed.
Drain old starter invocations when deploying this change: an in-flight older starter bypasses the new
entity.

### Regression coverage

`AgentReaderStartEntityTests` uses the pinned SDK's actual entity batch dispatcher to verify one
start action, persisted deduplication, and rollback of both the marker and an already-queued start on
an operation failure. `StarterFunctionTests` forces both concurrent callers to observe no instance,
then verifies that even a completed first execution is not restarted. It also covers polling readiness,
timeouts, cancellation, enqueue failures, and retained markers after history removal.

`OrchestratorRaceSqlTests` separately bypasses the starter to exercise the SQL defenses. Its empty-run
restart test proves only that the table stays empty when the guard is bypassed; preventing that second
enumeration is the start entity's responsibility.

## Telemetry

Every entry that pertains to a run carries the run identifier. The starter's rejection entry is the sole
exception, because a rejected request has no run to trace.

| EventId | Level | Event |
|---|---|---|
| 260000 / 260001 | Debug | Function started / completed |
| 260010 | Information | Population started |
| 260011 | Information | Population counters (all counters, one entry) |
| 260012 | Error | Population failed |
| 260020 | Warning | Request rejected |
| 260021 | Information | Existing instance returned |
| 260022 | Information | Orchestration scheduled |
| 260023 | Information | Start requested through the entity |
| 260024 | Information | Duplicate entity start ignored |
| 260025 | Error | Polling instance not confirmed before the deadline |
| 260060 | Debug | Page processed |

`AgentGraphTelemetryHandler` requires a non-empty RunId on every agent Graph request, so each response
that carries a cost header is attributed to its run through the `ResourceUnitsUsedByType` event.
Responses that carry no cost header are logged but not counted.

## Running the local SQL integration tests

The SQL tests are skipped unless an explicitly named, isolated LocalDB instance is supplied, so they do
not run in CI:

```powershell
$env:AGENTREADER_SQL_TEST_SERVER = '(localdb)\GmmAgentReader-<suffix>'
dotnet test Service\GroupMembershipManagement\Hosts\AgentReader\Services.Tests
Remove-Item Env:AGENTREADER_SQL_TEST_SERVER
```

The fixture creates and drops its own database and refuses to run against any instance whose name does
not match `(localdb)\GmmAgentReader-*`.

## Deployment

`Infrastructure/compute/template.bicep` and `Infrastructure/data/template.bicep` follow the
`AzureUserReader` layout, which is the closest structural match (Graph credentials + durable task hub +
HTTP starter). Two intentional differences:

- `AzureUserReader` owns a dedicated storage account, so it takes a `storageAccountSecretName` parameter
  and publishes a `storageAccountName` app setting. `AgentReader` has no such account, so the parameter,
  the variable and the app setting are all absent, and `parameters/*.json` are empty.
- `sqlServerMSIConnectionString` is added (cloned from `SqlMembershipObtainer`), because
  `Program.cs` resolves the ADF database connection from that setting. It is **not** the
  `ConnectionStrings__JobsContext` pair, which points at the SyncJobs database.

Registration lives outside this folder:

| What | Where |
| --- | --- |
| data, compute and post-compute modules | `Deployment/computeResources.bicep` |
| Flex `maximumInstanceCount` (`agentReader: 4`) | `Deployment/computeResources.bicep` |
| VNET subnet `func-pub-agentreader`, index 19 | `Infrastructure/networking/template.bicep` |
| ADF database reader/writer grant | `Deployment/Deploy-Resources.ps1` |

The subnet catalog is **append-only**: entries carry an explicit `index` that feeds
`cidrSubnet(prefix, 26, index + 4)`, so reordering or renumbering would move existing functions onto
different subnets. `agentreader` was appended as index 19 and nothing above it was touched.

The ADF database grant adds `db_datareader` and `db_datawriter` only — no `db_ddladmin`. AgentReader
writes rows into a table the pipeline has already created, and must never issue DDL. That grant is
wrapped in an `IF NOT EXISTS (SELECT * FROM sys.database_principals ...)` guard, so if the database
user already exists the role assignments are skipped; verify effective roles after deployment rather
than assuming the script applied them.

### Key Vault secrets published

`functionApp.bicep` writes `agentReaderUrl` and `agentReaderFunctionName`; `postCompute.bicep` writes
`agentReaderKey` once the app exists. ADF needs the URL and the key to invoke the starter. On a fresh
environment ADF is deployed before this function app, so those two secret names must also be added to
`$adfDataSecrets` in `Deployment/Deploy-Resources.ps1` when the pipeline wiring lands — otherwise the
ADF template's `getSecret` call fails against a secret that does not yet exist.
