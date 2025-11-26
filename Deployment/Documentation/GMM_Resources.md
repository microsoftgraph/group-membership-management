# GMM Resource Overview

This document provides a high-level overview of all Azure resources deployed by Group Membership Management (GMM).

---

## Resource Groups

GMM deploys resources across three resource groups, each serving a specific purpose:

| Resource Group | Naming Convention | Purpose |
|----------------|-------------------|---------|
| **Prereqs** | `{solutionAbbreviation}-prereqs-{environmentAbbreviation}` | Stores prerequisites like secrets and credentials |
| **Data** | `{solutionAbbreviation}-data-{environmentAbbreviation}` | Core data infrastructure (SQL, Service Bus, Storage) |
| **Compute** | `{solutionAbbreviation}-compute-{environmentAbbreviation}` | Azure Functions, Web API, and UI |

---

## App Registrations

GMM creates four app registrations in Microsoft Entra ID for authentication and authorization:

| App Registration | Naming Convention | Purpose |
|-----------------|-------------------|---------|
| **UI** | `{solutionAbbreviation}-ui-{environmentAbbreviation}` | Authentication for the React SPA |
| **WebAPI** | `{solutionAbbreviation}-webapi-{environmentAbbreviation}` | Authentication for the backend API |
| **Graph** | `{solutionAbbreviation}-Graph-{environmentAbbreviation}` | Microsoft Graph API access for membership operations |
| **TeamsChannel** | `{solutionAbbreviation}-TeamsChannel-{environmentAbbreviation}` | Teams channel membership management |

---

## Function Apps

GMM uses Azure Functions for its microservices architecture. Functions are organized by their role in the sync workflow.

> ℹ️ **Note:** Each Function App has its own dedicated storage account for runtime operations.

### Scheduling

| Function App | Purpose |
|--------------|---------|
| **JobScheduler** | Distributes job scheduling across time buckets to balance Graph API load |
| **JobTrigger** | Timer-triggered function that initiates sync jobs when they are due |

### Membership Obtainers

| Function App | Purpose |
|--------------|---------|
| **GroupMembershipObtainer** | Retrieves membership data from Entra ID security groups |
| **SqlMembershipObtainer** | Retrieves membership data from SQL data sources |
| **TeamsChannelMembershipObtainer** | Retrieves membership data from Teams channels |
| **PlaceMembershipObtainer** | Retrieves places via Graph API filters |

### Processing

| Function App | Purpose |
|--------------|---------|
| **MembershipAggregator** | Combines source memberships and calculates differences (adds/removes) |
| **MessageSplitter** | Routes messages to different processing lanes based on job size |

### Updaters

| Function App | Purpose |
|--------------|---------|
| **GraphUpdater** | Applies membership changes to Entra ID groups via Graph API |
| **TeamsChannelUpdater** | Applies membership changes to Teams channels |

### Support Services

| Function App | Purpose |
|--------------|---------|
| **Notifier** | Sends email notifications to group owners |
| **SyncJobUpdater** | Centralized function for updating sync job properties and tracking logs |
| **DestinationAttributesUpdater** | Periodically updates SQL cache with destination group information |
| **GroupOwnershipObtainer** | Obtains owners of destination groups managed by GMM |
| **AzureUserReader** | Reads user information from Entra ID |
| **NonProdService** | Creates groups and jobs for non-production load testing |

---

## Web Applications

| Resource | Naming Convention | Purpose |
|----------|-------------------|---------|
| **WebAPI** | `{solutionAbbreviation}-compute-{environmentAbbreviation}-webapi` | RESTful API for the GMM UI and operations |
| **Static Web App** | `{solutionAbbreviation}-ui` | React SPA for managing sync jobs |
| **SignalR Service** | `{solutionAbbreviation}-compute-{environmentAbbreviation}-signalr` | Real-time communication for the UI |

---

## SQL Server & Databases

| Resource | Naming Convention | Purpose |
|----------|-------------------|---------|
| **SQL Server** | `{solutionAbbreviation}-data-{environmentAbbreviation}` | Primary SQL Server |
| **SQL Server (Replica)** | `{solutionAbbreviation}-data-{environmentAbbreviation}-replica` | Read-only replica for load distribution |
| **Jobs Database** | `{solutionAbbreviation}-data-{environmentAbbreviation}` | Stores sync job configurations and state |
| **ADF Database** | `{solutionAbbreviation}-data-{environmentAbbreviation}-adf` | Stores HR data for SQL membership sources |

---

## Service Bus

GMM uses Azure Service Bus for decoupled inter-service communication.

| Resource | Naming Convention | Purpose |
|----------|-------------------|---------|
| **Namespace** | `{solutionAbbreviation}-data-{environmentAbbreviation}` | Service Bus namespace containing topics and queues |

### Topics

| Topic | Purpose |
|-------|---------|
| `syncJobs` | Job execution messages |
| `membershipUpdaters` | Membership update routing |

### Queues

| Queue | Purpose |
|-------|---------|
| `membershipAggregator` | Aggregation job messages |
| `notifications` | Notification delivery |
| `syncJobUpdater` | Job status updates |

---

## Storage Accounts

| Resource | Purpose |
|----------|---------|
| **ADF Storage Account** | Used for the ADF demo only |
| **Jobs Storage Account** | Stores membership files for each job |
| **Function App Storage Accounts** | Each function app has a dedicated storage account for runtime operations |

---

## Key Vaults

| Key Vault | Naming Convention | Purpose |
|-----------|-------------------|---------|
| **Prereqs Key Vault** | `{solutionAbbreviation}-prereqs-{environmentAbbreviation}` | App registration secrets, sender credentials, Teams service account |
| **Data Key Vault** | `{solutionAbbreviation}-data-{environmentAbbreviation}` | Connection strings, storage keys, service bus secrets, managed identity info |

---

## Monitoring

| Resource | Naming Convention | Purpose |
|----------|-------------------|---------|
| **Application Insights** | `{solutionAbbreviation}-data-{environmentAbbreviation}` | Application performance monitoring |
| **Log Analytics Workspace** | `{solutionAbbreviation}-data-{environmentAbbreviation}` | Centralized log collection |
| **Dashboard** | `GMM Dashboard ({environmentAbbreviation})` | Operations dashboard for monitoring system health |

---

## Additional Resources

| Resource | Naming Convention | Purpose |
|----------|-------------------|---------|
| **App Configuration** | `{solutionAbbreviation}-appConfig-{environmentAbbreviation}` | Centralized configuration and feature flags |
| **User Assigned Managed Identity** | `{solutionAbbreviation}-identity-{environmentAbbreviation}-Graph` | Managed identity for Graph API access |
