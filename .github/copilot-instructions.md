# GitHub Copilot Team Onboarding Guide for Group Membership Management (GMM)

Welcome to the GMM development team! This guide will help you understand our codebase, architectural patterns, and development practices so you can provide more effective assistance.

## What GMM Does

GMM automates Entra ID group membership synchronization at enterprise scale. The system:

- Synchronizes users from various data sources into Entra ID security groups and Microsoft Teams channels
- Processes thousands of security groups with millions of memberships
- Maintains accurate group memberships through automated workflows
- Provides a web interface for managing sync jobs and configurations

**Core Business Logic**: The system reads membership data from source systems (Entra ID groups, SQL databases, ect.), calculates differences (adds/removes), applies business rules and honors group owner-specified thresholds, then updates target groups via Microsoft Graph API.

## Architecture & Technology Stack

### Core Technologies
- **Backend**: .NET 8.0, C#
- **Cloud Platform**: Microsoft Azure
- **Frontend**: React with TypeScript, Fluent UI
- **Database**: SQL Server with Entity Framework Core
- **Authentication**: Azure AD, Microsoft Graph API
- **Infrastructure**: Azure Functions, Service Bus, Key Vault, Bicep ARM templates
- **Build System**: Azure DevOps YAML pipelines

### Key Components

#### Azure Functions (Microservices Architecture)
- **GraphUpdater**: Manages updates to Entra ID groups via Microsoft Graph API
- **GroupMembershipObtainer**: Retrieves membership data from various sources
- **MembershipAggregator**: Processes and aggregates membership data
- **TeamsChannelMembershipObtainer**: Handles Teams channel membership
- **TeamsChannelUpdater**: Updates Teams channel memberships
- **SqlMembershipObtainer**: Retrieves membership from SQL data sources
- **AzureMaintenance**: Performs system maintenance tasks
- **AzureUserReader**: Reads user information from Entra ID
- **MessageSplitter**: Handles message routing and distribution
- **WebApi**: RESTful API service for GMM's UI
- **DestinationAttributesUpdater**: Periodically updates our Sql cache with the latest information from Entra about owners, emails, group names.
- **GroupOwnershipObtainer**: Obtains the owners of all the destinations of sync jobs managed by GMM.
- **JobScheduler**: Periodically prioritizes and schedules out jobs across their period buckets.
- **JobTrigger**: Periodically triggers syncs to run.
- **NonProdService**: Creates groups and jobs for non prod load testing purposes.
- **Notifier**: Sends notifications to group owners to notify them of changes in their sync.
- **PlaceMembershipObtainer**: Obtains the users that are found in the result of a Graph users GET call based on and filters applied.
- **SyncJobUpdater**: Updates sync job properties in a centralized function, including tracking logs as well.
- **AutoApprover**: Processes auto-approval requests for configuration workflow.
- **SqlDataChecker**: Validates SQL data integrity by comparing recent ADF pipeline runs for table structure and row count changes.

#### Web Application
- **React SPA** located in `UI/web-app/`
- **Fluent UI components** for consistent Microsoft design
- **Redux state management**
- **TypeScript** for type safety

#### Infrastructure
- **Bicep templates** in `Deployment/` and individual service `Infrastructure/` folders
- **PowerShell scripts** in `Scripts/` for environment setup
- **Azure Resource Manager** for resource deployment

## Development Guidelines

### Code Organization
- Follow **repository pattern** for data access
- Use **dependency injection** throughout the application
- Implement **service layer abstractions** with contracts
- Apply **SOLID principles** and clean architecture patterns

### Key Patterns
- **Durable Functions** for orchestration workflows
- **Service Bus messaging** for decoupled communication
- **Entity Framework** for data persistence
- **Configuration management** via Azure App Configuration
- **Secrets management** via Azure Key Vault

