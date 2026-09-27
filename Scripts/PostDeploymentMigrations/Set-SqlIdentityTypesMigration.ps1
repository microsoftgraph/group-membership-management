# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.

$script:SqlIdentityTypesTransformVersion = '1.0.0'
$script:SqlMembershipDiscriminator = 'SqlMembership'
$script:IdentityTypesProperty = 'identityTypes'
$script:DefaultSelectionFragment = ',"identityTypes":["users"]'

function Assert-JsonFullyDecodable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonDocument]$Document
    )

    $stream = $null
    $writer = $null

    try {
        $stream = [IO.MemoryStream]::new()
        $writer = [System.Text.Json.Utf8JsonWriter]::new($stream)
        $Document.WriteTo($writer)
        $writer.Flush()
    }
    finally {
        if ($null -ne $writer) {
            $writer.Dispose()
        }

        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Test-SqlIdentityTypesSelection {
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

function Test-JsonContainsIdentityTypesName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Element
    )

    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        foreach ($property in $Element.EnumerateObject()) {
            if ([string]::Equals($property.Name, $script:IdentityTypesProperty, [StringComparison]::OrdinalIgnoreCase) -or
                (Test-JsonContainsIdentityTypesName -Element $property.Value)) {
                return $true
            }
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) {
            if (Test-JsonContainsIdentityTypesName -Element $item) {
                return $true
            }
        }
    }

    return $false
}

function Test-SqlIdentityTypesPositiveInteger {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Value
    )

    $number = 0
    return $Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Number -and
        $Value.TryGetInt32([ref]$number) -and
        $number -ge 1
}

function Test-SqlIdentityTypesManager {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Manager
    )

    if ($Manager.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        return $false
    }

    $idCount = 0
    $depthCount = 0

    foreach ($property in $Manager.EnumerateObject()) {
        if ($property.NameEquals('id')) {
            $idCount++
        }
        elseif ($property.NameEquals('depth')) {
            $depthCount++
        }
        else {
            return $false
        }

        if (-not (Test-SqlIdentityTypesPositiveInteger -Value $property.Value)) {
            return $false
        }
    }

    return $idCount -eq 1 -and $depthCount -le 1
}

# Accepts only sources the application's SQL membership schema accepts. Where this is stricter than
# the schema, the job is skipped, which is harmless because a missing selection already means users.
function Test-SqlIdentityTypesSource {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Source
    )

    if ($Source.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        return $false
    }

    $managerCount = 0
    $filterCount = 0

    foreach ($property in $Source.EnumerateObject()) {
        if ($property.NameEquals('manager')) {
            $managerCount++
            if (-not (Test-SqlIdentityTypesManager -Manager $property.Value)) {
                return $false
            }
        }
        elseif ($property.NameEquals('filter')) {
            $filterCount++
            if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                return $false
            }
        }
        else {
            return $false
        }
    }

    return ($managerCount + $filterCount) -gt 0 -and $managerCount -le 1 -and $filterCount -le 1
}

