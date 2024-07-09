$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Migrate the storage table into a sql table

.DESCRIPTION
Long description

.PARAMETER connectionString
connectionString
-notificationsCsvPath

.EXAMPLE
Set-MigrateStorageTable	-connectionString "<connectionString>"  `
                        -notificationsCsvPath "<notificationsCsvPath>" `
						-Verbose
#>

function Set-MigrateStorageTable {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $connectionString,
        [Parameter(Mandatory=$True)]
        [string] $notificationsCsvPath
    )

    Write-Host "Start Set-MigrateStorageTable"

    # Connect to the SQL Server instance
    $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
    $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate($context.Account, $context.Environment, $context.Tenant.Id.ToString(), $null, [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never, $null, "https://database.windows.net").AccessToken
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $connectionString
    $connection.AccessToken = $sqlToken
    $connection.Open()

    # Read the notifications CSV file
    $notifications = Import-Csv -Path $notificationsCsvPath

    # Function to convert timestamp format if needed
    function Convert-Timestamp {
        param (
            [string]$timestamp
        )
        return [DateTimeOffset]::Parse($timestamp).ToString('yyyy-MM-dd HH:mm:ss')
    }
 
    # Iterate over the CSV rows and insert into SQL table
    foreach ($row in $notifications) {
        $lastUpdateTime = Convert-Timestamp -timestamp $row.Timestamp
        $query = @"
        INSERT INTO ThresholdNotifications (
            Id,
            TargetOfficeGroupId,
            SyncJobId,
            StatusName,
            ThresholdPercentageForAdditions,
            ThresholdPercentageForRemovals,
            ChangePercentageForAdditions,
            ChangePercentageForRemovals,
            ChangeQuantityForAdditions,
            ChangeQuantityForRemovals,
            CreatedTime,
            ResolvedTime,
            ResolvedBy,
            LastUpdatedTime,
            ResolutionName,
            CardStateName
        )
        VALUES (
            '$($row.Id)',
            '$($row.TargetOfficeGroupId)',
            '$($row.SyncJobId)',
            '$($row.StatusName)',
            $($row.ThresholdPercentageForAdditions),
            $($row.ThresholdPercentageForRemovals),
            $($row.ChangePercentageForAdditions),
            $($row.ChangePercentageForRemovals),
            $($row.ChangeQuantityForAdditions),
            $($row.ChangeQuantityForRemovals),
            '$($row.CreatedTime)',
            '$($row.ResolvedTime)',
            '$($row.ResolvedBy)',
            '$lastUpdateTime',
            '$($row.ResolutionName)',
            '$($row.CardStateName)'
        )
"@

        $command = $connection.CreateCommand()
        $command.CommandText = $query
        $command.ExecuteNonQuery()
    }
    $connection.Close()
    Write-Output "Data migration completed successfully."
}