### Project Structure Understanding
```
Service/GroupMembershipManagement/
├── Hosts/                          # Azure Function apps
│   ├── [ServiceName]/
│   │   ├── Function/               # Function app entry points
│   │   ├── Services/               # Business logic
│   │   ├── Services.Contracts/     # Service interfaces
│   │   └── Infrastructure/         # Bicep deployment templates
├── Repositories.*/                 # Data access layers
├── Services.*/                     # Business services
├── Models/                         # Domain models and entities
├── Common.DependencyInjection/     # DI configuration
└── Hosts.FunctionBase/             # Shared function utilities
```

### Data Models
- **AzureADUser**: Represents Azure AD user entities
- **AzureADGroup**: Represents Azure AD security groups
- **AzureADTeamsChannel**: Represents Teams channels
- **SyncJob**: Core entity for synchronization jobs
- **GroupMembership**: Membership data container

### Key Services
- **IGraphGroupRepository**: Azure AD group operations via Microsoft Graph
- **ITeamsChannelRepository**: Teams channel management
- **IDatabaseSyncJobsRepository**: Sync job data persistence
- **ILoggingRepository**: Centralized logging
- **INotificationRepository**: Email and notification services

## Development Best Practices

### Coding Standards
- Use **async/await** for all I/O operations
- Implement proper **error handling** and logging
- Follow **C# naming conventions**
- Use **dependency injection** for all service dependencies
- Write **unit tests** for business logic
- Apply **configuration-driven** development practices

### Enterprise-Scale Considerations
- Design for **high availability** and **disaster recovery**
- Implement **circuit breaker patterns** for external service calls
- Use **exponential backoff** for retry logic
- Apply **rate limiting** to protect downstream services
- Design with **zero-downtime deployments** in mind
- Implement **comprehensive monitoring** and **alerting**

### Azure Function Development
- Use **CommonStartup** base class for function initialization
- Implement **proper dependency injection** setup
- Use **ILoggingRepository** for structured logging
- Handle **retry policies** and transient failures
- Implement **dry run capabilities** where applicable
- Design functions to be **stateless** and **idempotent**

### Frontend Development (React)
- Use **TypeScript** for all components
- Follow **Fluent UI design patterns**
- Implement **Redux** for state management
- Create **reusable components**
- Use **proper error boundaries**
- Follow **accessibility guidelines**

### Infrastructure as Code
- Use **Bicep** for all Azure resource definitions
- Implement **parameterized templates**
- Follow **least privilege** security principles
- Use **managed identities** where possible
- Implement **proper resource naming** conventions

## Microsoft Graph API Integration

### Permissions Required
- **GroupMember.ReadWrite.All**: For group membership management
- **User.Read.All**: For user information access
- **ChannelMember.ReadWrite.All**: For Teams channel management (optional)

### Key Graph Operations
- Group membership CRUD operations
- User lookup and validation
- Teams channel management
- Delta queries for efficient synchronization

## Security Considerations

### Authentication & Authorization
- Use **Azure AD authentication** throughout
- Implement **role-based access control** (RBAC)
- Use **managed identities** for service-to-service communication
- Store secrets in **Azure Key Vault**

### Data Protection
- Follow **data minimization** principles
- Implement **audit logging** for all operations
- Use **encryption at rest** and **in transit**
- Apply **least privilege** access patterns

## Testing Guidelines

### Unit Testing
- Use **MSTest** framework
- Mock external dependencies using **Moq**
- Test business logic in isolation
- Use **Playwright** for UI component testing
- Ensure **test data** is representative of production scenarios
- Follow **Arrange-Act-Assert** pattern for clarity
- Achieve high code coverage

### Integration Testing
- Test Azure Function orchestrations
- Validate Microsoft Graph API interactions
- Test database operations
- Verify message bus communications

## Deployment & Operations

### Environment Management
- Use **parameterized deployments** for multiple environments
- Implement **configuration management** via Azure App Configuration
- Use **feature flags** for controlled rollouts
- Monitor with **Application Insights**