function Get-SqlIdentityTypesPartAnalysis {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Part
    )

    # Ignored: a part the migration cannot recognise and leaves untouched. Other: an ordinary non-SQL part.
    $analysis = [ordered]@{
        Kind                     = 'Ignored'
        ReasonCode               = ''
        HasSelection             = $false
        EndsWithDefaultSelection = $false
    }

    if ($Part.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        return [PSCustomObject]$analysis
    }

    $typeCount = 0
    $stringTypeCount = 0
    $sqlTypeCount = 0
    $sourceCount = 0
    $exclusionaryCount = 0
    $selectionCount = 0
    $hasWrongCaseSelection = $false
    $hasNestedSelection = $false
    $hasUnsupportedProperty = $false
    $sourceProperty = [System.Text.Json.JsonElement]::new()
    $exclusionaryProperty = [System.Text.Json.JsonElement]::new()
    $selectionProperty = [System.Text.Json.JsonElement]::new()

    foreach ($property in $Part.EnumerateObject()) {
        if ($property.NameEquals('type')) {
            $typeCount++
            if ($property.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
                $stringTypeCount++
                if ([string]::Equals($property.Value.GetString(), $script:SqlMembershipDiscriminator, [StringComparison]::Ordinal)) {
                    $sqlTypeCount++
                }
            }
        }
        elseif ($property.NameEquals($script:IdentityTypesProperty)) {
            $selectionCount++
            $selectionProperty = $property.Value
        }
        elseif ([string]::Equals($property.Name, $script:IdentityTypesProperty, [StringComparison]::OrdinalIgnoreCase)) {
            $hasWrongCaseSelection = $true
        }
        else {
            if ($property.NameEquals('source')) {
                $sourceCount++
                $sourceProperty = $property.Value
            }
            elseif ($property.NameEquals('exclusionary')) {
                $exclusionaryCount++
                $exclusionaryProperty = $property.Value
            }
            else {
                $hasUnsupportedProperty = $true
            }

            if (Test-JsonContainsIdentityTypesName -Element $property.Value) {
                $hasNestedSelection = $true
            }
        }
    }

    if ($sqlTypeCount -eq 0) {
        if ($typeCount -eq 1 -and $stringTypeCount -eq 1) {
            $analysis.Kind = 'Other'
        }

        return [PSCustomObject]$analysis
    }

    $analysis.Kind = 'BrokenSql'

    if ($typeCount -ne 1) {
        $analysis.ReasonCode = 'DuplicateType'
        return [PSCustomObject]$analysis
    }

    # A selection the schema does not recognise may still be a deliberate choice, so it must not be replaced by the users-only default.
    if ($hasWrongCaseSelection) {
        $analysis.ReasonCode = 'WrongCaseIdentityTypes'
        return [PSCustomObject]$analysis
    }

    if ($hasNestedSelection) {
        $analysis.ReasonCode = 'MisplacedIdentityTypes'
        return [PSCustomObject]$analysis
    }

    if ($selectionCount -gt 1 -or
        ($selectionCount -eq 1 -and -not (Test-SqlIdentityTypesSelection -Selection $selectionProperty))) {
        $analysis.ReasonCode = 'InvalidIdentityTypes'
        return [PSCustomObject]$analysis
    }

    if ($sourceCount -ne 1 -or -not (Test-SqlIdentityTypesSource -Source $sourceProperty)) {
        $analysis.ReasonCode = 'InvalidSource'
        return [PSCustomObject]$analysis
    }

    if ($exclusionaryCount -gt 1 -or
        ($exclusionaryCount -eq 1 -and
            $exclusionaryProperty.ValueKind -ne [System.Text.Json.JsonValueKind]::True -and
            $exclusionaryProperty.ValueKind -ne [System.Text.Json.JsonValueKind]::False)) {
        $analysis.ReasonCode = 'InvalidExclusionary'
        return [PSCustomObject]$analysis
    }

    if ($hasUnsupportedProperty) {
        $analysis.ReasonCode = 'UnsupportedProperty'
        return [PSCustomObject]$analysis
    }

    $analysis.Kind = 'Sql'
    $analysis.HasSelection = $selectionCount -eq 1
    if ($analysis.HasSelection) {
        $analysis.EndsWithDefaultSelection = $Part.GetRawText().EndsWith(
            $script:DefaultSelectionFragment + '}',
            [StringComparison]::Ordinal)
    }

    return [PSCustomObject]$analysis
}

function Get-SqlIdentityTypesQueryAnalysis {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    $analysis = [ordered]@{
        Status     = 'Valid'
        ReasonCode = ''
        Parts      = @()
    }

    $document = $null
    try {
        try {
            $document = [System.Text.Json.JsonDocument]::Parse($Query)
        }
        catch {
            $analysis.Status = 'Malformed'
            $analysis.ReasonCode = 'InvalidJson'
            return [PSCustomObject]$analysis
        }

        $root = $document.RootElement
        if ($root.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
            $analysis.Status = 'Malformed'
            $analysis.ReasonCode = 'RootNotArray'
            return [PSCustomObject]$analysis
        }

        Assert-JsonFullyDecodable -Document $document

        $parts = [Collections.Generic.List[object]]::new()
        foreach ($part in $root.EnumerateArray()) {
            $partAnalysis = Get-SqlIdentityTypesPartAnalysis -Part $part
            if ($partAnalysis.Kind -eq 'BrokenSql') {
                $analysis.Status = 'Invalid'
                $analysis.ReasonCode = $partAnalysis.ReasonCode
                return [PSCustomObject]$analysis
            }

            $parts.Add($partAnalysis)
        }

        $analysis.Parts = $parts.ToArray()
        return [PSCustomObject]$analysis
    }
    catch {
        $underlying = $_.Exception
        while ($underlying -is [Management.Automation.MethodInvocationException] -and
            $null -ne $underlying.InnerException) {
            $underlying = $underlying.InnerException
        }

        if ($underlying -isnot [System.Text.Json.JsonException] -and
            $underlying -isnot [InvalidOperationException]) {
            throw
        }

        $analysis.Status = 'Malformed'
        $analysis.ReasonCode = 'UndecodableText'
        $analysis.Parts = @()
        return [PSCustomObject]$analysis
    }
    finally {
        if ($null -ne $document) {
            $document.Dispose()
        }
    }
}

function Get-SqlIdentityTypesPartCloseIndexMap {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    # Maps each root-level object part to the position of its closing brace. Arrays and objects share
    # one depth counter so that nested arrays and non-object parts cannot shift the part numbering.
    $closeIndexes = [Collections.Generic.Dictionary[int, int]]::new()
    $depth = 0
    $partIndex = 0
    $inString = $false
    $escaped = $false

    for ($index = 0; $index -lt $Query.Length; $index++) {
        $character = $Query[$index]

        if ($inString) {
            if ($escaped) {
                $escaped = $false
            }
            elseif ($character -eq '\') {
                $escaped = $true
            }
            elseif ($character -eq '"') {
                $inString = $false
            }

            continue
        }

        switch ($character) {
            '"' {
                $inString = $true
            }
            '[' {
                $depth++
            }
            '{' {
                $depth++
            }
            ']' {
                $depth--
            }
            '}' {
                if ($depth -eq 2) {
                    $closeIndexes[$partIndex] = $index
                }

                $depth--
            }
            ',' {
                if ($depth -eq 1) {
                    $partIndex++
                }
            }
        }
    }

    if ($depth -ne 0 -or $inString) {
        return $null
    }

    return $closeIndexes
}

