A synchronization job must have the following properties populated:

- PartitionKey
- RowKey
- Requestor
- TargetOfficeGroupId
- Status
- LastRunTime
- LastSuccessfulRunTime 
- Period
- Query
- StartDate
- ThresholdPercentageForAdditions
- ThresholdPercentageForRemovals
- IsDryRunEnabled
- DryRunTimeStamp

### PartitionKey
Partition key, the value added here represents the date the job was added to the table.
- DataType: string
- Format: YYYY-MM-DD

### RowKey
Unique key of the synchronization job.
- DataType: string
- Format: Guid

### Requestor
Email address of the person who requested the synchronization job.
- DataType: string
- Format: Email address

### TargetOfficeGroupId
Azure Object Id of destination group.
- DataType: Guid

### Status
Current synchronization job status; Set to Idle for new synchronization jobs.
- DataType: string
- Valid values: Idle, InProgress, Error

### LastRunTime
Last date time the synchronization job ran. Set to 1601-01-01T00:00:00.000Z for new synchronization jobs.
- DataType: DateTime
- Format: YYYY-MM-DDThh:mm:ss.zzzZ

### LastSuccessfulRunTime
Last date time the synchronization job ran successfully. Set to 1601-01-01T00:00:00.000Z for new synchronization jobs.
- DataType: DateTime
- Format: YYYY-MM-DDThh:mm:ss.zzzZ

### Period
Defines in hours, how often a synchronization job will run.
- DataType: int

### Query
Defines the type of sync and the Azure ObjectId of the security group that will be used as the source for the synchronization. One or multiple objects can be provided. For example:

        [
            {
                "type": "GroupMembership",
                "source": "<guid-group-object-id-1>",
                "identityTypes": ["users"]
            },
            {
                "type": "GroupMembership",
                "source": "<guid-group-object-id-2>",
                "identityTypes": ["agents"]
            },
            {
                "type": "GroupMembership",
                "source": "<guid-group-object-id-n>",
                "identityTypes": ["users", "agents"]
            }
        ]
- DataType: string
- Format: Serialized JSON array of query-part objects; each `GroupMembership` `source` is a Guid

For a `GroupMembership` part, `identityTypes` is optional and is available through the advanced JSON query:

- `users` selects people.
- `agents` selects agent users. It does not select agent identities or other service principals.
- `["users","agents"]` and `["agents","users"]` both select people and agent users.
- Omitting `identityTypes` also selects people and agent users, preserving existing group-source behavior.
- A present list must be nonempty, contain each supported lower-case value at most once, and can be used only when `source` is one group identifier.

The omitted default differs by source type: a `GroupMembership` part defaults to users and agents, while a `SqlMembership` part defaults to users only. `GroupOwnership`, `PlaceMembership`, and `TeamsChannelMembership` parts do not support `identityTypes`.

### StartDate
Defines the date and time when the synchronization job should start running, this allows to schedule jobs to run in the future.
i.e. 2021-01-01T00:00:00.000Z
- DataType: DateTime
- Format: YYYY-MM-DDThh:mm:ss.zzzZ

### ThresholdPercentageForAdditions
Threshold percentage for users being added.
If the threshold is exceeded GMM is not going to make any changes to the destination group and an email notification will be sent describing the issue.
The email notification will be sent to the recipients defined in the 'SyncDisabledEmailBody' setting located in the prereqs keyvault. Multiple email addresses can be specified separated by semicolon.
To continue processing the job increase the threshold value or disable the threshold check by setting it to 0 (zero).
- DataType: int

### ThresholdPercentageForRemovals
Threshold percentage for users being removed.
If the threshold is exceeded GMM is not going to make any changes to the destination group and an email notification will be sent describing the issue.
The email notification will be sent to the recipients defined in the 'SyncDisabledEmailBody' setting located in the prereqs keyvault. Multiple email addresses can be specified separated by semicolon.
To continue processing the job increase the threshold value or disable the threshold check by setting it to 0 (zero).
- DataType: int

### IsDryRunEnabled
Indicates if the job will run in DryRun (read-only) mode making no changes to the destination group.
### DryRunTimeStamp
Last date time the synchronization job ran in DryRun mode. Set to 1601-01-01T00:00:00.000Z for new synchronization jobs.
- DataType: DateTime
- Format: YYYY-MM-DDThh:mm:ss.zzzZ