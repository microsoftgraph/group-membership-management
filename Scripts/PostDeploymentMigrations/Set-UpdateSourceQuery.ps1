function Set-UpdateSourceQuery {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $ConnectionString
    )

    Write-Host "Start Set-UpdateSourceQuery"

    $tableName = "SyncJobs"
    $tableSchema = "dbo"

    # Connect to the SQL Server instance
    $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
    $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate($context.Account, $context.Environment, $context.Tenant.Id.ToString(), $null, [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never, $null, "https://database.windows.net").AccessToken
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $ConnectionString
    $connection.AccessToken = $sqlToken
    $connection.Open()
    
    # Check if the table exists
    $checkTableQuery = "SELECT CASE WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '$tableSchema' AND TABLE_NAME = '$tableName') THEN 1 ELSE 0 END AS TableExists;"


    $checkCommand = $connection.CreateCommand()
    $checkCommand.CommandText = $checkTableQuery
    $tableExists = $checkCommand.ExecuteScalar()

    if ($tableExists -eq 1) {
        Write-Output "The table '$tableSchema.$tableName' exists."

        # Replace SecurityGroup with GroupMembership
        $updateQuery = "UPDATE SyncJobs SET Query = REPLACE(Query, 'SecurityGroup', 'GroupMembership') WHERE Query LIKE '%SecurityGroup%'"
        $updateCommand = $connection.CreateCommand()
        $updateCommand.CommandText = $updateQuery
        $updateCommand.ExecuteNonQuery()

         # Replace != with <>
        $updateQuery = "UPDATE SyncJobs SET Query = REPLACE(Query, '!=', '<>') WHERE Query LIKE '%!=%'"
        $updateCommand = $connection.CreateCommand()
        $updateCommand.CommandText = $updateQuery
        $updateCommand.ExecuteNonQuery()

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

        $type = @("SqlMembership", "GroupMembership")

        $queriesUpdatedFromOldFormat = 0
        $queriesUpdatedFromNewFormat = 0
        $queriesUpdatedFromUnknownFormat = 0
        $queriesNotUpdated = 0

        $fromOldFormatFlag = $false
        $fromUnknownFormatFlag = $false
        $fromNewFormatFlag = $false

        foreach ($row in $dataTable.Rows)
        {
            if (-not [string]::IsNullOrEmpty($row["Query"])) {

                $newQueryParts = @()
                $currentQuery = ConvertFrom-Json -InputObject $row["Query"]

                foreach($part in $currentQuery) {

                    if($part.type -eq "GroupMembership" -and $part.source) {
                        foreach($id in $part.source) {
                            if($part.exclusionary -eq $true) {
                                    $newQueryPart = '{"type":"GroupMembership","source":"' + $id + '", "exclusionary": true}'
                            }
                            else {
                                $newQueryPart = '{"type":"GroupMembership","source":"' + $id + '"}'
                            }
                            $newQueryParts += $newQueryPart
                        }
                        $fromOldFormatFlag = $true
                    }
                    elseif($part.type -eq "SqlMembership" -and $part.source) {
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
                                    $newQueryPart = ([regex]'(?i)\\u([0-9a-h]{4})').Replace($newQueryPart, {param($Match) "$([char][int64]"0x$($Match.Groups[1].Value)")"})
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
                        $fromOldFormatFlag = $true
                    }
                    elseif($part.type -notin $type) {
                        $newQueryParts +=  ConvertTo-Json -InputObject $part -Compress -Depth 100
                        $fromUnknownFormatFlag = $true
                    }
                    else {
                        $newQueryParts +=  ConvertTo-Json -InputObject $part -Compress -Depth 100
                        $fromNewFormatFlag = $true
                    }
                }

                $newQuery = "[" + ($newQueryParts -join ",") + "]"
                if ($row["Query"] -ne $newQuery) {
                    $updateQuery = "UPDATE SyncJobs SET Query = @Query WHERE Id = @Id"

                    # Use parameterized query to handle data escaping
                    $updateCommand = $connection.CreateCommand()
                    $updateCommand.CommandText = $updateQuery


                    $updateCommand.Parameters.Add((New-Object Data.SqlClient.SqlParameter("@Query", [Data.SqlDbType]::NVarChar, -1))).Value = $newQuery
                    $updateCommand.Parameters.Add((New-Object Data.SqlClient.SqlParameter("@Id", [Data.SqlDbType]::UniqueIdentifier))).Value = [System.Guid]::Parse($row["Id"])


                    $connection.Open()
                    [void]$updateCommand.ExecuteNonQuery()
                    $connection.Close()

                    if ($fromUnknownFormatFlag) {
                        $queriesUpdatedFromUnknownFormat += 1
                        $fromUnknownFormatFlag = $false
                    }
                    elseif ($fromOldFormatFlag) {
                        $queriesUpdatedFromOldFormat += 1
                        $fromOldFormatFlag = $false
                    }
                    elseif ($fromNewFormatFlag) {
                        $queriesUpdatedFromNewFormat += 1
                        $fromNewFormatFlag = $false
                    }
                }
                else {
                    $queriesNotUpdated += 1
                }
            }
        }
        Write-Host "Queries updated from old format: " $queriesUpdatedFromOldFormat
        Write-Host "Queries updated from new format (removed white space): " $queriesUpdatedFromNewFormat
        Write-Host "Queries updated from unknown format: " $queriesUpdatedFromUnknownFormat
        Write-Host "Queries not updated: " $queriesNotUpdated
    } else {
        Write-Output "The table '$tableSchema.$tableName' does not exist. Skipping the Query update."
    }
    
    # Close the connection
    $checkCommand.Dispose()
    $connection.Close()
    
    Write-Host "Finish Set-UpdateSourceQuery"
}