function Test-SqlIdentityTypesPartEdit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OriginalQuery,

        [Parameter(Mandatory = $true)]
        [string]$EditedQuery,

        [Parameter(Mandatory = $true)]
        [Collections.Generic.HashSet[int]]$PartIndexes,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Add', 'Remove')]
        [string]$Edit
    )

    $originalDocument = $null
    $editedDocument = $null

    try {
        $originalDocument = [System.Text.Json.JsonDocument]::Parse($OriginalQuery)
        $editedDocument = [System.Text.Json.JsonDocument]::Parse($EditedQuery)
        Assert-JsonFullyDecodable -Document $editedDocument

        if ($editedDocument.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
            return $false
        }

        $originalParts = [Collections.Generic.List[string]]::new()
        foreach ($part in $originalDocument.RootElement.EnumerateArray()) {
            $originalParts.Add($part.GetRawText())
        }

        $editedParts = [Collections.Generic.List[string]]::new()
        foreach ($part in $editedDocument.RootElement.EnumerateArray()) {
            $editedParts.Add($part.GetRawText())
        }

        if ($editedParts.Count -ne $originalParts.Count) {
            return $false
        }

        foreach ($partIndex in $PartIndexes) {
            if ($partIndex -lt 0 -or $partIndex -ge $originalParts.Count) {
                return $false
            }
        }

        $selectionSuffix = $script:DefaultSelectionFragment + '}'
        for ($partIndex = 0; $partIndex -lt $originalParts.Count; $partIndex++) {
            $expected = $originalParts[$partIndex]

            if ($PartIndexes.Contains($partIndex)) {
                if ($Edit -eq 'Add') {
                    if (-not $expected.EndsWith('}', [StringComparison]::Ordinal)) {
                        return $false
                    }

                    $expected = $expected.Substring(0, $expected.Length - 1) + $selectionSuffix
                }
                else {
                    if (-not $expected.EndsWith($selectionSuffix, [StringComparison]::Ordinal)) {
                        return $false
                    }

                    $expected = $expected.Substring(0, $expected.Length - $selectionSuffix.Length) + '}'
                }
            }

            if (-not [string]::Equals($expected, $editedParts[$partIndex], [StringComparison]::Ordinal)) {
                return $false
            }
        }

        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $editedDocument) {
            $editedDocument.Dispose()
        }

        if ($null -ne $originalDocument) {
            $originalDocument.Dispose()
        }
    }
}

function Add-SqlIdentityTypesSelection {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query,

        [Parameter(Mandatory = $true)]
        [int[]]$PartIndexes
    )

    $closeIndexes = Get-SqlIdentityTypesPartCloseIndexMap -Query $Query
    if ($null -eq $closeIndexes) {
        return $null
    }

    $targets = [Collections.Generic.HashSet[int]]::new()
    $insertionPoints = [Collections.Generic.List[int]]::new()
    foreach ($partIndex in $PartIndexes) {
        $closeIndex = 0
        if (-not $targets.Add($partIndex) -or
            -not $closeIndexes.TryGetValue($partIndex, [ref]$closeIndex)) {
            return $null
        }

        $insertionPoints.Add($closeIndex)
    }

    $insertionPoints.Sort()
    $builder = [Text.StringBuilder]::new($Query.Length + ($insertionPoints.Count * $script:DefaultSelectionFragment.Length))
    $copiedUpTo = 0
    foreach ($closeIndex in $insertionPoints) {
        [void]$builder.Append($Query, $copiedUpTo, $closeIndex - $copiedUpTo)
        [void]$builder.Append($script:DefaultSelectionFragment)
        $copiedUpTo = $closeIndex
    }

    [void]$builder.Append($Query, $copiedUpTo, $Query.Length - $copiedUpTo)
    $edited = $builder.ToString()

    if (-not (Test-SqlIdentityTypesPartEdit -OriginalQuery $Query -EditedQuery $edited -PartIndexes $targets -Edit Add)) {
        return $null
    }

    return $edited
}

