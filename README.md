# Group Membership Management (GMM)

GMM automates Microsoft Entra ID group membership synchronization at enterprise scale. The system reads membership data from various sources (Entra ID groups, SQL databases, etc.), calculates differences, and updates target groups via Microsoft Graph API.

## Key Features

- Synchronize users from various data sources into Entra ID security groups and Microsoft Teams channels
- Process thousands of security groups with millions of memberships
- Maintain accurate group memberships through automated workflows
- Web interface for managing sync jobs and configurations

---

## 📚 Documentation

### Getting Started

| Document | Description |
|----------|-------------|
| [Deploying GMM](Deployment/Documentation/Deploying_GMM.md) | Complete deployment guide |
| [GMM Resources Overview](Documentation/Architecture/GMM_Resources.md) | All Azure resources created |
| [Setting up a Demo Tenant](Documentation/Demo%20Tenant/CreateDemoTenant.md) | Demo environment setup |
| [UI Roles](Service/GroupMembershipManagement/Hosts/WebApi/Documentation/WebApiSetup.md) | Roles as policy to gate functionality |

### Operations & Troubleshooting

| Document | Description |
|----------|-------------|
| [Finding Logs in Log Analytics](Documentation/Debugging/FindLogEntriesInLogAnalyticsForASync.md) | Query logs for sync jobs |
| [Troubleshooting with App Insights](Documentation/Debugging/TroubleshootWithApplicationInsights.md) | Debug failures and exceptions |
| [Unblock Email Issues](Service/GroupMembershipManagement/Hosts/Notifier/Documentation/UnblockEmailIssues.md) | Resolve email delivery problems |
| [Delete Environment](Documentation/Other%20Docs/DeleteEnvironment.md) | Tear down GMM |

### Email Notifications

| Document | Description |
|----------|-------------|
| [Set Sender Address](Service/GroupMembershipManagement/Hosts/Notifier/Documentation/SetSenderAddressForEmailNotification.md) | Configure email sender account |
| [Notifier Function Setup](Service/GroupMembershipManagement/Hosts/Notifier/Documentation/NotifierSetup.md) | Configure the notifier function to send actionable messages |

### Azure Data Factory (HR Data Integration)

GMM supports SQL-based membership synchronization using data from HR systems or other external sources. To help you get started, we provide a demo Azure Data Factory (ADF) setup that serves as a guide for building your own ADF pipelines and workflows to import data into GMM's SQL database.

| Document | Description |
|----------|-------------|
| [ADF Demo Setup](Infrastructure/adf/Documentation/ADF_Demo_Setup.md) | Demo ADF configuration to guide your own HR data integration |

> **Note:** ADF resources are deployed to the data resource group. Use the demo as a reference to create pipelines tailored to your organization's HR data sources.

---

## 🔗 Quick Links

- [Breaking Changes](breaking_changes.md)
- [Release Notes](release_notes.md)
- [FAQ](faq.md)
- [Features](features.md)
- [Roadmap](roadmap.md)

---

## Architecture Overview

GMM uses a microservices architecture built on Azure Functions:

```
JobScheduler → JobTrigger → MembershipObtainers → MembershipAggregator → GraphUpdater
```

1. **JobScheduler** distributes job scheduling to balance Graph API load
2. **JobTrigger** initiates sync jobs when they are due
3. **MembershipObtainers** retrieve membership from various sources (Entra ID groups, SQL, Teams channels)
4. **MembershipAggregator** calculates differences (adds/removes)
5. **GraphUpdater** applies changes to destination groups via Graph API

For a complete list of all services and resources, see the [GMM Resources Overview](Documentation/Architecture/GMM_Resources.md).

---

## Technology Stack

| Layer | Technologies |
|-------|-------------|
| **Backend** | .NET 8.0, C#, Azure Functions |
| **Frontend** | React, TypeScript, Fluent UI |
| **Database** | SQL Server, Entity Framework Core |
| **Messaging** | Azure Service Bus |
| **Authentication** | Microsoft Entra ID, Microsoft Graph API |
| **Infrastructure** | Azure (Functions, Key Vault, App Configuration), Bicep |

---

## Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

- [Code of Conduct](CODE_OF_CONDUCT.md)
- [Security Policy](SECURITY.md)
- [Support](SUPPORT.md)

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
