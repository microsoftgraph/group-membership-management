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
    $resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$storageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName

	$storageAccountNamePrefix = "jobs$EnvironmentAbbreviation"
	$jobStorageAccount = $storageAccounts | Where-Object { $_.StorageAccountName -like "$storageAccountNamePrefix*" }

	$tableName = "syncJobs"
	$cloudTable = (Get-AzStorageTable -Name $tableName -Context $jobStorageAccount.Context).CloudTable

    $syncJobs = Get-AzTableRow -table $cloudTable
    $sourceTableLength = $syncJobs.Length

    # Ensure that data exists in the source table
    if (0 -eq $sourceTableLength) {
        Write-Host "Skipping... Source table contains (0) records."
        Write-Host "Copy-SyncJobsToSQL completed."
        return
    } else {
        Write-Host "Source table contains ($sourceTableLength) records."
    }

    try {
        # Get SQL Connection String
        $dataKeyVaultName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
        $connectionString = Get-AzKeyVaultSecret -VaultName $dataKeyVaultName -Name "sqlDatabaseConnectionString" -AsPlainText

        # Set up connection to SQL
        $conn = New-Object System.Data.SqlClient.SQLConnection 
        $conn.ConnectionString = $connectionString

        # Check to see if the destination sql table is empty
        $syncJobsCountCmd = $conn.CreateCommand()
        $syncJobsCountCmd.CommandText = "SELECT COUNT(*) FROM [dbo].[SyncJobs]"

        $conn.Open()
        $existingCount = $syncJobsCountCmd.ExecuteScalar()
        $conn.Close()
    }
    catch {
        Write-Host "Error connecting to the jobs table in the jobs database. Do they exist?"
    }

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
}

function Get-InsertStatement {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [PSCustomObject] $SyncJob
    )
    $insertStatement = "
        INSERT INTO [dbo].[SyncJobs]
            ([DryRunTimeStamp]
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
            ('$($SyncJob.DryRunTimeStamp)'
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