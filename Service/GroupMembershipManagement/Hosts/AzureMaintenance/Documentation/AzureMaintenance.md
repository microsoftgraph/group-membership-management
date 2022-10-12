# Azure Maintenance Function App
This guide will explain how to use the AzureMaintenance function, found in AzureMaintenance/Function

## Prerequisites
* Visual Studio (tested in VS 2019, but likely works with other versions as well)
* Download the latest version of the public Github repository from: https://github.com/microsoftgraph/group-membership-management
* Ensure that GMM is already set up in your environment, as you will need some of the values from your gmm-data-\<env> keyvault

## AzureMaintenance Configuration
Format:
```
{
    SourceTableName: string
    SourceConnectionString: string
    DestinationConnectionString: string
    BackupType: string
    CleanupOnly: boolean
    DeleteAfterDays: int
}
```

* SourceTableName: The name of the source table, for example "syncJobs"
    * Note: If running where CleanupOnly is true, then this can also be "*"
to indicate the desire to clean all tables in the storage account
* SourceConnectionString: Connection string for the source storage account to backup / cleanup from
* DestinationConnectionString: Connection string for the destination storage account to backup to / cleanup
* BackupType: The type of backup desired from the following two options:
    * "blob"    | backup / cleanup blob storage
    * "table"   | backup / cleanup table storage
* CleanupOnly: Whether or not you only want to clean up
    * The default value is false, which means that a full backup and cleanup will be performed on the indicated tables
    * Set this to true if you only want to clean old tables for maintenance
* DeleteAfterDays: The number of days a table exists before it should be deleted

## Running the function
1. Update the json for the AzureMaintenance config in the "tablesToBackup" secret in the gmm-data-\<env> keyvault
2. Run the function in Azure