$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Creates or updates Destination column

.DESCRIPTION
Long description

.PARAMETER ConnectionString
ConnectionString

.EXAMPLE
Set-UpdateDestination	-ConnectionString "<connectionString>"  `
						-Verbose
#>
function Set-UpdateDestination {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $True)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $True)]
        [string] $ConnectionString
    )

    Write-Host "Start Set-UpdateDestination"

    $tableName = "SyncJobs"
    $tableSchema = "dbo"

    # Connect to the SQL Server instance
    $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
    $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate($context.Account, $context.Environment, $context.Tenant.Id.ToString(), $null, [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never, $null, "https://database.windows.net").AccessToken    
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $ConnectionString
    $connection.AccessToken = $sqlToken

    $tableExists = Invoke-SqlOperationWithFirewallRetry `
                        -EnvironmentAbbreviation $EnvironmentAbbreviation `
                        -SolutionAbbreviation $SolutionAbbreviation `
                        -Operation { 
                            $connection.Open()
                    
                            # Check if the table exists
                            $checkTableQuery = "SELECT CASE WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '$tableSchema' AND TABLE_NAME = '$tableName') THEN 1 ELSE 0 END AS TableExists;"

                            $command = $connection.CreateCommand()
                            $command.CommandText = $checkTableQuery
                            $tableExistsResult = $command.ExecuteScalar()

                            # Dispose the command and close the connection
                            $command.Dispose()
                            $connection.Close()

                            return $tableExistsResult
                        }

    if ($tableExists -eq 1) {
        Write-Host "The table '$tableSchema.$tableName' exists."
        Write-Host "Finding all jobs that need their Destination column updated..."
        $dataTable = Invoke-SqlOperationWithFirewallRetry `
                        -EnvironmentAbbreviation $EnvironmentAbbreviation `
                        -SolutionAbbreviation $SolutionAbbreviation `
                        -Operation { 
                            # Retrieve data from the table
                            $query = "SELECT * FROM $tableName WHERE Destination IS NULL OR LTRIM(RTRIM(Destination)) = '' OR JSON_VALUE(JSON_QUERY(Destination, '$[0].value'), '$.objectId') != TargetOfficeGroupId;"
                            $connection.Open()
                            $command = $connection.CreateCommand()
                            $command.CommandText = $query

                            # Create a DataTable to store the results
                            $dataTableResult = New-Object System.Data.DataTable
                            $dataAdapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
                            [void]$dataAdapter.Fill($dataTableResult)

                            # Dispose of the command
                            $command.Dispose()
                            $connection.Close()

                            return $dataTableResult
                        }

        Write-Host "Found $($dataTable.Count) jobs that need their Destination column updated."

        # Loop through the DataTable and update the "Destination" column
        foreach ($row in $dataTable) {
            $id = $row["Id"]
            $destination = $row["Destination"]
            $targetOfficeId = $row["TargetOfficeGroupId"].ToString()

            Write-Host "`nUpdating job with Id: $id, TargetOfficeGroupId: $targetOfficeId, current Destination: $destination"  

            # Apply the logic from the PowerShell code to update the "Destination" column
            $destination = @{"type" = "GroupMembership"; "value" = @{"objectId" = $targetOfficeId}} | ConvertTo-Json -Compress -AsArray
            $updateQuery = "UPDATE SyncJobs SET Destination = @Destination WHERE Id = @Id"

            # Use parameterized query to handle data escaping
            $updateCommand = $connection.CreateCommand()
            $updateCommand.CommandText = $updateQuery
            $updateCommand.Parameters.Add((New-Object Data.SqlClient.SqlParameter("@Destination", [Data.SqlDbType]::NVarChar, -1))).Value = $destination
            $updateCommand.Parameters.Add((New-Object Data.SqlClient.SqlParameter("@Id", [Data.SqlDbType]::UniqueIdentifier))).Value = [System.Guid]::Parse($id)

            Invoke-SqlOperationWithFirewallRetry `
                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                -SolutionAbbreviation $SolutionAbbreviation `
                -Operation { 
                    $connection.Open()
                    [void]$updateCommand.ExecuteNonQuery()
                    $connection.Close()
                }
                
                Write-Host "`nUpdated job with Id: $id, TargetOfficeGroupId: $targetOfficeId, new Destination: $destination"
        }

    } else {
        Write-Host "The table '$tableSchema.$tableName' does not exist. Skipping the Destination column update."
    }

    Write-Host "`nFinish Set-UpdateDestination"
}