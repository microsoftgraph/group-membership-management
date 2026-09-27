# Runs on every deployment. Rewrites SyncJobs.Query values saved in older formats into the
# current format: SecurityGroup parts become GroupMembership, parts listing several group or
# manager IDs are split into one part per ID, and the "!=" operator in SQL filters becomes "<>".
# Parts that need no change, SQL parts in a shape the converter does not recognise, parts whose
# values cannot be written back exactly, and queries it cannot read are kept exactly as stored.
# All changes are saved in a single transaction.
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
    [OutputType([bool])]
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

function Test-JsonIntegerInRange {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter()]
        [AllowNull()]
        $Value,

        [Parameter(Mandatory = $true)]
        [long]$Minimum
    )

    return ($Value -is [int] -or $Value -is [long]) -and $Value -ge $Minimum -and $Value -le [int]::MaxValue
}

# True only when the converter can rewrite this SQL source without losing any of it. Manager and
# filter sources are written back with every property except the old portal's includeOrg and
# includeFilter flags, which nothing reads. The ids shape is rebuilt from ids, depth and filter, so
# it may carry only those and the same two flags.
# A depth of 0 and an empty filter are dropped because the service reads both as "not set".
function Test-ConvertibleSqlSource {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter()]
        [AllowNull()]
        $Source
    )

    if ($Source -isnot [System.Management.Automation.PSCustomObject]) {
        return $false
    }

    if ($Source.manager.id) {
        return $true
    }

    if ($null -eq $Source.PSObject.Properties['ids']) {
        return [bool]$Source.filter
    }

    foreach ($property in $Source.PSObject.Properties) {
        $value = $property.Value

        if ($property.Name -ceq 'ids') {
            if ($value -isnot [array] -or $value.Count -eq 0) {
                return $false
            }

            foreach ($id in $value) {
                if (-not (Test-JsonIntegerInRange -Value $id -Minimum 1)) {
                    return $false
                }
            }
        }
        elseif ($property.Name -ceq 'depth') {
            if ($null -ne $value -and -not (Test-JsonIntegerInRange -Value $value -Minimum 0)) {
                return $false
            }
        }
        elseif ($property.Name -ceq 'filter') {
            if ($null -ne $value -and $value -isnot [string]) {
                return $false
            }
        }
        elseif ($property.Name -cin @('includeOrg', 'includeFilter')) {
            if ($null -ne $value -and $value -isnot [bool]) {
                return $false
            }
        }
        else {
            return $false
        }
    }

    return $true
}

# True when a SQL source kept in the current format matches the current schema.
function Test-CurrentSqlSourceShape {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter()]
        [AllowNull()]
        $Source
    )

    if ($Source -isnot [System.Management.Automation.PSCustomObject]) {
        return $false
    }

    $hasManagerOrFilter = $false
    foreach ($property in $Source.PSObject.Properties) {
        $value = $property.Value

        if ($property.Name -ceq 'manager') {
            if ($value -isnot [System.Management.Automation.PSCustomObject]) {
                return $false
            }

            $hasId = $false
            foreach ($managerProperty in $value.PSObject.Properties) {
                if ($managerProperty.Name -cnotin @('id', 'depth') -or
                    -not (Test-JsonIntegerInRange -Value $managerProperty.Value -Minimum 1)) {
                    return $false
                }

                if ($managerProperty.Name -ceq 'id') {
                    $hasId = $true
                }
            }

            if (-not $hasId) {
                return $false
            }
        }
        elseif ($property.Name -ceq 'filter') {
            if ($value -isnot [string]) {
                return $false
            }
        }
        else {
            return $false
        }

        $hasManagerOrFilter = $true
    }

    return $hasManagerOrFilter
}

