# SqlDataChecker

## Overview

SqlDataChecker is a Durable Azure Function that validates the data integrity of SQL tables used by the Group Membership Management (GMM) system. It compares the results of two recent successful Azure Data Factory pipeline runs to detect unexpected changes in table structure and row counts.

## How It Works

1. **StarterFunction** (HTTP trigger) initiates the orchestration and immediately returns the Durable Functions check-status response. The calling Azure Data Factory pipeline polls `statusQueryGetUri` until the orchestration reaches a terminal state and fails the pipeline when that state is not `Completed`, matching the pattern used by the other ADF-invoked GMM functions.
2. **OrchestratorFunction** coordinates the following activities:
   - **TableNameReader** — reads the list of table names from the SQL database
   - **ColumnReader** — reads column definitions for each table
   - **ColumnValidator** — validates that columns match between the two pipeline runs
   - **RowReader** — reads row counts for each table
   - **ThresholdReader** — reads the per-column null thresholds configured for the default SQL membership source (`dbo.SqlMembershipSources` in the GMM jobs database)
   - **DifferenceChecker** — compares row counts between the two pipeline runs and flags significant differences
   - **Logger** — logs validation results

## Configuration

The function requires the following application settings:

| Setting | Description |
|---------|-------------|
| `sqlServerBasicConnectionString` | Connection string for the ADF data database (the tables produced by the pipeline runs) |
| `ConnectionStrings__JobsContext` | Connection string for the GMM jobs database, where `dbo.SqlMembershipSources` (per-column null thresholds) lives |
| `ConnectionStrings__JobsContextReadOnly` | Read-replica connection string for the GMM jobs database |
| `dataFactoryName` | Name of the Azure Data Factory |
| `pipeline` | Name of the ADF pipeline to check |
| `subscriptionId` | Azure subscription ID |
| `dataResourceGroup` | Resource group containing the data resources |

> **Note:** the ADF data database and the GMM jobs database are separate. Table, column, and row inspection uses `sqlServerBasicConnectionString`; configuration stored in GMM tables such as `SqlMembershipSources` must be read through the Entity Framework repositories backed by `ConnectionStrings__JobsContext`. Querying GMM tables over the ADF connection fails with `Invalid object name`.

## Deployment

Infrastructure is defined in the `Infrastructure/` directory using Bicep templates:

- `compute/template.bicep` — Main deployment orchestrator
- `compute/functionApp.bicep` — Function app resource definition
- `data/template.bicep` — Data resource definitions (storage account, Key Vault secrets)