function Convert-SqlIdentityTypesQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Query
    )

    $result = [ordered]@{
        Status           = 'NoChanges'
        OriginalQuery    = $Query
        TransformedQuery = $Query
        ChangedParts     = @()
        IgnoredParts     = 0
        ReasonCode       = ''
        TransformVersion = $script:SqlIdentityTypesTransformVersion
    }

    if ($Query.Length -eq 0) {
        return [PSCustomObject]$result
    }

    $analysis = Get-SqlIdentityTypesQueryAnalysis -Query $Query
    if ($analysis.Status -ne 'Valid') {
        $result.Status = $analysis.Status
        $result.ReasonCode = $analysis.ReasonCode
        return [PSCustomObject]$result
    }

    $partsNeedingSelection = [Collections.Generic.List[int]]::new()
    for ($partIndex = 0; $partIndex -lt $analysis.Parts.Count; $partIndex++) {
        $part = $analysis.Parts[$partIndex]
        if ($part.Kind -eq 'Ignored') {
            $result.IgnoredParts++
        }
        elseif ($part.Kind -eq 'Sql' -and -not $part.HasSelection) {
            $partsNeedingSelection.Add($partIndex)
        }
    }

    if ($partsNeedingSelection.Count -eq 0) {
        return [PSCustomObject]$result
    }

    $spliced = Add-SqlIdentityTypesSelection -Query $Query -PartIndexes $partsNeedingSelection
    if ($null -eq $spliced) {
        $result.Status = 'Malformed'
        $result.ReasonCode = 'SpliceFailed'
        return [PSCustomObject]$result
    }

    $result.Status = 'Changed'
    $result.TransformedQuery = $spliced
    $result.ChangedParts = $partsNeedingSelection.ToArray()
    return [PSCustomObject]$result
}

function Get-SqlIdentityTypesMigrationPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [Collections.IEnumerable]$Rows
    )

    $changes = [Collections.Generic.List[object]]::new()
    $skipped = [Collections.Generic.List[object]]::new()
    $ignoredPartJobs = [Collections.Generic.List[object]]::new()
    $jobsInspected = 0

    foreach ($row in $Rows) {
        $jobsInspected++
        $transformed = Convert-SqlIdentityTypesQuery -Query ([string]$row.Query)

        if ($transformed.Status -in @('Malformed', 'Invalid')) {
            $skipped.Add([PSCustomObject]@{
                Id = [Guid]$row.Id
                ReasonCode = $transformed.ReasonCode
            })

            continue
        }

        if ($transformed.Status -eq 'Changed') {
            $changes.Add([PSCustomObject]@{
                Id = [Guid]$row.Id
                BeforeQuery = [string]$row.Query
                AfterQuery = $transformed.TransformedQuery
                ChangedParts = $transformed.ChangedParts
            })

            if ($transformed.IgnoredParts -gt 0) {
                $ignoredPartJobs.Add([PSCustomObject]@{
                    Id = [Guid]$row.Id
                    IgnoredParts = $transformed.IgnoredParts
                })
            }
        }
    }

    $status = if ($changes.Count -gt 0) { 'Ready' } else { 'NoChanges' }

    return [PSCustomObject]@{
        Status = $status
        JobsInspected = $jobsInspected
        JobsToChange = $changes.Count
        JobsSkipped = $skipped.Count
        JobsWithIgnoredParts = $ignoredPartJobs.Count
        Skipped = $skipped.ToArray()
        IgnoredPartJobs = $ignoredPartJobs.ToArray()
        Changes = $changes.ToArray()
    }
}

function Write-SqlIdentityTypesSkipSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Skipped,

        [Parameter(Mandatory = $true)]
        [string]$Activity
    )

    foreach ($skippedJob in $Skipped) {
        Write-Warning "SQL identity $Activity skipped job $($skippedJob.Id) ($($skippedJob.ReasonCode)); its query was left unchanged."
    }
}

function Write-SqlIdentityTypesMigrationPlanWarning {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Plan,

        [Parameter(Mandatory = $true)]
        [Collections.Generic.Dictionary[Guid, string]]$ReportedSkips,

        [Parameter(Mandatory = $true)]
        [Collections.Generic.Dictionary[Guid, int]]$ReportedIgnoredParts
    )

    # Each job is reported once; a retry reports only what its new read changed.
    $currentSkips = [Collections.Generic.HashSet[Guid]]::new()
    $unreportedSkips = [Collections.Generic.List[object]]::new()
    foreach ($skippedJob in $Plan.Skipped) {
        $jobId = [Guid]$skippedJob.Id
        [void]$currentSkips.Add($jobId)
        $reportedReason = $null
        if (-not $ReportedSkips.TryGetValue($jobId, [ref]$reportedReason) -or $reportedReason -ne $skippedJob.ReasonCode) {
            $ReportedSkips[$jobId] = $skippedJob.ReasonCode
            $unreportedSkips.Add($skippedJob)
        }
    }

    Write-SqlIdentityTypesSkipSummary -Skipped $unreportedSkips.ToArray() -Activity 'migration'
    foreach ($jobId in @($ReportedSkips.Keys)) {
        if (-not $currentSkips.Contains($jobId)) {
            [void]$ReportedSkips.Remove($jobId)
            Write-Warning "SQL identity migration read job $jobId again after a concurrent change; its earlier skip warning no longer applies."
        }
    }

    $currentIgnoredParts = [Collections.Generic.HashSet[Guid]]::new()
    foreach ($ignoredPartJob in $Plan.IgnoredPartJobs) {
        $jobId = [Guid]$ignoredPartJob.Id
        [void]$currentIgnoredParts.Add($jobId)
        $reportedCount = 0
        if (-not $ReportedIgnoredParts.TryGetValue($jobId, [ref]$reportedCount) -or $reportedCount -ne $ignoredPartJob.IgnoredParts) {
            $ReportedIgnoredParts[$jobId] = $ignoredPartJob.IgnoredParts
            Write-Warning "SQL identity migration leaves $($ignoredPartJob.IgnoredParts) unrecognisable part(s) unchanged in job $jobId; they do not stop its SQL parts from gaining identityTypes."
        }
    }

    foreach ($jobId in @($ReportedIgnoredParts.Keys)) {
        if (-not $currentIgnoredParts.Contains($jobId)) {
            [void]$ReportedIgnoredParts.Remove($jobId)
            Write-Warning "SQL identity migration read job $jobId again after a concurrent change; its earlier warning about unrecognisable parts no longer applies."
        }
    }
}

