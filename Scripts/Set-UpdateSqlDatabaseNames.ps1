$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Creates or updates Destination column

.DESCRIPTION
Long description

.PARAMETER SubscriptionName
Subscription name

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.EXAMPLE
Set-UpdateSqlDatabaseNames	-SubscriptionName "<subscriptionName>"  `
                            -SolutionAbbreviation "<solution>" `
                            -EnvironmentAbbreviation "<environment>" `
                            -Verbose
#>
function Set-UpdateSqlDatabaseNames {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $True)]
        [string] $SubscriptionName,
        [Parameter(Mandatory = $True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $True)]
        [string] $EnvironmentAbbreviation
    )

    Write-Host "Start Set-UpdateSqlDatabaseNames"

    Set-AzContext -SubscriptionName $SubscriptionName
    $resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $sqlServerName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $sqlReplicaServerName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-r"

    $adfOldDatabaseName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $jobsOldDatabaseName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-jobs"
    $jobsOldReplicaDatabaseName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-jobs-R"
    try {

        $adfOldDatabase = Get-AzSqlDatabase -ResourceGroupName $resourceGroupName `
                                            -ServerName $sqlServerName `
                                            -DatabaseName $adfOldDatabaseName

        Write-Host $adfOldDatabase.DatabaseName

        $jobsOldDatabase = Get-AzSqlDatabase -ResourceGroupName $resourceGroupName `
                                            -ServerName $sqlServerName `
                                            -DatabaseName $jobsOldDatabaseName

        Write-Host $jobsOldDatabase.DatabaseName

    }
    catch {
        Write-Host "An old database could not be found. This environment must already be migrated. Terminating script."
        return
    }

    # Remove the authorization locks on the databases so they can be moved
    Write-Host "Getting authorization locks for this environment."
    $authorizationLocks = (Get-AzResourceLock -ResourceGroupName $resourceGroupName) | Where-Object { $_.ResourceType -eq "Microsoft.Sql/servers/databases" }

    if($authorizationLocks) {
        Write-Host "Removing old Sql authorization locks removed from this environment..."

        $authorizationLocks | ForEach-Object {
            Remove-AzResourceLock -LockId $_.LockId -Force
        }

        Write-Host "Sql authorization locks removed from this environment."
    }
    else {
        Write-Host "There are no Sql authorization locks on this environment, proceeding with migration."
    }

    try {
        # Get the old replica database if it exists.
        Write-Host "Checking if old replica database still exists..."
        $jobsOldReplicaDatabase = Get-AzSqlDatabase -ResourceGroupName $resourceGroupName `
                                                    -ServerName $sqlReplicaServerName `
                                                    -DatabaseName $jobsOldReplicaDatabaseName

        Write-Host "Found the old replica database!"
    }
    catch {
        Write-Host "Replica database doesn't exist. Proceeding with migration."
    }

    # Delete old replica database and its link if it exists
    if($jobsOldReplicaDatabase) {
        Write-Host "Getting replication link for old database from Sql server."
        $oldReplicationLink = $jobsOldReplicaDatabase | Get-AzSqlDatabaseReplicationLink -PartnerResourceGroupName $resourceGroupName -PartnerServerName $sqlServerName

        if ($oldReplicationLink) {
            Write-Host "Removing old replication link..."

            $oldReplicationLink | Remove-AzSqlDatabaseSecondary

            Write-Host "Old replication link removed."
        }
        else {
            Write-Host "No replication link exists, proceeding with migration"
        }

        Write-Host "Deleting the old replica database now..."

        Remove-AzSqlDatabase -ResourceGroupName $resourceGroupName -ServerName $sqlReplicaServerName -DatabaseName $jobsOldReplicaDatabaseName

        Write-Host "Deleted the old replica database."
    }

    try {
        # Update the database names
        Write-Host "Updating the database names"

        $adfNewDatabaseName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
        Set-AzSqlDatabase -ResourceGroupName $resourceGroupName `
                            -ServerName $sqlServerName `
                            -DatabaseName $adfOldDatabaseName `
                            -NewName $adfNewDatabaseName

        Write-Host $adfOldDatabase.DatabaseName

        $jobsNewDatabaseName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
        Set-AzSqlDatabase -ResourceGroupName $resourceGroupName `
                            -ServerName $sqlServerName `
                            -DatabaseName $jobsOldDatabaseName `
                            -NewName $jobsNewDatabaseName

        Write-Host "Updated the database names"
    }
    catch {
        Write-Host "Error updating the database names. Please check that the authorization locks and replication links have been removed accordingly."
        Write-Host "Error: $_"
        throw
    }

    Write-Host "Finish Set-UpdateSqlDatabaseNames"
}