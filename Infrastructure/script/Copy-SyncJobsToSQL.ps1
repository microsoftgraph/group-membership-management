$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Copies the data from the SyncJobs table in Table Storage to the SyncJobs table in SQL if the destination table contains no records.

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (or application). Length 1-3.

.PARAMETER EnvironmentAbbreviation
Your Environment Abbreviation

.EXAMPLE
Copy-SyncJobsToSQL  -SolutionAbbreviation "<SolutionAbbreviation>" `
                    -EnvironmentAbbreviation "<EnvironmentAbbreviation>"
#>
function Copy-SyncJobsToSQL {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $false)]
        [bool] $Overwrite
    )

    . ($PSScriptRoot + '\Install-AzTableModuleIfNeeded.ps1')
    Install-AzTableModuleIfNeeded | Out-Null

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
    }
    else {
        Write-Host "Source table contains ($sourceTableLength) records."
    }

    try {
        # Get SQL Connection String
        $dataKeyVaultName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
        $connectionString = Get-AzKeyVaultSecret -VaultName $dataKeyVaultName -Name "sqlDatabaseConnectionString" -AsPlainText
        $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
        $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate($context.Account, $context.Environment, $context.Tenant.Id.ToString(), $null, [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never, $null, "https://database.windows.net").AccessToken

        # Set up connection to SQL
        $conn = New-Object System.Data.SqlClient.SQLConnection
        $conn.ConnectionString = $connectionString
        $conn.AccessToken = $sqlToken

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
        }
        else {
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
        $dryRunTimeStampParam = $sqlCommand.Parameters.Add("@DryRunTimeStamp", [System.Data.SqlDbType]::DateTime)
        $dryRunTimeStampParam.Value = (Get-SqlDate -dateTime $syncJob.DryRunTimeStamp)
        $ignoreThresholdOnceParam = $sqlCommand.Parameters.Add("@IgnoreThresholdOnce", [System.Data.SqlDbType]::Bit)
        $ignoreThresholdOnceParam.Value = $([int][bool]::Parse($SyncJob.IgnoreThresholdOnce -eq "True"))
        $isDryRunEnabledParam = $sqlCommand.Parameters.Add("@IsDryRunEnabled", [System.Data.SqlDbType]::Bit)
        $isDryRunEnabledParam.Value = $([int][bool]::Parse($SyncJob.IsDryRunEnabled -eq "True"))
        $allowEmptyDestinationParam = $sqlCommand.Parameters.Add("@AllowEmptyDestination", [System.Data.SqlDbType]::Bit)
        $allowEmptyDestinationParam.Value = $([int][bool]::Parse($SyncJob.AllowEmptyDestination -eq "True"))
        $lastRunTimeParam = $sqlCommand.Parameters.Add("@LastRunTime", [System.Data.SqlDbType]::DateTime)
        $lastRunTimeParam.Value = (Get-SqlDate -dateTime $syncJob.LastRunTime)
        $lastSuccessfulRunTimeParam = $sqlCommand.Parameters.Add("@LastSuccessfulRunTime", [System.Data.SqlDbType]::DateTime)
        $lastSuccessfulRunTimeParam.Value = (Get-SqlDate -dateTime $syncJob.LastSuccessfulRunTime)
        $lastSuccessfulStartTimeParam = $sqlCommand.Parameters.Add("@LastSuccessfulStartTime", [System.Data.SqlDbType]::DateTime)
        $lastSuccessfulStartTimeParam.Value = (Get-SqlDate -dateTime $syncJob.LastSuccessfulStartTime)
        $periodParam = $sqlCommand.Parameters.Add("@Period", [System.Data.SqlDbType]::Int)
        $periodParam.Value = $syncJob.Period
        $queryParam = $sqlCommand.Parameters.Add("@Query", [System.Data.SqlDbType]::NVarChar)
        $queryParam.Value = $syncJob.Query
        $requestorParam = $sqlCommand.Parameters.Add("@Requestor", [System.Data.SqlDbType]::NVarChar)
        $requestorParam.Value = $syncJob.Requestor
        $runIdParam = $sqlCommand.Parameters.Add("@RunId", [System.Data.SqlDbType]::UniqueIdentifier)
        $runIdParam.Value = $syncJob.RunId
        $startDateParam = $sqlCommand.Parameters.Add("@StartDate", [System.Data.SqlDbType]::DateTime)
        $startDateParam.Value = (Get-SqlDate -dateTime $syncJob.StartDate)
        $statusParam = $sqlCommand.Parameters.Add("@Status", [System.Data.SqlDbType]::NVarChar)
        $statusParam.Value = $syncJob.Status
        $targetOfficeGroupIdParam = $sqlCommand.Parameters.Add("@TargetOfficeGroupId", [System.Data.SqlDbType]::UniqueIdentifier)
        $targetOfficeGroupIdParam.Value = $syncJob.TargetOfficeGroupId
        $thresholdPercentageForAdditionsParam = $sqlCommand.Parameters.Add("@ThresholdPercentageForAdditions", [System.Data.SqlDbType]::Int)
        $thresholdPercentageForAdditionsParam.Value = $syncJob.ThresholdPercentageForAdditions
        $thresholdPercentageForRemovalsParam = $sqlCommand.Parameters.Add("@ThresholdPercentageForRemovals", [System.Data.SqlDbType]::Int)
        $thresholdPercentageForRemovalsParam.Value = $syncJob.ThresholdPercentageForRemovals
        $thresholdViolationsParam = $sqlCommand.Parameters.Add("@ThresholdViolations", [System.Data.SqlDbType]::Int)
        $thresholdViolationsParam.Value = $syncJob.ThresholdViolations
        $destinationParam = $sqlCommand.Parameters.Add("@Destination", [System.Data.SqlDbType]::NVarChar)
        $destinationParam.Value = $syncJob.Destination
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

function Get-SqlDate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [datetime] $dateTime
    )
    # minimum sql date
    $sqlMinDate = [System.Data.SqlTypes.SqlDateTime]::MinValue.Value

    if ($null -eq $dateTime -or $dateTime -lt $sqlMinDate) {
        return $sqlMinDate
    }

    return $dateTime

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
            ,[AllowEmptyDestination]
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
            ,[ThresholdViolations]
            ,[Destination])
        VALUES
            (
             @DryRunTimeStamp
            ,@IgnoreThresholdOnce
            ,@IsDryRunEnabled
            ,@AllowEmptyDestination
            ,@LastRunTime
            ,@LastSuccessfulRunTime
            ,@LastSuccessfulStartTime
            ,@Period
            ,@Query
            ,@Requestor
            ,@RunId
            ,@StartDate
            ,@Status
            ,@TargetOfficeGroupId
            ,@ThresholdPercentageForAdditions
            ,@ThresholdPercentageForRemovals
            ,@ThresholdViolations
            ,@Destination
           )
    "

    return $insertStatement
}