function New-SqlIdentityTypesMigrationConnection {
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

function Invoke-SqlIdentityTypesOperation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Operation,

        [Parameter(Mandatory = $false)]
        [ValidateRange(1, 10)]
        [int]$MaxRetries = 10
    )

    $payloadMarker = [Guid]::NewGuid().ToString('N')
    $wrappedOperation = {
        $value = & $Operation
        return [PSCustomObject]@{
            SqlIdentityTypesPayload = $payloadMarker
            Value = $value
        }
    }.GetNewClosure()

    $outputs = @(
        Invoke-SqlOperationWithFirewallRetry `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SolutionAbbreviation $SolutionAbbreviation `
            -MaxRetries $MaxRetries `
            -Operation $wrappedOperation
    )
    $payloads = @(
        $outputs |
            Where-Object {
                $_.PSObject.Properties['SqlIdentityTypesPayload'] -and
                $_.SqlIdentityTypesPayload -ceq $payloadMarker
            }
    )

    if ($payloads.Count -ne 1) {
        throw 'The SQL migration operation did not return exactly one result payload.'
    }

    return $payloads[0].Value
}

function Get-SqlIdentityTypesMigrationRows {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [Data.SqlClient.SqlConnection]$Connection
    )

    $reader = $null
    $tableCommand = $null
    $readCommand = $null

    try {
        $Connection.Open()

        $tableCommand = $Connection.CreateCommand()
        $tableCommand.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.SyncJobs', N'U') IS NULL THEN 0 ELSE 1 END;"
        if ([int]$tableCommand.ExecuteScalar() -ne 1) {
            throw 'The existing deployment does not contain dbo.SyncJobs.'
        }

        $readCommand = $Connection.CreateCommand()
        $readCommand.CommandTimeout = 600
        $readCommand.CommandText = @'
SELECT Id, [Query]
FROM dbo.SyncJobs
WHERE [Query] IS NOT NULL
  AND DATALENGTH([Query]) > 0
ORDER BY Id;
'@

        $rows = [Collections.Generic.List[object]]::new()
        $reader = $readCommand.ExecuteReader()
        while ($reader.Read()) {
            $rows.Add([PSCustomObject]@{
                Id = $reader.GetGuid(0)
                Query = $reader.GetString(1)
            })
        }

        return , $rows
    }
    finally {
        if ($null -ne $reader -and -not $reader.IsClosed) {
            $reader.Close()
        }

        if ($null -ne $readCommand) {
            $readCommand.Dispose()
        }

        if ($null -ne $tableCommand) {
            $tableCommand.Dispose()
        }

        if ($Connection.State -ne [Data.ConnectionState]::Closed) {
            $Connection.Close()
        }
    }
}

function Add-SqlIdentityTypesPendingTable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [Data.SqlClient.SqlConnection]$Connection,

        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [Data.SqlClient.SqlTransaction]$Transaction,

        [Parameter(Mandatory = $true)]
        [object[]]$Changes
    )

    $createCommand = $Connection.CreateCommand()
    $createCommand.Transaction = $Transaction
    $createCommand.CommandText = @'
CREATE TABLE #PendingIdentityTypes
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    BeforeQuery nvarchar(max) NOT NULL,
    AfterQuery nvarchar(max) NOT NULL
);
'@

    try {
        [void]$createCommand.ExecuteNonQuery()
    }
    finally {
        $createCommand.Dispose()
    }

    $staging = [Data.DataTable]::new()
    [void]$staging.Columns.Add('Id', [Guid])
    [void]$staging.Columns.Add('BeforeQuery', [string])
    [void]$staging.Columns.Add('AfterQuery', [string])

    foreach ($change in $Changes) {
        [void]$staging.Rows.Add($change.Id, $change.BeforeQuery, $change.AfterQuery)
    }

    $bulkCopy = [Data.SqlClient.SqlBulkCopy]::new(
        $Connection,
        [Data.SqlClient.SqlBulkCopyOptions]::Default,
        $Transaction)

    try {
        $bulkCopy.DestinationTableName = '#PendingIdentityTypes'
        $bulkCopy.BatchSize = 5000
        $bulkCopy.BulkCopyTimeout = 600
        [void]$bulkCopy.ColumnMappings.Add('Id', 'Id')
        [void]$bulkCopy.ColumnMappings.Add('BeforeQuery', 'BeforeQuery')
        [void]$bulkCopy.ColumnMappings.Add('AfterQuery', 'AfterQuery')
        $bulkCopy.WriteToServer($staging)
    }
    finally {
        $bulkCopy.Close()
        $bulkCopy.Dispose()
        $staging.Dispose()
    }
}

function Invoke-SqlIdentityTypesMigrationTransaction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [Data.SqlClient.SqlConnection]$Connection,

        [Parameter(Mandatory = $true)]
        [object[]]$Changes
    )

    $transaction = $null
    $resolved = $false

    try {
        $Connection.Open()
        $transaction = $Connection.BeginTransaction([Data.IsolationLevel]::Serializable)

        $statusCommand = $Connection.CreateCommand()
        $statusCommand.Transaction = $transaction
        $statusCommand.CommandTimeout = 120
        $statusCommand.CommandText = @'
SELECT statuses.Name
FROM
(
    SELECT TOP (1) history.ServiceStatusId
    FROM dbo.ServiceStatusHistory AS history
    ORDER BY history.Timestamp DESC, history.Id DESC
) AS latest
LEFT JOIN dbo.ServiceStatuses AS statuses
    ON statuses.Id = latest.ServiceStatusId;
'@

        try {
            try {
                $storedStatus = $statusCommand.ExecuteScalar()
            }
            catch {
                $transaction.Rollback()
                $resolved = $true
                return [PSCustomObject]@{
                    Outcome = 'Rejected'
                    ReasonCode = 'ServiceStatusReadFailed'
                    RowsAffected = 0
                }
            }
        }
        finally {
            $statusCommand.Dispose()
        }

        if ($null -eq $storedStatus -or
            $storedStatus -is [DBNull] -or
            -not [string]::Equals([string]$storedStatus, 'Stopped', [StringComparison]::OrdinalIgnoreCase)) {
            $transaction.Rollback()
            $resolved = $true
            return [PSCustomObject]@{
                Outcome = 'Rejected'
                ReasonCode = 'ServiceNotStopped'
                RowsAffected = 0
            }
        }

        Add-SqlIdentityTypesPendingTable -Connection $Connection -Transaction $transaction -Changes $Changes

        $updateCommand = $Connection.CreateCommand()
        $updateCommand.Transaction = $transaction
        $updateCommand.CommandTimeout = 600
        $updateCommand.CommandText = @'
UPDATE jobs
SET jobs.[Query] = pending.AfterQuery
FROM dbo.SyncJobs AS jobs
INNER JOIN #PendingIdentityTypes AS pending ON pending.Id = jobs.Id
WHERE DATALENGTH(jobs.[Query]) = DATALENGTH(pending.BeforeQuery)
  AND CONVERT(varbinary(max), jobs.[Query]) = CONVERT(varbinary(max), pending.BeforeQuery);
'@

        try {
            $rowsAffected = $updateCommand.ExecuteNonQuery()
        }
        finally {
            $updateCommand.Dispose()
        }

        if ($rowsAffected -ne $Changes.Count) {
            $transaction.Rollback()
            $resolved = $true
            return [PSCustomObject]@{
                Outcome = if ($rowsAffected -lt $Changes.Count) { 'CompareAndSwapMiss' } else { 'Failed' }
                ReasonCode = 'UnexpectedAffectedRows'
                RowsAffected = $rowsAffected
            }
        }

        $verifyCommand = $Connection.CreateCommand()
        $verifyCommand.Transaction = $transaction
        $verifyCommand.CommandTimeout = 600
        $verifyCommand.CommandText = @'
SELECT COUNT_BIG(*)
FROM #PendingIdentityTypes AS pending
INNER JOIN dbo.SyncJobs AS jobs ON jobs.Id = pending.Id
WHERE DATALENGTH(jobs.[Query]) = DATALENGTH(pending.AfterQuery)
  AND CONVERT(varbinary(max), jobs.[Query]) = CONVERT(varbinary(max), pending.AfterQuery);
'@

        try {
            $verifiedRows = [long]$verifyCommand.ExecuteScalar()
        }
        finally {
            $verifyCommand.Dispose()
        }

        if ($verifiedRows -ne $Changes.Count) {
            $transaction.Rollback()
            $resolved = $true
            return [PSCustomObject]@{
                Outcome = 'Failed'
                ReasonCode = 'TransactionReadBackFailed'
                RowsAffected = $rowsAffected
            }
        }

        $transaction.Commit()
        $resolved = $true
        return [PSCustomObject]@{
            Outcome = 'Committed'
            ReasonCode = ''
            RowsAffected = $rowsAffected
        }
    }
    finally {
        if ($null -ne $transaction) {
            if (-not $resolved) {
                try {
                    $transaction.Rollback()
                }
                catch {
                    Write-Verbose 'Rollback failed after an uncertain update outcome.'
                }
            }

            $transaction.Dispose()
        }

        if ($Connection.State -ne [Data.ConnectionState]::Closed) {
            $Connection.Close()
        }
    }
}

function Get-SqlIdentityTypesMigrationStates {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [Data.SqlClient.SqlConnection]$Connection,

        [Parameter(Mandatory = $true)]
        [object[]]$Changes
    )

    $reader = $null
    $stateCommand = $null

    try {
        $Connection.Open()
        Add-SqlIdentityTypesPendingTable -Connection $Connection -Changes $Changes

        $stateCommand = $Connection.CreateCommand()
        $stateCommand.CommandTimeout = 600
        $stateCommand.CommandText = @'
SELECT pending.Id,
       CASE
           WHEN jobs.Id IS NULL OR jobs.[Query] IS NULL THEN 'Other'
           WHEN DATALENGTH(jobs.[Query]) = DATALENGTH(pending.AfterQuery)
            AND CONVERT(varbinary(max), jobs.[Query]) = CONVERT(varbinary(max), pending.AfterQuery) THEN 'After'
           WHEN DATALENGTH(jobs.[Query]) = DATALENGTH(pending.BeforeQuery)
            AND CONVERT(varbinary(max), jobs.[Query]) = CONVERT(varbinary(max), pending.BeforeQuery) THEN 'Before'
           ELSE 'Other'
       END AS CurrentState
FROM #PendingIdentityTypes AS pending
LEFT JOIN dbo.SyncJobs AS jobs ON jobs.Id = pending.Id
ORDER BY pending.Id;
'@

        $states = [Collections.Generic.List[object]]::new()
        $reader = $stateCommand.ExecuteReader()
        while ($reader.Read()) {
            $states.Add([PSCustomObject]@{
                Id = $reader.GetGuid(0)
                State = $reader.GetString(1)
            })
        }

        return $states.ToArray()
    }
    finally {
        if ($null -ne $reader -and -not $reader.IsClosed) {
            $reader.Close()
        }

        if ($null -ne $stateCommand) {
            $stateCommand.Dispose()
        }

        if ($Connection.State -ne [Data.ConnectionState]::Closed) {
            $Connection.Close()
        }
    }
}

function Resolve-SqlIdentityTypesMigrationStates {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$States,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedCount
    )

    if ($States.Count -ne $ExpectedCount) {
        return 'Conflict'
    }

    if (@($States | Where-Object { $_.State -eq 'After' }).Count -eq $ExpectedCount) {
        return 'After'
    }

    if (@($States | Where-Object { $_.State -eq 'Before' }).Count -eq $ExpectedCount) {
        return 'Before'
    }

    return 'Conflict'
}

function Invoke-SqlIdentityTypesGuardedUpdate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [Data.SqlClient.SqlConnection]$Connection,

        [Parameter(Mandatory = $true)]
        [object[]]$Changes
    )

    # The operation script blocks below read $Connection and $Changes from this function's scope.
    $outcome = [ordered]@{
        Status = 'Failed'
        ReasonCode = ''
        JobsChanged = 0
        Conflicts = 0
        RolledBack = $false
    }

    $attempt = 0
    while ($attempt -lt 2) {
        $attempt++
        $transactionResult = $null
        $transactionFailed = $false

        try {
            $transactionResult = Invoke-SqlIdentityTypesOperation `
                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                -SolutionAbbreviation $SolutionAbbreviation `
                -MaxRetries 1 `
                -Operation {
                    Invoke-SqlIdentityTypesMigrationTransaction `
                        -Connection $Connection `
                        -Changes $Changes
                }
        }
        catch {
            $transactionFailed = $true
            Write-Verbose 'Update transaction outcome requires exact read-back.'
        }

        if ($null -eq $transactionResult) {
            $transactionFailed = $true
        }
        elseif ($transactionResult.Outcome -notin @(
            'Committed',
            'CompareAndSwapMiss',
            'Rejected',
            'Failed')) {

            $transactionFailed = $true
        }

        if ($null -ne $transactionResult -and $transactionResult.Outcome -in @('Rejected', 'Failed')) {
            $outcome.Status = $transactionResult.Outcome
            $outcome.ReasonCode = $transactionResult.ReasonCode
            return [PSCustomObject]$outcome
        }

        $states = $null
        try {
            $states = Invoke-SqlIdentityTypesOperation `
                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                -SolutionAbbreviation $SolutionAbbreviation `
                -Operation {
                    Get-SqlIdentityTypesMigrationStates `
                        -Connection $Connection `
                        -Changes $Changes
                }
        }
        catch {
            $states = $null
        }

        if ($null -eq $states) {
            $outcome.ReasonCode = 'ReadBackFailed'
            return [PSCustomObject]$outcome
        }

        $classification = Resolve-SqlIdentityTypesMigrationStates `
            -States $states `
            -ExpectedCount $Changes.Count

        if ($classification -eq 'After') {
            if ($transactionFailed -or
                ($null -ne $transactionResult -and $transactionResult.Outcome -eq 'CompareAndSwapMiss')) {
                $outcome.Status = 'AlreadyApplied'
                return [PSCustomObject]$outcome
            }

            $outcome.Status = 'Applied'
            $outcome.JobsChanged = $Changes.Count
            return [PSCustomObject]$outcome
        }

        if ($classification -eq 'Before' -and $attempt -lt 2) {
            Write-Verbose 'The uncertain attempt left every query unchanged; retrying the guarded transaction once.'
            continue
        }

        if ($classification -eq 'Before') {
            $outcome.ReasonCode = 'TransactionNotApplied'
            return [PSCustomObject]$outcome
        }

        $outcome.Status = 'Conflict'
        $outcome.Conflicts = $Changes.Count - @($states | Where-Object { $_.State -eq 'After' }).Count
        $outcome.ReasonCode = 'ConcurrentQueryChange'
        # A compare-and-swap miss is only reported after the whole transaction was rolled back.
        $outcome.RolledBack = $null -ne $transactionResult -and $transactionResult.Outcome -eq 'CompareAndSwapMiss'
        return [PSCustomObject]$outcome
    }

    $outcome.ReasonCode = 'RetryLimitReached'
    return [PSCustomObject]$outcome
}

