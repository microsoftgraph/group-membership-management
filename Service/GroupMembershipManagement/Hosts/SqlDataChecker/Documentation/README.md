# SqlDataChecker

## Overview

SqlDataChecker is a Durable Azure Function that validates the data integrity of SQL tables used by the Group Membership Management (GMM) system. It compares the results of two recent successful Azure Data Factory pipeline runs to detect unexpected changes in table structure and row counts.

## How It Works

1. **StarterFunction** (HTTP trigger) initiates the orchestration
2. **OrchestratorFunction** coordinates the following activities:
   - **TableNameReader** — reads the list of table names from the SQL database
   - **ColumnReader** — reads column definitions for each table
   - **ColumnValidator** — validates that columns match between the two pipeline runs
   - **RowReader** — reads row counts for each table
   - **DifferenceChecker** — compares row counts between the two pipeline runs and flags significant differences
   - **Logger** — logs validation results

## Configuration

The function requires the following application settings:

| Setting | Description |
|---------|-------------|
| `sqlServerBasicConnectionString` | Connection string for the SQL database |
| `dataFactoryName` | Name of the Azure Data Factory |
| `pipeline` | Name of the ADF pipeline to check |
| `subscriptionId` | Azure subscription ID |
| `dataResourceGroup` | Resource group containing the data resources |

## Deployment

Infrastructure is defined in the `Infrastructure/` directory using Bicep templates:

- `compute/template.bicep` — Main deployment orchestrator
- `compute/functionApp.bicep` — Function app resource definition
- `data/template.bicep` — Data resource definitions (storage account, Key Vault secrets)