### Monitoring & Logging
- Use **structured logging** with correlation IDs
- Implement **health checks** for all services
- Set up **alerting** for critical failures
- Track **performance metrics** and **business KPIs**

## Common Tasks & Patterns

### Adding a New Data Source
1. Create repository interface in `Repositories.Contracts`
2. Implement repository in dedicated `Repositories.*` project
3. Create service layer with business logic
4. Add Azure Function host if needed
5. Update dependency injection configuration
6. Create Bicep infrastructure templates

### Adding New API Endpoints
1. Create request/response models in `WebApi.Models`
2. Implement message handlers in `Services.WebApi`
3. Add controller endpoints in `WebApi`
4. Update OpenAPI documentation
5. Add authorization policies

### Troubleshooting Common Issues
- **Graph API throttling**: Implement exponential backoff with jitter
- **Service Bus message failures**: Check dead letter queues and implement poison message handling
- **Authentication issues**: Verify managed identity permissions and token expiration
- **Database timeouts**: Check connection strings, connection pooling, and retry policies
- **Memory pressure**: Monitor function memory usage and optimize object lifecycle
- **Cold start latency**: Consider using Azure Functions Premium plan for critical workloads

### Performance Optimization
- **Batch operations** where possible to reduce API calls
- **Use caching strategies** for frequently accessed data
- **Implement connection pooling** for database connections
- **Optimize query patterns** to reduce database load
- **Use async patterns** throughout to maximize throughput
- **Monitor and profile** regularly to identify bottlenecks

## External Resources

