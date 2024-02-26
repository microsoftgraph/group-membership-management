$ErrorActionPreference = "Stop"
<#
.DESCRIPTION
This script converts all existing SqlMembership jobs to the new format

.PARAMETER ConnectionString
sqlDatabaseConnectionString which can be found in data keyvault

.EXAMPLE
Set-UpdateSqlMembershipQuery -ConnectionString "<sqlDatabaseConnectionString>" `
							 -Verbose
#>

function Set-UpdateSqlMembershipQuery {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $ConnectionString
    )

    Write-Host "Start Set-UpdateSqlMembershipQuery"

    # Connect to the SQL Server instance
    $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
    $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate($context.Account, $context.Environment, $context.Tenant.Id.ToString(), $null, [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never, $null, "https://database.windows.net").AccessToken
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $ConnectionString
    $connection.AccessToken = $sqlToken
    $connection.Open()

    # Retrieve data from the SQL table
    $query = "SELECT * FROM SyncJobs"
    $command = $connection.CreateCommand()
    $command.CommandText = $query

    # Create a DataTable to store the results
    $dataTable = New-Object System.Data.DataTable
    $dataAdapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
    [void]$dataAdapter.Fill($dataTable)

    # Close the DataReader and the connection
    $command.Dispose()
    $connection.Close()

    $type = @("SqlMembership")

    foreach ($row in $dataTable.Rows)
    {
        if (-not [string]::IsNullOrEmpty($row["Query"])) {

            $newQueryParts = @()
            $currentQuery = ConvertFrom-Json -InputObject $row["Query"]

            foreach($part in $currentQuery) {

                if($part.type -eq "SqlMembership" -and $part.source) {
                    foreach($source in $part.source) {
                        if ($source.manager.id) {
                            $sourceAsString = ConvertTo-Json -InputObject $source -Compress -Depth 100
                            write-host $sourceAsString
                            $sourceAsString = ([regex]'(?i)\\u([0-9a-h]{4})').Replace($sourceAsString, {param($Match) "$([char][int64]"0x$($Match.Groups[1].Value)")"})

                            if($part.exclusionary -eq $true) {
                                $newQueryPart = '{"type":"SqlMembership","source":' + $sourceAsString + ', "exclusionary": true}'
                            }
                            else {
                                $newQueryPart = '{"type":"SqlMembership","source":' + $sourceAsString + '}'
                            }
                            $newQueryParts += $newQueryPart
                        }

                        elseif ($source.ids) {
                            foreach ($id in $part.source.ids) {
                                $newQueryPart = @{
                                    type = "SqlMembership"
                                    source = @{
                                        manager = @{
                                            id = $id
                                        }
                                    }
                                }

                                if ($part.source.depth) {
                                    $newQueryPart.source.manager.depth = $part.source.depth
                                }
                                if ($part.source.filter) {
                                    $newQueryPart.source.filter = $part.source.filter
                                }
                                if($part.exclusionary -eq $true)
                                {
                                    $newQueryPart.exclusionary = $true
                                }

                                $newQueryPart = $newQueryPart | ConvertTo-Json -Depth 100 -Compress

                                $newQueryParts += $newQueryPart
                            }
                        }

                        elseif ($source.filter) {
                            $sourceAsString = ConvertTo-Json -InputObject $source -Compress -Depth 100
                            $sourceAsString = ([regex]'(?i)\\u([0-9a-h]{4})').Replace($sourceAsString, {param($Match) "$([char][int64]"0x$($Match.Groups[1].Value)")"})

                            if($part.exclusionary -eq $true) {
                                $newQueryPart = '{"type":"SqlMembership","source":' + $sourceAsString + ', "exclusionary": true}'
                            }
                            else {
                                $newQueryPart = '{"type":"SqlMembership","source":' + $sourceAsString + '}'
                            }
                            $newQueryParts += $newQueryPart
                        }
                    }
                }
                elseif($part.type -notin $type) {
                    $newQueryParts +=  ConvertTo-Json -InputObject $part -Compress -Depth 100
                }
                else {
                    $newQueryParts +=  ConvertTo-Json -InputObject $part -Compress -Depth 100
                }
            }

            $newQuery = "[" + ($newQueryParts -join ",") + "]"
            if ($row["Query"] -ne $newQuery) {
                $updateQuery = "UPDATE SyncJobs SET Query = @Query WHERE Id = @Id"
                $updateCommand = $connection.CreateCommand()
                $updateCommand.CommandText = $updateQuery
                $updateCommand.Parameters.Add((New-Object Data.SqlClient.SqlParameter("@Query", [Data.SqlDbType]::NVarChar, -1))).Value = $newQuery
                $updateCommand.Parameters.Add((New-Object Data.SqlClient.SqlParameter("@Id", [Data.SqlDbType]::UniqueIdentifier))).Value = [System.Guid]::Parse($row["Id"])
                $connection.Open()
                [void]$updateCommand.ExecuteNonQuery()
                $connection.Close()
            }
        }
    }
    Write-Host "Finish Set-UpdateSqlMembershipQuery"
}