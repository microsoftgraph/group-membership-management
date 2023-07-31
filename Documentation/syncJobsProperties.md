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
- ThresholdViolations
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
                "source": "<guid-group-objet-id-1>"
            },
            {
                "type": "GroupMembership",
                "source": "<guid-group-objet-id-2>"
            },
            {
                "type": "GroupMembership",
                "source": "<guid-group-objet-id-n>"
            }
        ]
- DataType: string
- Format: Guid

       

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

### ThresholdViolations
Indicates how many times the threshold has been exceeded.
It gets reset to 0 once the job syncs successfully.

### IsDryRunEnabled
Indicates if the job will run in DryRun (read-only) mode making no changes to the destination group.
### DryRunTimeStamp
Last date time the synchronization job ran in DryRun mode. Set to 1601-01-01T00:00:00.000Z for new synchronization jobs.
- DataType: DateTime
- Format: YYYY-MM-DDThh:mm:ss.zzzZ