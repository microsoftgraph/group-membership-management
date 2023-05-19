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
    "SourceStorageSetting":
    {
        "TargetName": "<table-name>",
        "StorageConnectionString": "<connection-string>",
        "StorageType": "Table"
    },
    "DestinationStorageSetting":
    {
        "TargetName": "<table-name>",
        "StorageConnectionString": "<connection-string>",
        "StorageType": "Table"
    },
    "Backup": true,
    "Cleanup": true,
    "DeleteAfterDays": 30
}
```
* SourceStorageSetting: This property has information about the source table.
* DestinationStorageSetting: This property has information about the destination table.

* TargetName: The name of the source table, for example "syncJobs"
    * Note: If running where CleanupOnly is true, then this can also be "*"
to indicate the desire to clean all tables in the storage account
* StorageConnectionString: Connection string for the source storage account to backup / cleanup from
* StorageType: The type of backup desired from the following two options:
    * "Blob"    | backup / cleanup blob storage
    * "Table"   | backup / cleanup table storage
* Backup: If set to true, a full backup will be performed on the indicated tables.
* Cleanup: Set this to true if you only want to clean old tables for maintenance.
* DeleteAfterDays: The number of days a table exists before it should be deleted

## Running the function
1. Update the json for the AzureMaintenance config in the "maintenanceJobs" secret in the gmm-data-\<env> keyvault
2. Run the function in Azure