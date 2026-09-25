# Runs on every deployment. Rewrites SyncJobs.Query values saved in older formats into the
# current format: SecurityGroup parts become GroupMembership, parts listing several group or
# manager IDs are split into one part per ID, and "!=" becomes "<>". SQL parts already in the
# current format with a valid identityTypes list are kept exactly as stored. All changes are
# saved in a single transaction.
#
# Rewritten JSON must stay compact (no whitespace), matching the WebApi serializer; otherwise
# every deployment sees a difference and rewrites the same rows again.

function Build-SqlQueryPart {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $OriginalPart,

        [Parameter(Mandatory = $true)]
        $NewSource
    )

    $rebuilt = [ordered]@{}
    $sawSource = $false

    foreach ($property in $OriginalPart.PSObject.Properties) {
        if ($property.Name -ceq 'source') {
            $rebuilt['source'] = $NewSource
            $sawSource = $true
        }
        else {
            $rebuilt[$property.Name] = $property.Value
        }
    }

    if (-not $sawSource) {
        $rebuilt['source'] = $NewSource
    }

    return ConvertTo-Json -InputObject ([PSCustomObject]$rebuilt) -Compress -Depth 100
}

function Test-SourceQueryIdentityTypesSelection {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Selection
    )

    if ($Selection.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
        return $false
    }

    $values = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $Selection.EnumerateArray()) {
        if ($item.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
            return $false
        }

        $value = $item.GetString()
        if (-not [string]::Equals($value, 'users', [StringComparison]::Ordinal) -and
            -not [string]::Equals($value, 'agents', [StringComparison]::Ordinal)) {
            return $false
        }

        if (-not $values.Add($value)) {
            return $false
        }
    }

    return $values.Count -gt 0
}

function Get-SourceQueryRawParts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($Query)
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
            return @()
        }

        $parts = [Collections.Generic.List[object]]::new()
        foreach ($part in $document.RootElement.EnumerateArray()) {
            $preserve = $false

            if ($part.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
                $typeProperty = [System.Text.Json.JsonElement]::new()
                $selectionProperty = [System.Text.Json.JsonElement]::new()
                $sourceProperty = [System.Text.Json.JsonElement]::new()

                $hasType = $part.TryGetProperty('type', [ref]$typeProperty)
                $hasSelection = $part.TryGetProperty('identityTypes', [ref]$selectionProperty)
                $hasSource = $part.TryGetProperty('source', [ref]$sourceProperty)

                if ($hasType -and
                    $typeProperty.ValueKind -eq [System.Text.Json.JsonValueKind]::String -and
                    [string]::Equals($typeProperty.GetString(), 'SqlMembership', [StringComparison]::Ordinal) -and
                    $hasSelection -and
                    (Test-SourceQueryIdentityTypesSelection -Selection $selectionProperty) -and
                    $hasSource -and
                    $sourceProperty.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {

                    $idsProperty = [System.Text.Json.JsonElement]::new()
                    $hasIds = $sourceProperty.TryGetProperty('ids', [ref]$idsProperty)
                    $preserve = -not $hasIds
                }
            }

            $parts.Add([PSCustomObject]@{
                RawText = $part.GetRawText()
                Preserve = $preserve
            })
        }

        return $parts.ToArray()
    }
    catch {
        return @()
    }
    finally {
        if ($null -ne $document) {
            $document.Dispose()
        }
    }
}

