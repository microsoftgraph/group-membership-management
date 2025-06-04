$ErrorActionPreference = "Stop"
<#
.DESCRIPTION
This script is simplified to only replace BETWEEN/NOT BETWEEN operators with IN/NOT IN 
operators in SQL filters for SqlMembership jobs. It properly preserves quotes around values
in the generated IN lists.

.PARAMETER ConnectionString
sqlDatabaseConnectionString which can be found in data keyvault

.EXAMPLE
Set-UpdateSqlMembershipQueryBetweenFilter -ConnectionString "<sqlDatabaseConnectionString>" `
							 -Verbose
#>

function Convert-BetweenToIn {
    param (
        [string]$filter
    )    # Keep replacing until no more matches are found
    $previousFilter = ""
    while ($previousFilter -ne $filter) {
        $previousFilter = $filter

        # Handle numeric values with NOT BETWEEN
        $filter = [regex]::Replace($filter, "(?i)([\w\._]+)\s+NOT\s+BETWEEN\s+('?)(\d+)('?)\s+AND\s+('?)(\d+)('?)", {
            param($match)
            $field = $match.Groups[1].Value
            
            # Get the starting and ending quotes
            $startQuote = $match.Groups[2].Value
            $endQuote = $match.Groups[5].Value
            
            # Use starting quote if it exists, otherwise use ending quote (should be the same)
            $quote = if ($startQuote) { $startQuote } else { $endQuote }
            
            # Get the numeric values
            $start = [int]$match.Groups[3].Value
            $end = [int]$match.Groups[6].Value
            
            # Generate the values list with appropriate quoting
            $values = ($start..$end | ForEach-Object { "$quote$_$quote" }) -join ", "
            
            return "$field NOT IN ($values)"
        })
        
        # Handle numeric values with BETWEEN
        $filter = [regex]::Replace($filter, "(?i)([\w\._]+)\s+BETWEEN\s+('?)(\d+)('?)\s+AND\s+('?)(\d+)('?)", {
            param($match)
            $field = $match.Groups[1].Value
            
            # Get the starting and ending quotes
            $startQuote = $match.Groups[2].Value
            $endQuote = $match.Groups[5].Value
            
            # Use starting quote if it exists, otherwise use ending quote (should be the same)
            $quote = if ($startQuote) { $startQuote } else { $endQuote }
            
            # Get the numeric values
            $start = [int]$match.Groups[3].Value
            $end = [int]$match.Groups[6].Value
            
            # Generate the values list with appropriate quoting
            $values = ($start..$end | ForEach-Object { "$quote$_$quote" }) -join ", "
            
            return "$field IN ($values)"
        })
        # Handle string values with quotes for NOT BETWEEN
        $filter = [regex]::Replace($filter, "(?i)([\w\._]+)\s+NOT\s+BETWEEN\s+(['""])([^'""]+?)(\2)\s+AND\s+(['""])([^'""]+?)(\5)", {
            param($match)
            $field = $match.Groups[1].Value
            $quote = $match.Groups[2].Value
            $start = $match.Groups[3].Value
            $end = $match.Groups[6].Value
            
            # For strings, check if they're numeric strings that we can enumerate
            if ($start -match '^\d+$' -and $end -match '^\d+$') {
                # If both are numeric strings, we can generate all values in between
                $startInt = [int]$start
                $endInt = [int]$end
                
                # Check for excessively large ranges to prevent performance issues
                if (($endInt - $startInt) -gt $MaxRangeSize) {
                    Write-Warning "Range between $startInt and $endInt exceeds the maximum range size of $MaxRangeSize. Using only start and end values."
                    $values = "$quote$start$quote, $quote$end$quote"
                } else {
                    $values = ($startInt..$endInt | ForEach-Object { "$quote$_$quote" }) -join ", "
                }
            } else {
                # For non-numeric strings, just include start and end values
                $values = "$quote$start$quote, $quote$end$quote"
            }
            
            return "$field NOT IN ($values)"
        })
        
        # Handle string values with quotes for BETWEEN
        $filter = [regex]::Replace($filter, "(?i)([\w\._]+)\s+BETWEEN\s+(['""])([^'""]+?)(\2)\s+AND\s+(['""])([^'""]+?)(\5)", {
            param($match)
            $field = $match.Groups[1].Value
            $quote = $match.Groups[2].Value
            $start = $match.Groups[3].Value
            $end = $match.Groups[6].Value
            
            # For strings, we need to handle them differently than numbers
            # Generate a comma-separated list of all values between start and end (inclusive)
            # For simplicity here, we'll just include the start and end values
            $values = "$quote$start$quote, $quote$end$quote"
            
            return "$field IN ($values)"
        })}
    
    return $filter
}

function Set-UpdateSqlMembershipQueryBetweenFilter {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $ConnectionString,
        
        [Parameter(Mandatory=$False)]
        [int] $MaxRangeSize = 1000
    )
    
    Write-Host "Start Set-UpdateSqlMembershipQueryBetweenFilter"
    
    try {# Connect to the SQL Server instance
    $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
    $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate($context.Account, $context.Environment, $context.Tenant.Id.ToString(), $null, [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never, $null, "https://database.windows.net").AccessToken
    
    # Create a connection without the Authentication parameter in the connection string
    $cleanConnectionString = $ConnectionString -replace 'Authentication="[^"]*"\s*;?', ''
    
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $cleanConnectionString
    $connection.AccessToken = $sqlToken
    $connection.Open()    # Retrieve data from the SQL table
    $query = "SELECT * FROM [SyncJobs] WHERE Query LIKE '%BETWEEN%' OR Query LIKE '%between%'" 
    $command = $connection.CreateCommand()
    $command.CommandText = $query

    # Create a DataTable to store the results
    $dataTable = New-Object System.Data.DataTable
    $dataAdapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
    [void]$dataAdapter.Fill($dataTable)

    # Close the DataReader and the connection
    $command.Dispose()
    $connection.Close()

    Write-Host "Found $($dataTable.Rows.Count) jobs with BETWEEN in their query"

    foreach ($row in $dataTable.Rows) {
        if (-not [string]::IsNullOrEmpty($row["Query"])) {
            $currentQuery = $row["Query"]            # Parse the JSON to process filters
            $queryObj = ConvertFrom-Json -InputObject $currentQuery
            
            # Copy the original query to use for string replacement
            $newQuery = $currentQuery
            $modified = $false            # Process the JSON object to find filter strings
            foreach ($part in $queryObj) {
                if ($part.type -eq "SqlMembership" -and $part.source) {
                    # Handle direct filter on source - this works whether source is a simple object or has other properties
                    if ($part.source.PSObject.Properties.Name -contains "filter" -and 
                        ($part.source.filter -match "BETWEEN" -or $part.source.filter -match "NOT\s+BETWEEN")) {
                        $oldFilter = $part.source.filter
                        $newFilter = Convert-BetweenToIn -filter $oldFilter
                        
                        # Only replace this specific filter in the original JSON string
                        if ($oldFilter -ne $newFilter) {
                            # Store the original filter for later use in string replacement
                            $part.source | Add-Member -MemberType NoteProperty -Name "originalFilter" -Value $oldFilter -Force
                            # Create updated object with modified filter
                            $part.source.filter = $newFilter
                            $modified = $true
                        }
                    }
                    # Handle filter in nested sources if it's an array
                    if ($part.source -is [System.Array]) {
                        foreach ($src in $part.source) {
                            if ($src.filter -and ($src.filter -match "BETWEEN" -or $src.filter -match "NOT\s+BETWEEN")) {
                                $oldFilter = $src.filter
                                $newFilter = Convert-BetweenToIn -filter $oldFilter
                                
                                # Only replace this specific filter in the original JSON string
                                if ($oldFilter -ne $newFilter) {
                                    # Store the original filter for later use in string replacement
                                    $src | Add-Member -MemberType NoteProperty -Name "originalFilter" -Value $oldFilter -Force
                                    $src.filter = $newFilter
                                    $modified = $true
                                }
                            }
                        }
                    }
                }
            }
            # Only update if changes were made
            if ($modified) {
                Write-Host "Updating query for job $($row["Id"])"
                # Instead of using ConvertTo-Json directly, we'll do a simple string replacement
                # to preserve the original format and avoid unwanted escaping
                foreach ($part in $queryObj) {
                    if ($part.type -eq "SqlMembership" -and $part.source -and 
                        $part.source.PSObject.Properties.Name -contains "filter" -and
                        $part.source.PSObject.Properties.Name -contains "originalFilter") {
                        
                        $oldFilter = [regex]::Escape($part.source.originalFilter)
                        $newFilter = $part.source.filter
                        $newQuery = $newQuery -replace $oldFilter, $newFilter
                    }
                }
                
                # Create a new connection for the update
                $updateConnection = New-Object System.Data.SqlClient.SqlConnection
                $updateConnection.ConnectionString = $cleanConnectionString
                $updateConnection.AccessToken = $sqlToken
                $updateConnection.Open()

                $updateQuery = "UPDATE [SyncJobs] SET Query = @Query WHERE Id = @Id"
                $updateCommand = $updateConnection.CreateCommand()
                $updateCommand.CommandText = $updateQuery
                $updateCommand.Parameters.Add((New-Object Data.SqlClient.SqlParameter("@Query", [Data.SqlDbType]::NVarChar, -1))).Value = $newQuery
                $updateCommand.Parameters.Add((New-Object Data.SqlClient.SqlParameter("@Id", [Data.SqlDbType]::UniqueIdentifier))).Value = [System.Guid]::Parse($row["Id"])
                [void]$updateCommand.ExecuteNonQuery()
                $updateConnection.Close()
            }
        }
    }
      Write-Host "Finish Set-UpdateSqlMembershipQueryBetweenFilter"
    } catch {
        Write-Error "An error occurred: $_"
        Write-Error $_.ScriptStackTrace
        throw $_
    }
}