function Set-SqlIdentityTypesMigration {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$ConnectionString,

        [Parameter(Mandatory = $false)]
        [switch]$Apply
    )

    Write-Host 'Start Set-SqlIdentityTypesMigration'

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . (Join-Path $scriptsDirectory 'ReusableModules\Invoke-SqlOperationWithFirewallRetry.ps1')

    $connection = New-SqlIdentityTypesMigrationConnection -ConnectionString $ConnectionString
    $summary = [ordered]@{
        Status = 'NoChanges'
        JobsInspected = 0
        JobsToChange = 0
        JobsChanged = 0
        JobsSkipped = 0
        JobsWithIgnoredParts = 0
        Conflicts = 0
        ReasonCode = ''
    }

    try {
        $attempt = 0
        $confirmationDeclined = $false
        $reportedSkips = [Collections.Generic.Dictionary[Guid, string]]::new()
        $reportedIgnoredParts = [Collections.Generic.Dictionary[Guid, int]]::new()
        while ($true) {
            $attempt++
            $rows = @(
                Invoke-SqlIdentityTypesOperation `
                    -EnvironmentAbbreviation $EnvironmentAbbreviation `
                    -SolutionAbbreviation $SolutionAbbreviation `
                    -Operation { Get-SqlIdentityTypesMigrationRows -Connection $connection }
            )

            $plan = Get-SqlIdentityTypesMigrationPlan -Rows $rows
            $summary.JobsInspected = $plan.JobsInspected
            $summary.JobsToChange = $plan.JobsToChange
            $summary.JobsSkipped = $plan.JobsSkipped
            $summary.JobsWithIgnoredParts = $plan.JobsWithIgnoredParts

            # Warn before confirming so the operator sees what will be left alone.
            Write-SqlIdentityTypesMigrationPlanWarning -Plan $plan -ReportedSkips $reportedSkips -ReportedIgnoredParts $reportedIgnoredParts

            if ($plan.Status -eq 'NoChanges' -or -not $Apply) {
                break
            }

            # A retry re-reads every job, so its plan can differ from the one already confirmed.
            $confirmationTarget = "$($plan.JobsToChange) sync job(s) in dbo.SyncJobs"
            if ($attempt -gt 1) {
                $confirmationTarget += ', read again after a concurrent change'
            }

            if (-not $PSCmdlet.ShouldProcess($confirmationTarget, 'Add the default users-only identity selection')) {
                $confirmationDeclined = $true
                break
            }

            $outcome = Invoke-SqlIdentityTypesGuardedUpdate `
                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                -SolutionAbbreviation $SolutionAbbreviation `
                -Connection $connection `
                -Changes $plan.Changes

            if ($attempt -eq 1 -and $outcome.Status -eq 'Conflict' -and $outcome.RolledBack) {
                Write-Warning 'SQL identity migration wrote nothing because at least one job changed while it was running; reading every job again and retrying once.'
                continue
            }

            break
        }

        if ($plan.Status -eq 'NoChanges') {
            Write-Host "No eligible SQL jobs are missing identityTypes. Inspected $($plan.JobsInspected); skipped $($plan.JobsSkipped)."
            return [PSCustomObject]$summary
        }

        if (-not $Apply) {
            $summary.Status = 'Ready'
            Write-Host "Preview: $($plan.JobsToChange) of $($plan.JobsInspected) job(s) would gain identityTypes users. No changes were written."
            return [PSCustomObject]$summary
        }

        if ($confirmationDeclined) {
            $summary.Status = 'Rejected'
            $summary.ReasonCode = 'ConfirmationDeclined'
            return [PSCustomObject]$summary
        }

        $summary.Status = $outcome.Status
        $summary.ReasonCode = $outcome.ReasonCode
        $summary.JobsChanged = $outcome.JobsChanged
        $summary.Conflicts = $outcome.Conflicts

        if ($outcome.Status -eq 'Applied') {
            Write-Host "Applied identityTypes users to $($summary.JobsChanged) job(s)."
        }
        elseif ($outcome.Status -eq 'AlreadyApplied') {
            Write-Host 'Every proposed identityTypes update was already present after exact read-back.'
        }

        return [PSCustomObject]$summary
    }
    finally {
        if ($null -ne $connection) {
            if ($connection.State -ne [Data.ConnectionState]::Closed) {
                $connection.Close()
            }

            $connection.Dispose()
        }

        Write-Host 'Finish Set-SqlIdentityTypesMigration'
    }
}