- [Microsoft Graph API Documentation](https://docs.microsoft.com/en-us/graph/)
- [Azure Functions Documentation](https://docs.microsoft.com/en-us/azure/azure-functions/)
- [Fluent UI Documentation](https://developer.microsoft.com/en-us/fluentui)
- [Azure Service Bus Documentation](https://docs.microsoft.com/en-us/azure/service-bus-messaging/)
- [Microsoft Tech Community](https://techcommunity.microsoft.com/)

## Contributing Guidelines

This project follows Microsoft's coding standards and practices. When contributing:

1. **Follow established patterns** in the codebase
2. **Add appropriate logging** and error handling
3. **Write comprehensive tests** for new functionality
4. **Update documentation** as needed
5. **Use semantic versioning** for releases
6. **Maintain backward compatibility** where possible

## Code Review & Feedback Guidelines

When reviewing code changes or providing feedback for the Group Membership Management (GMM) system, use the following prompt to ensure comprehensive analysis:

"Review this changeset for any code patterns, logic errors, configuration changes, or architectural decisions that could introduce reliability, security, or performance risks in a production environment. Pay special attention to any logic that adds or removes users from groups, and flag anything that could unintentionally alter group memberships. Identify changes that might cause system instability, data loss, degraded user experience, or operational incidents after deployment. Where possible, implement mitigations or improvements.  Additionally, consider recommending changes to the .github/copilot-instructions.md file if you believe the changeset indicates a need for updates to ensure it remains up-to-date with the latest architectural patterns and development practices."

### Critical Review Areas for GMM

- **Group Membership Logic**: Scrutinize any code that modifies group memberships, ensuring proper validation and error handling
- **Authentication & Authorization**: Verify that changes maintain proper security boundaries and access controls
- **Data Consistency**: Check for race conditions or transaction boundaries that could lead to inconsistent membership states
- **Error Handling**: Ensure robust error handling for Microsoft Graph API calls and external service interactions
- **Configuration Changes**: Review any changes to settings that could affect sync behavior or service reliability
- **Performance Impact**: Assess changes for potential performance degradation, especially in batch operations
- **Monitoring & Alerting**: Verify that changes include appropriate logging and monitoring for production visibility

---
# Top Priority Questions for Context Enhancement

After evaluating the entire repo, Copilot was asked to generate a list of critical questions that would provide the greatest impact in understanding the GMM system's architecture, workflows, and business logic.  The following are those questions as well as answers provided by the GMM team:

1. **Service Interaction Flow**:
Q: What is the typical end-to-end flow of data through the system? For example: JobScheduler → JobTrigger → GroupMembershipObtainer → MembershipAggregator → GraphUpdater? Understanding this core workflow is essential for debugging and extending the system.
A: The typical flow follows this sequence:
* **JobScheduler** updates job run times by leveraging past runtimes to distribute Graph API load throughout the day. This primarily affects read distribution since every job requires reading at least the destination group (and often source groups). Write distribution is less predictable due to unpredictable membership changes.
* **JobTrigger** is a timer-triggered function that runs every 5 minutes, checking for due jobs. It separates due jobs into membership obtaining tasks based on their constituent source parts and destination group.
* **MembershipObtainer functions** (GroupMembershipObtainer, SqlMembershipObtainer, TeamsChannelMembershipObtainer) retrieve membership data from various sources and the destination group.
* **MembershipAggregator** combines source memberships (excluding exclusionary parts) and compares them to the destination. It identifies members to remove (in destination but not source) and members to add (in source but not destination).
* **GraphUpdater** applies the changes by removing and adding the identified members to the destination group.

2. **Message Size-Based Routing Architecture**:
Q: The MessageSplitter uses multiple instances (s1, m1, l1, o1) with size-based routing (Small, Medium, Large lanes). What performance or reliability problems does this solve? How are lane sizes and instance counts determined? This affects how we approach scalability and performance optimization.
A: The MessageSplitter is optionally used when GMM is configured for "multi-lane" processing. When enabled, it distributes work from the MembershipAggregator by routing messages to different lanes based on the number of adds/removes required to process each job. This prevents large jobs from starving smaller jobs, ensuring that quick changes can be processed promptly even when lengthy operations are running.

3. **Threshold Calculation Business Logic**: 
Q: The DeltaCalculatorService contains sophisticated threshold logic with percentage calculations and notification triggers. What business scenarios drove these specific threshold rules? How do administrators determine appropriate threshold percentages? This is critical for understanding the core safety mechanisms.
A: The thresholding logic ensures group owners are consulted when GMM detects changes larger than the group owner's specified threshold. This protects groups from inadvertent large-scale membership increases or decreases without owner awareness. The threshold is specified as a percentage of current membership, and group owners can set this percentage to whatever they deem appropriate for their group. 

4. **Multi-Source Membership Aggregation**:
Q: The MembershipSubOrchestratorFunction handles complex logic for combining multiple source groups with inclusion/exclusion patterns. What real-world scenarios require this level of complexity? How do administrators design these queries? This affects how we build and validate membership logic.
A: Group owners (not administrators) design these queries. The MembershipSubOrchestratorFunction combines multiple source groups to determine membership based on the group owner's defined criteria, allowing owners to express membership through inclusion and exclusion patterns.

5. **Azure Data Factory Integration Purpose**:
Q: The system includes extensive ADF infrastructure with pipelines and HR data integration. What business scenarios require this data factory? Is it for importing user data from HR systems? How does this integrate with real-time sync processing? Understanding this helps with data architecture decisions.
A: The ADF infrastructure is used to import user data from HR systems and other data sources into Azure SQL databases. This data is then used by GMM to synchronize group memberships. The ADF pipelines ensure that the data is up-to-date and available for real-time sync processing, allowing GMM to maintain accurate group memberships based on the latest user information.

6. **Authentication Strategy Selection**:
Q: The system uses both service principals and managed identities, and dual authentication strategies for Teams channel operations. What determines which authentication method is used for different scenarios? This is crucial for security architecture and troubleshooting.
A: We prefer managed identities for authentication, using system managed identities for GMM's internal services and user managed identities for external authentication. System managed identities are lifecycle-tied to their services, which would require re-granting access if services are deleted/recreated. User managed identities provide flexibility for external service authentication to GMM. Service principals or service accounts are used when external services don't support managed identities. For consistency in communicating with group owners who add GMM identities as group/channel owners, we're shifting from service principals to service accounts—the only option available for Teams channel operations.

7. **Dry Run vs Production Execution**:
Q: Multiple services have parallel dry run and production execution paths. What specific validation and testing scenarios do dry runs enable? How do administrators use dry run results to validate changes before production execution? This affects testing and deployment strategies.
A: The dry run functionality validates changes GMM would make to a group before applying them, allowing the development team to review and ensure changes are acceptable. Dry runs provide a preview of membership changes (additions/removals) without modifying actual group membership. This feature hasn't been used recently and may not be fully functional—it should likely be removed to prevent confusion or issues.

8. **UI User Personas and Workflows**:
Q: The React application has ManageMembership, JobDetails, OwnerPage, and AdminConfig pages. What are the typical user journeys through these pages? Who are the primary user personas (IT admins, group owners, end users)? This helps with UI development and user experience decisions.
A: The primary user personas are GMM Administrators, GMM Reviewers, and Group Owners. The typical user journey involves:
- **GMM Administrators**: Configure system settings, and monitor job execution.
- **GMM Reviewers**: Review and validate membership management onboarding and update requests, ensuring compliance with organizational policies.
- **Group Owners**: Submit requests to change their groups' membership query definitions and view job statuses.

9. **Error Recovery and Resilience Patterns**:
Q: Services implement retry logic, circuit breakers, and error state management. What are the most common failure scenarios in production? How does the system recover from partial failures or inconsistent states? This is essential for operational reliability.
A: Common failure scenarios include transient network issues, service timeouts, and API rate limiting. The system leverages Polly to implement retry logic with exponential backoff for transient errors. For API rate limiting, the system also employs sophisticated scheduling and self-governed multi-lane processing to avoid throttling (particularly Graph API). When these techniques fail, Polly provides the retry/backoff safety net.

10. **Microsoft Graph API Rate Limiting at Scale**:
Q: The system implements sophisticated retry and backoff logic for Graph API calls. What rate limiting challenges exist at enterprise scale? How does the system handle API throttling during peak usage periods? This affects performance tuning and capacity planning.
A: We leverage Polly to implement retry/backoff policies. The primary challenges involve "Resource unit quota" limits for reading from large (L) tenants and "Write quota" limits for membership changes. More details on these limits are available in the [Microsoft Graph documentation](https://learn.microsoft.com/en-us/graph/throttling-limits#pattern).

11. **Performance Characteristics at Scale**:
Q: How does the system handle groups with hundreds of thousands of members? What are the performance bottlenecks and memory considerations when processing large membership sets?
A: The system handles large groups through several optimization strategies: batching to improve write performance and caching to enhance read performance. We are currently constrained by the 1.5 GB memory limit of the serverless Azure Functions SKU. To address this, we're transitioning to Flex Consumption plans that support instances with higher memory capacity. Additionally, we're modifying data exchange patterns between Durable Functions' Orchestrator Functions and Activity Functions to reduce memory usage.

12. **Configuration Management in Practice**:
Q: How are multi-lane sizes, batch sizes, and threshold parameters tuned in production environments? What factors influence these configuration decisions?
A: Multi-lane processing is a relatively new feature for GMM. We are currently experimenting with different configurations in lower environments to determine optimal settings through empirical testing and performance analysis.

13. **Monitoring and Alerting Strategy**:
Q: What are the most critical metrics and alerts for GMM operations? How do operators identify and respond to system health issues?
A: We use an Azure dashboard to monitor system health, defined in `Infrastructure\data\dashboard.bicep`. Key metrics include Graph API Resource Unit Usage (RUU) consumption, daily job completion counts, job duration percentiles, and user addition/removal statistics. This comprehensive monitoring enables proactive identification of performance issues and system bottlenecks.