# Replaces the != operator with <>, the form the portal's filter editor offers. Text inside quoted
# strings, quoted identifiers and comments is left alone, because changing it would change which
# rows the filter matches.
function Convert-SqlFilterNotEqualOperator {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Filter
    )

    if (-not $Filter.Contains('!=')) {
        return $Filter
    }

    $result = [Text.StringBuilder]::new($Filter.Length)
    $closingQuote = [char]0
    $inLineComment = $false
    $blockCommentDepth = 0
    $index = 0

    while ($index -lt $Filter.Length) {
        $current = $Filter[$index]
        $next = if ($index + 1 -lt $Filter.Length) { $Filter[$index + 1] } else { [char]0 }

        if ($closingQuote -ne [char]0) {
            [void]$result.Append($current)
            $index++
            if ($current -eq $closingQuote) {
                if ($next -eq $closingQuote) {
                    [void]$result.Append($next)
                    $index++
                }
                else {
                    $closingQuote = [char]0
                }
            }

            continue
        }

        if ($inLineComment) {
            [void]$result.Append($current)
            $index++
            if ($current -eq "`n" -or $current -eq "`r") {
                $inLineComment = $false
            }

            continue
        }

        if ($blockCommentDepth -gt 0) {
            if ($current -eq '/' -and $next -eq '*') {
                $blockCommentDepth++
                [void]$result.Append($current).Append($next)
                $index += 2
            }
            elseif ($current -eq '*' -and $next -eq '/') {
                $blockCommentDepth--
                [void]$result.Append($current).Append($next)
                $index += 2
            }
            else {
                [void]$result.Append($current)
                $index++
            }

            continue
        }

        if ($current -eq '!' -and $next -eq '=') {
            [void]$result.Append('<>')
            $index += 2
            continue
        }

        if ($current -eq '-' -and $next -eq '-') {
            $inLineComment = $true
            [void]$result.Append($current).Append($next)
            $index += 2
            continue
        }

        if ($current -eq '/' -and $next -eq '*') {
            $blockCommentDepth = 1
            [void]$result.Append($current).Append($next)
            $index += 2
            continue
        }

        if ($current -eq "'") {
            $closingQuote = [char]"'"
        }
        elseif ($current -eq '"') {
            $closingQuote = [char]'"'
        }
        elseif ($current -eq '[') {
            $closingQuote = [char]']'
        }

        [void]$result.Append($current)
        $index++
    }

    return $result.ToString()
}

# Copies a manager or filter source with its filter normalised and without the old portal's
# includeOrg and includeFilter flags, which nothing reads. Every other property is kept.
function Copy-SqlSourceInCurrentFormat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Source
    )

    $copy = [ordered]@{}
    foreach ($property in $Source.PSObject.Properties) {
        $value = $property.Value
        if ($property.Name -cin @('includeOrg', 'includeFilter') -and ($null -eq $value -or $value -is [bool])) {
            continue
        }

        if ($property.Name -ceq 'filter' -and $value -is [string]) {
            $value = Convert-SqlFilterNotEqualOperator -Filter $value
        }

        $copy[$property.Name] = $value
    }

    return [PSCustomObject]$copy
}

function ConvertFrom-SourceQueryJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    if (-not (Test-Path -LiteralPath 'variable:script:sourceQueryReaderHasDateKind')) {
        $script:sourceQueryReaderHasDateKind = (Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')
    }

    # Keeps date-like text as stored instead of converting it to local time. PowerShell versions
    # without -DateKind return DateTime values, which Test-SourceQueryValueRewritable detects.
    if ($script:sourceQueryReaderHasDateKind) {
        return ConvertFrom-Json -InputObject $Query -DateKind String -ErrorAction Stop
    }

    return ConvertFrom-Json -InputObject $Query -ErrorAction Stop
}

# False when writing a parsed value back could change it: ConvertFrom-Json turned date-like text
# into a DateTime, or the value is nested deeper than ConvertTo-Json -Depth 100 writes out in full.
function Test-SourceQueryValueRewritable {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter()]
        [AllowNull()]
        $Value,

        [Parameter()]
        [int]$Depth = 0
    )

    if ($Value -is [datetime] -or $Value -is [datetimeoffset]) {
        return $false
    }

    $isObject = $Value -is [System.Management.Automation.PSCustomObject]
    if (-not $isObject -and $Value -isnot [array]) {
        return $true
    }

    if ($Depth -ge 90) {
        return $false
    }

    if ($isObject) {
        foreach ($property in $Value.PSObject.Properties) {
            if (-not (Test-SourceQueryValueRewritable -Value $property.Value -Depth ($Depth + 1))) {
                return $false
            }
        }
    }
    else {
        foreach ($item in $Value) {
            if (-not (Test-SourceQueryValueRewritable -Value $item -Depth ($Depth + 1))) {
                return $false
            }
        }
    }

    return $true
}