function Convert-SqlSourceQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Query
    )

    if ([string]::IsNullOrEmpty($Query)) {
        return [PSCustomObject]@{
            Status = 'NoChanges'
            TransformedQuery = $Query
            Category = 'None'
        }
    }

    $currentQuery = @(ConvertFrom-Json -InputObject $Query)
    $rawParts = @(Get-SourceQueryRawParts -Query $Query)
    if ($rawParts.Count -ne $currentQuery.Count) {
        $rawParts = @()
    }

    $newQueryParts = [Collections.Generic.List[string]]::new()
    $fromOldFormat = $false
    $fromUnknownFormat = $false
    $fromNewFormat = $false
    $knownTypes = @('SqlMembership', 'GroupMembership')

    for ($partIndex = 0; $partIndex -lt $currentQuery.Count; $partIndex++) {
        $part = $currentQuery[$partIndex]

        if ($rawParts.Count -eq $currentQuery.Count -and $rawParts[$partIndex].Preserve) {
            $newQueryParts.Add($rawParts[$partIndex].RawText)
            $fromNewFormat = $true
            continue
        }

        $partType = if ($part.type -eq 'SecurityGroup') { 'GroupMembership' } else { $part.type }

        if ($partType -eq 'GroupMembership' -and $part.source) {
            foreach ($id in $part.source) {
                if ($part.exclusionary -eq $true) {
                    $newQueryParts.Add('{"type":"GroupMembership","source":"' + $id + '","exclusionary":true}')
                }
                else {
                    $newQueryParts.Add('{"type":"GroupMembership","source":"' + $id + '"}')
                }
            }

            $fromOldFormat = $true
            continue
        }

        if ($partType -eq 'SqlMembership' -and $part.source) {
            foreach ($source in $part.source) {
                if ($source.manager.id) {
                    $newQueryParts.Add(
                        (Build-SqlQueryPart -OriginalPart $part -NewSource $source).Replace('!=', '<>'))
                }
                elseif ($source.ids) {
                    foreach ($id in $source.ids) {
                        $expandedSource = [ordered]@{
                            manager = [ordered]@{
                                id = $id
                            }
                        }

                        if ($source.depth) {
                            $expandedSource.manager.depth = $source.depth
                        }

                        if ($source.filter) {
                            $expandedSource.filter = $source.filter
                        }

                        $newQueryParts.Add(
                            (Build-SqlQueryPart -OriginalPart $part -NewSource $expandedSource).Replace('!=', '<>'))
                    }
                }
                elseif ($source.filter) {
                    $newQueryParts.Add(
                        (Build-SqlQueryPart -OriginalPart $part -NewSource $source).Replace('!=', '<>'))
                }
            }

            $fromOldFormat = $true
            continue
        }

        $serializedPart = ConvertTo-Json -InputObject $part -Compress -Depth 100
        $serializedPart = $serializedPart.Replace('SecurityGroup', 'GroupMembership').Replace('!=', '<>')
        $newQueryParts.Add($serializedPart)

        if ($partType -notin $knownTypes) {
            $fromUnknownFormat = $true
        }
        else {
            $fromNewFormat = $true
        }
    }

    $newQuery = '[' + ($newQueryParts -join ',') + ']'
    $category = if ($fromUnknownFormat) {
        'Unknown'
    }
    elseif ($fromOldFormat) {
        'Old'
    }
    elseif ($fromNewFormat) {
        'New'
    }
    else {
        'None'
    }

    return [PSCustomObject]@{
        Status = if ([string]::Equals($Query, $newQuery, [StringComparison]::Ordinal)) { 'NoChanges' } else { 'Changed' }
        TransformedQuery = $newQuery
        Category = $category
    }
}

function New-SourceQueryMigrationConnection {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions',
        '',
        Justification = 'Creates an unopened in-memory connection.')]
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString
    )

    $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
    $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate(
        $context.Account,
        $context.Environment,
        $context.Tenant.Id.ToString(),
        $null,
        [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never,
        $null,
        'https://database.windows.net').AccessToken

    $connection = [Data.SqlClient.SqlConnection]::new()
    $connection.ConnectionString = $ConnectionString
    $connection.AccessToken = $sqlToken
    return $connection
}

