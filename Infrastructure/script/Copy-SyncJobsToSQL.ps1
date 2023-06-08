$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Copies the data from the SyncJobs table in Table Storage to the SyncJobs table in SQL if the destination table contains no records.

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (or application). Length 1-3.

.PARAMETER EnvironmentAbbreviation
Your Environment Abbreviation

.EXAMPLE
Copy-SyncJobsToSQL  -SubscriptionId "<SubscriptionId>" `
                    -SolutionAbbreviation "<SolutionAbbreviation>" `
                    -EnvironmentAbbreviation "<EnvironmentAbbreviation>"
#>
function Copy-SyncJobsToSQL {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory=$true)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory=$false)]
        [bool] $Overwrite
    )

    Write-Host "Copy-SyncJobsToSQL starting..."
    
    $infrastructureDirectory = Split-Path $PSScriptRoot -Parent
    $rootDirectory = Split-Path $infrastructureDirectory -Parent
    . ($rootDirectory + '\Scripts\Install-AzTableModuleIfNeeded.ps1')
    Install-AzTableModuleIfNeeded | Out-Null

    $resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $storageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName
    $storageAccounts | ft

    $storageAccountNamePrefix = "jobs$EnvironmentAbbreviation".ToLower()
    $jobStorageAccount = $storageAccounts | Where-Object { $_.StorageAccountName -like "$storageAccountNamePrefix*" }

    if (!$jobStorageAccount) {
        Write-Host "Skipping... Could not find storage account starting with '$storageAccountNamePrefix' in resource group '$resourceGroupName'."
        Write-Host "Copy-SyncJobsToSQL completed."
        return
    }

    # Create a reference to the Storage Account
    Write-Host ">>> Getting storage table..."
    $storageTable = Get-AzStorageTable –Name "SyncJobs" –Context $jobStorageAccount.Context  -ErrorAction SilentlyContinue
    Write-Host ">>> Getting SyncJobs..."
    $syncJobs = Get-AzTableRow -table $storageTable.CloudTable
    $sourceTableLength = $syncJobs.Length

    # Ensure that data exists in the source table
    if (0 -eq $syncJobs.Length) {
        Write-Host "Skipping... Source table contains (0) records."
        Write-Host "Copy-SyncJobsToSQL completed."
        return
    } else {
        Write-Host "Source table contains ($sourceTableLength) records."
    }

    $subscriptionId = (Get-AzContext).Subscription.Id
    Write-Host ">>> Subscription Id: $subscriptionId"

    $databaseName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $sqlServerName = "$databaseName.database.windows.net"

    Write-Host ">>> Creating SQL Connection"
    # Set up connection to SQL
    $conn = New-Object System.Data.SqlClient.SQLConnection 
    $conn.ConnectionString = "Server=$sqlServerName;Initial Catalog=$databaseName;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30"
    Write-Host ">>> Getting access token"
    $conn.AccessToken = $(az account get-access-token --subscription $subscriptionId --resource https://database.windows.net --query accessToken -o tsv)

    Write-Host ">>> Creating SQL Command"
    # Check to see if the destination sql table is empty
    $syncJobsCountCmd = $conn.CreateCommand()
    $syncJobsCountCmd.CommandText = "SELECT COUNT(*) FROM [dbo].[SyncJobs]"

    Write-Host ">>> Opening Connection..."
    $conn.Open()
    Write-Host ">>> Running command"
    $existingCount = $syncJobsCountCmd.ExecuteScalar()
    Write-Host ">>> Closing connection"
    $conn.Close()

    Write-Host ">>> SQL Record count: $existingCount"
    if (0 -ne $existingCount) {
        if ($true -ne $Overwrite) {
            Write-Host "Skipping... Destination table contains ($existingCount) records and `$Overwrite is set to `$false."
            Write-Host "Copy-SyncJobsToSQL completed."
            return
        } else {
            Write-Host "Destination table contains ($existingCount) records that will be deleted."
            $deleteSyncJobsCmd = $conn.CreateCommand()
            $deleteSyncJobsCmd.CommandText = "DELETE FROM [dbo].[SyncJobs]"
            $conn.Open()
            $deleteCount = $deleteSyncJobsCmd.ExecuteNonQuery()
            $conn.Close()
            Write-Host "Deleted ($deleteCount) records."
        }
    }

    # Copy Records
    $conn.Open()
    Write-Host "Copying ($sourceTableLength) records."
    foreach ($syncJob in $syncJobs) {
        $sqlCommand = $conn.CreateCommand()
        $sqlCommand.CommandText = (Get-InsertStatement -SyncJob $syncJob)
        $result = $sqlCommand.ExecuteNonQuery()

        if (1 -eq $result) {
            Write-Host "$($syncJob.RowKey) OK"
        }
        else {
            Write-Host "$($syncJob.RowKey) $result"
        }
    }
    $conn.Close()

    Write-Host "Copy-SyncJobsToSQL completed."
}

function Get-InsertStatement {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [PSCustomObject] $SyncJob
    )

    $insertStatement = "
        INSERT INTO [dbo].[SyncJobs]
            ([Id]
            ,[DryRunTimeStamp]
            ,[IgnoreThresholdOnce]
            ,[IsDryRunEnabled]
            ,[LastRunTime]
            ,[LastSuccessfulRunTime]
            ,[LastSuccessfulStartTime]
            ,[Period]
            ,[Query]
            ,[Requestor]
            ,[RunId]
            ,[StartDate]
            ,[Status]
            ,[TargetOfficeGroupId]
            ,[ThresholdPercentageForAdditions]
            ,[ThresholdPercentageForRemovals]
            ,[ThresholdViolations])
        VALUES
            ('$($SyncJob.RowKey)'
            ,'$($SyncJob.DryRunTimeStamp)'
            ,$([int][bool]::Parse($SyncJob.IgnoreThresholdOnce -eq "True"))
            ,$([int][bool]::Parse($SyncJob.IsDryRunEnabled -eq "True"))
            ,'$($SyncJob.LastRunTime)'
            ,'$($SyncJob.LastSuccessfulRunTime)'
            ,'$($SyncJob.LastSuccessfulStartTime)'
            ,$($SyncJob.Period)
            ,'$($SyncJob.Query)'
            ,'$($SyncJob.Requestor)'
            ,'$($SyncJob.RunId)'
            ,'$($SyncJob.StartDate)'
            ,'$($SyncJob.Status)'
            ,'$($SyncJob.TargetOfficeGroupId)'
            ,$($SyncJob.ThresholdPercentageForAdditions)
            ,$($SyncJob.ThresholdPercentageForRemovals)
            ,$($SyncJob.ThresholdViolations))
    "

    return $insertStatement
}