# ConvertFrom-Json keeps only the last of repeated property names, so rebuilding a value that
# repeats a name would silently drop the other values.
function Test-JsonElementRepeatedName {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Element
    )

    $pending = [Collections.Generic.Stack[System.Text.Json.JsonElement]]::new()
    $pending.Push($Element)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        if ($current.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($property in $current.EnumerateObject()) {
                if (-not $names.Add($property.Name)) {
                    return $true
                }

                if ($property.Value.ValueKind -in @([System.Text.Json.JsonValueKind]::Object, [System.Text.Json.JsonValueKind]::Array)) {
                    $pending.Push($property.Value)
                }
            }
        }
        elseif ($current.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
            foreach ($item in $current.EnumerateArray()) {
                if ($item.ValueKind -in @([System.Text.Json.JsonValueKind]::Object, [System.Text.Json.JsonValueKind]::Array)) {
                    $pending.Push($item)
                }
            }
        }
    }

    return $false
}

# For a query without exact part text. True when a property name repeats, or when the query uses
# syntax System.Text.Json cannot read even leniently, so repeats cannot be ruled out.
function Test-SourceQueryPossibleRepeatedName {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    $document = $null
    try {
        $documentOptions = [System.Text.Json.JsonDocumentOptions]::new()
        $documentOptions.MaxDepth = 1024
        $documentOptions.AllowTrailingCommas = $true
        $documentOptions.CommentHandling = [System.Text.Json.JsonCommentHandling]::Skip
        $document = [System.Text.Json.JsonDocument]::Parse($Query, $documentOptions)
        return Test-JsonElementRepeatedName -Element $document.RootElement
    }
    catch {
        return $true
    }
    finally {
        if ($null -ne $document) {
            $document.Dispose()
        }
    }
}