function Set-UpdateSourceQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$ConnectionString
    )

    Write-Host 'Start Set-UpdateSourceQuery'

    $connection = New-SourceQueryMigrationConnection -ConnectionString $ConnectionString
    try {
        $tableExists = Invoke-SqlOperationWithFirewallRetry `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SolutionAbbreviation $SolutionAbbreviation `
            -Operation {
            try {
                $connection.Open()
                $checkCommand = $connection.CreateCommand()
                $checkCommand.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.SyncJobs', N'U') IS NULL THEN 0 ELSE 1 END;"
                try {
                    return $checkCommand.ExecuteScalar()
                }
                finally {
                    $checkCommand.Dispose()
                }
            }
            finally {
                if ($connection.State -ne [Data.ConnectionState]::Closed) {
                    $connection.Close()
                }
            }
        }
        if ($tableExists -ne 1) {
            Write-Output "The table 'dbo.SyncJobs' does not exist. Skipping the Query update."
            return
        }

        Write-Output "The table 'dbo.SyncJobs' exists."

        $dataTable = Invoke-SqlOperationWithFirewallRetry `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SolutionAbbreviation $SolutionAbbreviation `
            -Operation {
            try {
                $connection.Open()
                $command = $connection.CreateCommand()
                $command.CommandText = 'SELECT Id, [Query] FROM dbo.SyncJobs'
                $dataTableResult = [Data.DataTable]::new()
                $dataAdapter = [Data.SqlClient.SqlDataAdapter]::new($command)

                try {
                    [void]$dataAdapter.Fill($dataTableResult)
                    return $dataTableResult
                }
                finally {
                    $dataAdapter.Dispose()
                    $command.Dispose()
                }
            }
            finally {
                if ($connection.State -ne [Data.ConnectionState]::Closed) {
                    $connection.Close()
                }
            }
        }

        $queriesUpdatedFromOldFormat = 0
        $queriesUpdatedFromNewFormat = 0
        $queriesUpdatedFromUnknownFormat = 0
        $queriesNotUpdated = 0
        $pendingUpdates = [Collections.Generic.List[object]]::new()

        foreach ($row in $dataTable) {
            if ([string]::IsNullOrEmpty($row['Query'])) {
                continue
            }

            $originalQuery = [string]$row['Query']
            $converted = Convert-SqlSourceQuery -Query $originalQuery
            if ($converted.Status -eq 'NoChanges') {
                $queriesNotUpdated++
                continue
            }

            $pendingUpdates.Add([PSCustomObject]@{
                    Id            = [Guid]::Parse([string]$row['Id'])
                    OriginalQuery = $originalQuery
                    Query         = $converted.TransformedQuery
                })

            switch ($converted.Category) {
                'Unknown' { $queriesUpdatedFromUnknownFormat++ }
                'Old' { $queriesUpdatedFromOldFormat++ }
                'New' { $queriesUpdatedFromNewFormat++ }
            }
        }

        if ($pendingUpdates.Count -gt 0) {
            [void](Invoke-SqlOperationWithFirewallRetry `
                    -EnvironmentAbbreviation $EnvironmentAbbreviation `
                    -SolutionAbbreviation $SolutionAbbreviation `
                    -Operation {
                    $bulkCopy = $null
                    $staging = $null
                    $transaction = $null
                    $resolved = $false

                    try {
                        $connection.Open()
                        $transaction = $connection.BeginTransaction()

                        $createCommand = $connection.CreateCommand()
                        $createCommand.Transaction = $transaction
                        $createCommand.CommandText = 'CREATE TABLE #RewrittenQueries (Id uniqueidentifier NOT NULL PRIMARY KEY, OriginalQuery nvarchar(max) NOT NULL, NewQuery nvarchar(max) NOT NULL);'
                        try {
                            [void]$createCommand.ExecuteNonQuery()
                        }
                        finally {
                            $createCommand.Dispose()
                        }

                        $staging = [Data.DataTable]::new()
                        [void]$staging.Columns.Add('Id', [Guid])
                        [void]$staging.Columns.Add('OriginalQuery', [string])
                        [void]$staging.Columns.Add('NewQuery', [string])
                        foreach ($pendingUpdate in $pendingUpdates) {
                            [void]$staging.Rows.Add($pendingUpdate.Id, $pendingUpdate.OriginalQuery, $pendingUpdate.Query)
                        }

                        $bulkCopy = [Data.SqlClient.SqlBulkCopy]::new(
                            $connection,
                            [Data.SqlClient.SqlBulkCopyOptions]::Default,
                            $transaction)
                        $bulkCopy.DestinationTableName = '#RewrittenQueries'
                        $bulkCopy.BatchSize = 5000
                        $bulkCopy.BulkCopyTimeout = 600
                        [void]$bulkCopy.ColumnMappings.Add('Id', 'Id')
                        [void]$bulkCopy.ColumnMappings.Add('OriginalQuery', 'OriginalQuery')
                        [void]$bulkCopy.ColumnMappings.Add('NewQuery', 'NewQuery')
                        $bulkCopy.WriteToServer($staging)

                        $applyCommand = $connection.CreateCommand()
                        $applyCommand.Transaction = $transaction
                        $applyCommand.CommandTimeout = 600
                        $applyCommand.CommandText = @'
UPDATE jobs
SET jobs.[Query] = rewritten.NewQuery
FROM dbo.SyncJobs AS jobs
INNER JOIN #RewrittenQueries AS rewritten ON rewritten.Id = jobs.Id
WHERE DATALENGTH(jobs.[Query]) = DATALENGTH(rewritten.OriginalQuery)
  AND CONVERT(varbinary(max), jobs.[Query]) = CONVERT(varbinary(max), rewritten.OriginalQuery);
'@

                        try {
                            $affected = $applyCommand.ExecuteNonQuery()
                        }
                        finally {
                            $applyCommand.Dispose()
                        }

                        if ($affected -ne $pendingUpdates.Count) {
                            throw "Expected to update $($pendingUpdates.Count) query value(s) but $affected row(s) were affected. A job was changed or deleted after it was read, so no changes were saved; run the script again."
                        }

                        $transaction.Commit()
                        $resolved = $true
                        return $affected
                    }
                    finally {
                        if ($null -ne $bulkCopy) {
                            $bulkCopy.Close()
                            $bulkCopy.Dispose()
                        }

                        if ($null -ne $staging) {
                            $staging.Dispose()
                        }

                        if ($null -ne $transaction) {
                            if (-not $resolved) {
                                try {
                                    $transaction.Rollback()
                                }
                                catch {
                                    Write-Verbose 'Rollback failed after the source-query migration transaction.'
                                }
                            }

                            $transaction.Dispose()
                        }

                        if ($connection.State -ne [Data.ConnectionState]::Closed) {
                            $connection.Close()
                        }
                    }
                })
        }

        Write-Host "Queries updated from old format: $queriesUpdatedFromOldFormat"
        Write-Host "Queries updated from new format: $queriesUpdatedFromNewFormat"
        Write-Host "Queries updated from unknown format: $queriesUpdatedFromUnknownFormat"
        Write-Host "Queries not updated: $queriesNotUpdated"
    }
    finally {
        $connection.Dispose()
        Write-Host 'Finish Set-UpdateSourceQuery'
    }
}