function Get-SourceQueryRawParts {
    [CmdletBinding()]
    [OutputType([object[]])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    $document = $null
    try {
        # Matches ConvertFrom-Json's default depth so both readers see the same parts.
        $documentOptions = [System.Text.Json.JsonDocumentOptions]::new()
        $documentOptions.MaxDepth = 1024
        $document = [System.Text.Json.JsonDocument]::Parse($Query, $documentOptions)
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
                HasRepeatedNames = Test-JsonElementRepeatedName -Element $part
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
            UnconvertedSqlParts = 0
            UnrewritableParts = 0
            SqlPartsOutsideSchema = 0
            KeptAsStoredReason = 'None'
        }
    }

    try {
        $currentQuery = @(ConvertFrom-SourceQueryJson -Query $Query)
    }
    catch {
        return [PSCustomObject]@{
            Status = 'NoChanges'
            TransformedQuery = $Query
            Category = 'None'
            UnconvertedSqlParts = 0
            UnrewritableParts = 0
            SqlPartsOutsideSchema = 0
            KeptAsStoredReason = 'UnreadableJson'
        }
    }

    $rawParts = @(Get-SourceQueryRawParts -Query $Query)
    $hasRawParts = $rawParts.Count -eq $currentQuery.Count

    if (-not $hasRawParts -and (Test-SourceQueryPossibleRepeatedName -Query $Query)) {
        return [PSCustomObject]@{
            Status = 'NoChanges'
            TransformedQuery = $Query
            Category = 'None'
            UnconvertedSqlParts = 0
            UnrewritableParts = 0
            SqlPartsOutsideSchema = 0
            KeptAsStoredReason = 'PossibleRepeatedNames'
        }
    }

    $newQueryParts = [Collections.Generic.List[string]]::new()
    $rewrittenParts = 0
    $fromOldFormat = $false
    $fromUnknownFormat = $false
    $fromNewFormat = $false
    $unconvertedSqlParts = 0
    $unrewritableParts = 0
    $sqlPartsOutsideSchema = 0
    $keptAsStoredReason = 'None'
    $knownTypes = @('SqlMembership', 'GroupMembership')

    for ($partIndex = 0; $partIndex -lt $currentQuery.Count; $partIndex++) {
        $part = $currentQuery[$partIndex]
        $rawText = if ($hasRawParts) { $rawParts[$partIndex].RawText } else { $null }

        if ($hasRawParts -and $rawParts[$partIndex].Preserve) {
            $newQueryParts.Add($rawText)
            if ($rawParts[$partIndex].HasRepeatedNames) {
                $unrewritableParts++
            }

            if (-not (Test-CurrentSqlSourceShape -Source $part.source)) {
                $sqlPartsOutsideSchema++
            }

            $fromNewFormat = $true
            continue
        }

        if (($hasRawParts -and $rawParts[$partIndex].HasRepeatedNames) -or
            -not (Test-SourceQueryValueRewritable -Value $part)) {
            $unrewritableParts++
            if ($part.type -eq 'SqlMembership' -and -not (Test-CurrentSqlSourceShape -Source $part.source)) {
                $sqlPartsOutsideSchema++
            }

            if ($hasRawParts) {
                $newQueryParts.Add($rawText)
            }
            elseif ($keptAsStoredReason -eq 'None') {
                $keptAsStoredReason = 'UnrewritablePart'
            }

            continue
        }

        $partType = if ($part.type -eq 'SecurityGroup') { 'GroupMembership' } else { $part.type }

        if ($partType -eq 'GroupMembership' -and $part.source) {
            if ($part.type -ceq 'GroupMembership' -and $part.source -isnot [array]) {
                $newQueryParts.Add($(if ($hasRawParts) { $rawText } else { ConvertTo-Json -InputObject $part -Compress -Depth 100 }))
                continue
            }

            foreach ($id in $part.source) {
                if ($part.exclusionary -eq $true) {
                    $newQueryParts.Add('{"type":"GroupMembership","source":"' + $id + '","exclusionary":true}')
                }
                else {
                    $newQueryParts.Add('{"type":"GroupMembership","source":"' + $id + '"}')
                }
            }

            $rewrittenParts++
            $fromOldFormat = $true
            continue
        }

        if ($partType -eq 'SqlMembership') {
            $sources = @($part.source)
            $convertible = $sources.Count -gt 0
            foreach ($source in $sources) {
                if (-not (Test-ConvertibleSqlSource -Source $source)) {
                    $convertible = $false
                    break
                }
            }

            if (-not $convertible) {
                $unconvertedSqlParts++
                if (-not (Test-CurrentSqlSourceShape -Source $part.source)) {
                    $sqlPartsOutsideSchema++
                }

                if ($hasRawParts) {
                    $newQueryParts.Add($rawText)
                }
                elseif ($keptAsStoredReason -eq 'None') {
                    # Without the exact original text the part cannot be kept byte-for-byte,
                    # so the whole query is left as stored.
                    $keptAsStoredReason = 'UnrecoverableSqlPart'
                }

                continue
            }

            $builtParts = [Collections.Generic.List[string]]::new()
            foreach ($source in $sources) {
                if ($source.manager.id) {
                    $builtParts.Add(
                        (Build-SqlQueryPart -OriginalPart $part -NewSource (Copy-SqlSourceInCurrentFormat -Source $source)))
                }
                elseif ($source.ids) {
                    foreach ($id in $source.ids) {
                        $expandedSource = [ordered]@{
                            manager = [ordered]@{
                                id = $id
                            }
                        }

                        if ($source.depth -ge 1) {
                            $expandedSource.manager.depth = $source.depth
                        }

                        if ($source.filter) {
                            $expandedSource.filter = Convert-SqlFilterNotEqualOperator -Filter $source.filter
                        }

                        $builtParts.Add(
                            (Build-SqlQueryPart -OriginalPart $part -NewSource $expandedSource))
                    }
                }
                elseif ($source.filter) {
                    $builtParts.Add(
                        (Build-SqlQueryPart -OriginalPart $part -NewSource (Copy-SqlSourceInCurrentFormat -Source $source)))
                }
            }

            foreach ($builtPart in $builtParts) {
                if (-not (Test-CurrentSqlSourceShape -Source (ConvertFrom-SourceQueryJson -Query $builtPart).source)) {
                    $sqlPartsOutsideSchema++
                }
            }

            $unchanged = $builtParts.Count -eq 1 -and [string]::Equals(
                $builtParts[0],
                (ConvertTo-Json -InputObject $part -Compress -Depth 100),
                [StringComparison]::Ordinal)
            if ($unchanged) {
                $newQueryParts.Add($(if ($hasRawParts) { $rawText } else { $builtParts[0] }))
                continue
            }

            $newQueryParts.AddRange($builtParts)
            $rewrittenParts++
            $fromOldFormat = $true
            continue
        }

        $changed = $false
        if ($part.type -eq 'SecurityGroup') {
            $part.type = 'GroupMembership'
            $changed = $true
        }
        elseif ($part.type -eq 'GroupOwnership' -and $part.source -is [array]) {
            for ($sourceIndex = 0; $sourceIndex -lt $part.source.Count; $sourceIndex++) {
                $sourceType = $part.source[$sourceIndex]
                if ($sourceType -is [string] -and $sourceType.Trim() -eq 'SecurityGroup') {
                    $part.source[$sourceIndex] = 'GroupMembership'
                    $changed = $true
                }
            }
        }

        if (-not $changed) {
            $newQueryParts.Add($(if ($hasRawParts) { $rawText } else { ConvertTo-Json -InputObject $part -Compress -Depth 100 }))
            continue
        }

        $newQueryParts.Add((ConvertTo-Json -InputObject $part -Compress -Depth 100))
        $rewrittenParts++

        if ($partType -notin $knownTypes) {
            $fromUnknownFormat = $true
        }
        else {
            $fromNewFormat = $true
        }
    }

    $newQuery = if ($keptAsStoredReason -ne 'None' -or $rewrittenParts -eq 0) { $Query } else { '[' + ($newQueryParts -join ',') + ']' }
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
        UnconvertedSqlParts = $unconvertedSqlParts
        UnrewritableParts = $unrewritableParts
        SqlPartsOutsideSchema = $sqlPartsOutsideSchema
        KeptAsStoredReason = $keptAsStoredReason
    }
}

function New-SourceQueryMigrationConnection {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions',
        '',
        Justification = 'Creates an unopened in-memory connection.')]
    [CmdletBinding()]
    [OutputType([System.Data.SqlClient.SqlConnection])]
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
        $queriesWithUnconvertedSqlParts = 0
        $queriesKeptWholeForUnconvertedSqlParts = 0
        $queriesWithUnrewritableParts = 0
        $queriesKeptWholeForUnrewritableParts = 0
        $queriesWithSqlPartsOutsideSchema = 0
        $queriesKeptWholeForRepeatedNames = 0
        $queriesNotReadable = 0
        $pendingUpdates = [Collections.Generic.List[object]]::new()

        foreach ($row in $dataTable) {
            if ([string]::IsNullOrEmpty($row['Query'])) {
                continue
            }

            $originalQuery = [string]$row['Query']
            $converted = Convert-SqlSourceQuery -Query $originalQuery
            if ($converted.KeptAsStoredReason -eq 'UnreadableJson') {
                $queriesNotReadable++
                Write-Host "Job $($row['Id']): left the query unchanged because it is not valid JSON."
            }
            elseif ($converted.KeptAsStoredReason -eq 'PossibleRepeatedNames') {
                $queriesKeptWholeForRepeatedNames++
                Write-Host "Job $($row['Id']): left the whole query unchanged because it may repeat a property name, and rewriting it could lose one of the values."
            }
            else {
                if ($converted.KeptAsStoredReason -eq 'UnrecoverableSqlPart') {
                    $queriesKeptWholeForUnconvertedSqlParts++
                    Write-Host "Job $($row['Id']): left the whole query unchanged because a SQL part in an unrecognised format could not be kept exactly as stored."
                }
                elseif ($converted.KeptAsStoredReason -eq 'UnrewritablePart') {
                    $queriesKeptWholeForUnrewritableParts++
                    Write-Host "Job $($row['Id']): left the whole query unchanged because a part whose values cannot be written back exactly could not be kept exactly as stored."
                }

                if ($converted.UnconvertedSqlParts -gt 0) {
                    $queriesWithUnconvertedSqlParts++
                    Write-Host "Job $($row['Id']): left $($converted.UnconvertedSqlParts) SQL part(s) unchanged because their format is not recognised."
                }

                if ($converted.UnrewritableParts -gt 0) {
                    $queriesWithUnrewritableParts++
                    Write-Host "Job $($row['Id']): left $($converted.UnrewritableParts) part(s) unchanged because their values cannot be written back exactly."
                }

                if ($converted.SqlPartsOutsideSchema -gt 0) {
                    $queriesWithSqlPartsOutsideSchema++
                    Write-Host "Job $($row['Id']): $($converted.SqlPartsOutsideSchema) SQL part(s) do not match the current schema, so the job may stop with a schema error."
                }
            }

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
        Write-Host "Queries with SQL parts left unchanged because their format is not recognised: $queriesWithUnconvertedSqlParts"
        Write-Host "Queries left whole because such a SQL part could not be kept exactly as stored: $queriesKeptWholeForUnconvertedSqlParts"
        Write-Host "Queries with parts left unchanged because their values cannot be written back exactly: $queriesWithUnrewritableParts"
        Write-Host "Queries left whole because such a part could not be kept exactly as stored: $queriesKeptWholeForUnrewritableParts"
        Write-Host "Queries with SQL parts that do not match the current schema and may stop with a schema error: $queriesWithSqlPartsOutsideSchema"
        Write-Host "Queries left whole because they may repeat a property name: $queriesKeptWholeForRepeatedNames"
        Write-Host "Queries left unchanged because they are not valid JSON: $queriesNotReadable"
    }
    finally {
        $connection.Dispose()
        Write-Host 'Finish Set-UpdateSourceQuery'
    }
}
