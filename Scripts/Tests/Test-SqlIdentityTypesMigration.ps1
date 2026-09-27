# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.
#
# Dependency-free checks for the SQL identity-types migration.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$script:Passed = 0

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [AllowNull()]
        $Expected,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [AllowNull()]
        $Actual,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    if ($Expected -is [string] -and $Actual -is [string]) {
        $same = [string]::Equals($Expected, $Actual, [StringComparison]::Ordinal)
    }
    else {
        $same = $Expected -eq $Actual
    }

    if (-not $same) {
        throw "Assertion failed: $Because. Expected '$Expected', actual '$Actual'."
    }

    $script:Passed++
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    if (-not $Condition) {
        throw "Assertion failed: $Because."
    }

    $script:Passed++
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Operation,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    try {
        & $Operation
    }
    catch {
        $script:Passed++
        return
    }

    throw "Assertion failed: $Because."
}

$scriptsRoot = Split-Path $PSScriptRoot -Parent
$migrationScript = Join-Path $scriptsRoot 'PostDeploymentMigrations\Set-SqlIdentityTypesMigration.ps1'
Assert-True (Test-Path -LiteralPath $migrationScript) 'the migration script exists at the packaged path'

. $migrationScript

Assert-True ($null -ne (Get-Command Convert-SqlIdentityTypesQuery -ErrorAction SilentlyContinue)) 'dot-sourcing defines the pure transform'
Assert-True ($null -ne (Get-Command Get-SqlIdentityTypesMigrationPlan -ErrorAction SilentlyContinue)) 'dot-sourcing defines batch planning'

function Invoke-SqlOperationWithFirewallRetry {
    param(
        [string]$SolutionAbbreviation,
        [string]$EnvironmentAbbreviation,
        [scriptblock]$Operation,
        [int]$MaxRetries
    )

    [PSCustomObject]@{ Name = 'ancillary-firewall-output' }
    & $Operation
}

$wrappedResult = Invoke-SqlIdentityTypesOperation `
    -EnvironmentAbbreviation 'test' `
    -SolutionAbbreviation 'gmm' `
    -Operation { return 'expected-result' }
Assert-Equal 'expected-result' $wrappedResult 'ancillary firewall-helper output cannot contaminate the SQL result'

Remove-Item -LiteralPath 'function:Invoke-SqlOperationWithFirewallRetry'

. (Join-Path $scriptsRoot 'ReusableModules\Invoke-SqlOperationWithFirewallRetry.ps1')
. (Join-Path $scriptsRoot 'ReusableModules\Invoke-WithFirewallRetry.ps1')

$script:RealHelperCalls = 0
$realValueResult = Invoke-SqlIdentityTypesOperation `
    -EnvironmentAbbreviation 'test' `
    -SolutionAbbreviation 'gmm' `
    -Operation { $script:RealHelperCalls++; return 'expected-result' }
Assert-Equal 'expected-result' $realValueResult 'the real retry helper returns the SQL result'
Assert-Equal 1 $script:RealHelperCalls 'a completed operation runs once through the real retry helper'

$script:RealHelperCalls = 0
$realEmptyResult = @(
    Invoke-SqlIdentityTypesOperation `
        -EnvironmentAbbreviation 'test' `
        -SolutionAbbreviation 'gmm' `
        -Operation { $script:RealHelperCalls++; return @() }
)
Assert-Equal 0 $realEmptyResult.Count 'an empty SQL result remains a valid completed operation'
Assert-Equal 1 $script:RealHelperCalls 'an empty result is not retried by the real retry helper'

$script:RealHelperCalls = 0
$realNullResult = Invoke-SqlIdentityTypesOperation `
    -EnvironmentAbbreviation 'test' `
    -SolutionAbbreviation 'gmm' `
    -Operation { $script:RealHelperCalls++; return $null }
Assert-True ($null -eq $realNullResult) 'a null SQL result is returned as null'
Assert-Equal 1 $script:RealHelperCalls 'a null result is not retried by the real retry helper'

$script:RealHelperCalls = 0
$unwrappedNullResult = Invoke-WithFirewallRetry `
    -ResourceGroup 'unused' `
    -MaxRetries 3 `
    -Operation { $script:RealHelperCalls++; return $null }
Assert-True ($null -eq $unwrappedNullResult) 'the unwrapped helper also returns null'
Assert-Equal 3 $script:RealHelperCalls 'without the result wrapper the real helper repeats a null-returning operation'

Remove-Item -LiteralPath 'function:Invoke-SqlOperationWithFirewallRetry', 'function:Invoke-WithFirewallRetry', 'function:Add-SqlIpFromError'

$single = '[{"type":"SqlMembership","source":{"manager":{"id":2066}}}]'
$singleResult = Convert-SqlIdentityTypesQuery -Query $single
Assert-Equal 'Changed' $singleResult.Status 'a SQL part without a selection is changed'
Assert-Equal '[{"type":"SqlMembership","source":{"manager":{"id":2066}},"identityTypes":["users"]}]' `
    $singleResult.TransformedQuery `
    'the users-only selection is spliced at the query-part root'
Assert-Equal 0 $singleResult.ChangedParts[0] 'the changed part index is reported'

$secondResult = Convert-SqlIdentityTypesQuery -Query $singleResult.TransformedQuery
Assert-Equal 'NoChanges' $secondResult.Status 'the transform is idempotent'
Assert-Equal $singleResult.TransformedQuery $secondResult.TransformedQuery 'a rerun is byte-identical'

$explicitSelections = @(
    '[{"type":"SqlMembership","source":{"filter":"users.Department = ''Sales''"},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{"filter":"agents.BlueprintId = ''alpha''"},"identityTypes":["agents"]}]',
    '[{"type":"SqlMembership","source":{"filter":"users.Department = ''Sales''"},"identityTypes":["users","agents"]}]',
    '[{"type":"SqlMembership","source":{"filter":"users.Department = ''Sales''"},"identityTypes":["agents","users"]}]'
)

foreach ($query in $explicitSelections) {
    $result = Convert-SqlIdentityTypesQuery -Query $query
    Assert-Equal 'NoChanges' $result.Status "a valid explicit selection is preserved: $query"
    Assert-Equal $query $result.TransformedQuery 'a valid explicit selection is byte-identical'
}

$invalidSelections = @(
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":[]}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":["users","users"]}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":["Users"]}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":["devices"]}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":"users"}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":null}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":[1]}]'
)

foreach ($query in $invalidSelections) {
    $result = Convert-SqlIdentityTypesQuery -Query $query
    Assert-Equal 'Invalid' $result.Status "an invalid explicit selection is rejected: $query"
    Assert-Equal 'InvalidIdentityTypes' $result.ReasonCode 'invalid selections use a bounded reason category'
    Assert-Equal $query $result.TransformedQuery 'an invalid selection is not rewritten'
}

$wrongCaseSelections = @(
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"IdentityTypes":["agents"]}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"IDENTITYTYPES":["users","agents"]}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":["users"],"IdentityTypes":["agents"]}]'
)

foreach ($query in $wrongCaseSelections) {
    $result = Convert-SqlIdentityTypesQuery -Query $query
    Assert-Equal 'Invalid' $result.Status "a wrong-cased selection is rejected rather than defaulted to users: $query"
    Assert-Equal 'WrongCaseIdentityTypes' $result.ReasonCode 'a wrong-cased selection uses its own bounded reason'
    Assert-Equal $query $result.TransformedQuery 'a wrong-cased selection is not rewritten'
}

$nestedSelections = @(
    '[{"type":"SqlMembership","source":{"filter":"A = 1","identityTypes":["agents"]}}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1","IdentityTypes":["agents"]}}]',
    '[{"type":"SqlMembership","source":{"manager":{"id":1,"identityTypes":["agents"]}}}]',
    '[{"type":"SqlMembership","source":{"rules":[{"identityTypes":["agents"]}]}}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1","identityTypes":["agents"]},"identityTypes":["users"]}]'
)

foreach ($query in $nestedSelections) {
    $result = Convert-SqlIdentityTypesQuery -Query $query
    Assert-Equal 'Invalid' $result.Status "a selection nested below the part root is rejected rather than defaulted to users: $query"
    Assert-Equal 'MisplacedIdentityTypes' $result.ReasonCode 'a nested selection uses its own bounded reason'
    Assert-Equal $query $result.TransformedQuery 'a nested selection is not rewritten'
}

$selectionNameInText = '[{"type":"SqlMembership","source":{"filter":"Note = ''identityTypes''"}}]'
$selectionNameInTextResult = Convert-SqlIdentityTypesQuery -Query $selectionNameInText
Assert-Equal 'Changed' $selectionNameInTextResult.Status 'the property name inside filter text is not mistaken for a selection'

$nonSqlWithSelection = '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111","IdentityTypes":["agents"]}]'
$nonSqlWithSelectionResult = Convert-SqlIdentityTypesQuery -Query $nonSqlWithSelection
Assert-Equal 'NoChanges' $nonSqlWithSelectionResult.Status 'non-SQL parts are neither rewritten nor rejected'
Assert-Equal $nonSqlWithSelection $nonSqlWithSelectionResult.TransformedQuery 'a non-SQL part is byte-identical'

$mixed = '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"},{"type":"SqlMembership","source":{"manager":{"id":7}}}]'
$mixedResult = Convert-SqlIdentityTypesQuery -Query $mixed
Assert-Equal 'Changed' $mixedResult.Status 'only the SQL part in a mixed query is changed'
Assert-Equal 1 $mixedResult.ChangedParts[0] 'the second part is reported'
Assert-Equal 0 $mixedResult.IgnoredParts 'an ordinary non-SQL part is not counted as unrecognised'
Assert-Equal '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"},{"type":"SqlMembership","source":{"manager":{"id":7}},"identityTypes":["users"]}]' `
    $mixedResult.TransformedQuery `
    'the non-SQL part is unchanged'

$multi = '[{"type":"SqlMembership","source":{"manager":{"id":1}}},{"type":"SqlMembership","source":{"manager":{"id":2}},"identityTypes":["agents"]},{"type":"SqlMembership","source":{"manager":{"id":3}}}]'
$multiResult = Convert-SqlIdentityTypesQuery -Query $multi
Assert-Equal 2 @($multiResult.ChangedParts).Count 'all and only missing SQL selections are added'
Assert-Equal '[{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":["users"]},{"type":"SqlMembership","source":{"manager":{"id":2}},"identityTypes":["agents"]},{"type":"SqlMembership","source":{"manager":{"id":3}},"identityTypes":["users"]}]' `
    $multiResult.TransformedQuery `
    'an explicit middle selection remains unchanged'

$unicodeCharacter = [char]0x00ED
$formattingVariants = [ordered]@{
    'spaces and indentation' = "[`r`n  {`r`n    `"type`": `"SqlMembership`",`r`n    `"source`": { `"filter`": `"Department = '$unicodeCharacter'`" }`r`n  }`r`n]"
    'escaped quotes' = '[{"type":"SqlMembership","source":{"filter":"Title = \"Principal\""}}]'
    'braces in a string' = '[{"type":"SqlMembership","source":{"filter":"Note = ''a{b}c''"}}]'
    'escaped newline text' = '[{"type":"SqlMembership","source":{"filter":"A = ''1''\r\nAND B = ''2''"}}]'
    'Windows path' = '[{"type":"SqlMembership","source":{"filter":"C:\\dir\\sub\\"}}]'
}

foreach ($name in $formattingVariants.Keys) {
    $query = $formattingVariants[$name]
    $result = Convert-SqlIdentityTypesQuery -Query $query
    Assert-Equal 'Changed' $result.Status "the byte-preserving transform accepts $name"
    Assert-Equal $query `
        $result.TransformedQuery.Replace(',"identityTypes":["users"]', '') `
        "removing only the inserted fragment restores every original byte for $name"
}

$malformedInputs = [ordered]@{
    '[{"type":"SqlMembership","source":{"manager":{"id":1}}' = 'InvalidJson'
    'not json' = 'InvalidJson'
    '{"type":"SqlMembership"}' = 'RootNotArray'
}

foreach ($query in $malformedInputs.Keys) {
    $result = Convert-SqlIdentityTypesQuery -Query $query
    Assert-Equal 'Malformed' $result.Status "unreadable input is reported as malformed: $query"
    Assert-Equal $malformedInputs[$query] $result.ReasonCode "unreadable input reports its reason: $query"
    Assert-Equal $query $result.TransformedQuery 'unreadable input is returned unchanged'
}

$unrecognisedParts = @(
    '[1]',
    '[null]',
    '["text"]',
    '[[1,{"type":"SqlMembership","source":{"filter":"A = 1"}}]]',
    '[{"source":"x"}]',
    '[{"type":1,"source":"x"}]',
    '[{"type":null,"source":"x"}]',
    '[{"type":"GroupMembership","type":"GroupMembership","source":"x"}]'
)

foreach ($query in $unrecognisedParts) {
    $result = Convert-SqlIdentityTypesQuery -Query $query
    Assert-Equal 'NoChanges' $result.Status "an unrecognised non-SQL part does not block the query: $query"
    Assert-Equal $query $result.TransformedQuery "an unrecognised non-SQL part is byte-identical: $query"
    Assert-Equal 1 $result.IgnoredParts "an unrecognised non-SQL part is counted: $query"
}

$oddMixed = '[1,{"source":"x"},[2,{"a":[3]}],{"type":"SqlMembership","source":{"manager":{"id":7}}}]'
$oddMixedResult = Convert-SqlIdentityTypesQuery -Query $oddMixed
Assert-Equal 'Changed' $oddMixedResult.Status 'a SQL part after unrecognised parts is still changed'
Assert-Equal 3 $oddMixedResult.ChangedParts[0] 'unrecognised parts do not shift the changed part index'
Assert-Equal 3 $oddMixedResult.IgnoredParts 'every unrecognised part is counted'
Assert-Equal '[1,{"source":"x"},[2,{"a":[3]}],{"type":"SqlMembership","source":{"manager":{"id":7}},"identityTypes":["users"]}]' `
    $oddMixedResult.TransformedQuery `
    'the selection is added only to the SQL part'

$brokenSqlParts = @(
    @{ Query = '[{"type":"SqlMembership"}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":null}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":[]}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":"x"}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"futureShape":{"version":2}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":7}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":"1"}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":1.5}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"depth":2}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":0}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":1,"depth":0}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":1,"x":1}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":1,"id":2}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":1,"depth":2,"depth":3}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":1},"manager":{"id":2}}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1","unknown":true}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"Filter":"A = 1"}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"ids":[7],"filter":"A = 1","future":1}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":null}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1","filter":"B = 2"}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"source":{"filter":"B = 2"}}]'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"exclusionary":"true"}]'; Reason = 'InvalidExclusionary' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"exclusionary":true,"exclusionary":false}]'; Reason = 'InvalidExclusionary' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"unknown":true}]'; Reason = 'UnsupportedProperty' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"Source":{"filter":"B = 2"}}]'; Reason = 'UnsupportedProperty' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"future":{"identityTypes":["agents"]}}]'; Reason = 'MisplacedIdentityTypes' },
    @{ Query = '[{"type":"SqlMembership","type":"SqlMembership","source":{"filter":"A = 1"}}]'; Reason = 'DuplicateType' },
    @{ Query = '[{"type":"GroupMembership","type":"SqlMembership","source":{"filter":"A = 1"}}]'; Reason = 'DuplicateType' }
)

foreach ($case in $brokenSqlParts) {
    $result = Convert-SqlIdentityTypesQuery -Query $case.Query
    Assert-Equal 'Invalid' $result.Status "a SQL part the application cannot read is reported: $($case.Query)"
    Assert-Equal $case.Reason $result.ReasonCode "a broken SQL part reports its reason: $($case.Query)"
    Assert-Equal $case.Query $result.TransformedQuery "a broken SQL part is not rewritten: $($case.Query)"
}

$validSqlShapes = @(
    '[{"type":"SqlMembership","source":{"manager":{"id":1}}}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"}}]',
    '[{"type":"SqlMembership","source":{"manager":{"id":1},"filter":"A = 1"}}]',
    '[{"type":"SqlMembership","source":{"manager":{"id":1,"depth":2}}}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"exclusionary":true}]',
    '[{"type":"SqlMembership","source":{"filter":"A = 1"},"exclusionary":false}]',
    '[{"type":"SqlMembership","source":{"filter":""}}]'
)

foreach ($query in $validSqlShapes) {
    $result = Convert-SqlIdentityTypesQuery -Query $query
    Assert-Equal 'Changed' $result.Status "a readable SQL part gains the default selection: $query"
    Assert-Equal ($query.Substring(0, $query.Length - 2) + ',"identityTypes":["users"]}]') `
        $result.TransformedQuery `
        "only the selection is appended to a readable SQL part: $query"
}

$validRowId = [Guid]::NewGuid()
$invalidRowId = [Guid]::NewGuid()
$misplacedRowId = [Guid]::NewGuid()
$oddRowId = [Guid]::NewGuid()
$oddUnchangedRowId = [Guid]::NewGuid()
$plan = Get-SqlIdentityTypesMigrationPlan -Rows @(
    [PSCustomObject]@{ Id = $validRowId; Query = $single },
    [PSCustomObject]@{ Id = $invalidRowId; Query = $invalidSelections[0] },
    [PSCustomObject]@{ Id = $misplacedRowId; Query = $nestedSelections[0] },
    [PSCustomObject]@{ Id = $oddRowId; Query = $oddMixed },
    [PSCustomObject]@{ Id = $oddUnchangedRowId; Query = '[7,{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":["users"]}]' }
)
Assert-Equal 'Ready' $plan.Status 'jobs that cannot be migrated do not block the rest of the batch'
Assert-Equal 5 $plan.JobsInspected 'every job is inspected'
Assert-Equal 2 $plan.JobsToChange 'only readable jobs are planned for change'
Assert-Equal 2 $plan.JobsSkipped 'every job that cannot be migrated is counted'
Assert-Equal 1 $plan.JobsWithIgnoredParts 'only jobs being changed are counted for unrecognised parts'
Assert-Equal 1 @($plan.IgnoredPartJobs).Count 'a job left unchanged is not listed for unrecognised parts'
Assert-Equal $oddRowId $plan.IgnoredPartJobs[0].Id 'the job with unrecognised parts is identified'
Assert-Equal 3 $plan.IgnoredPartJobs[0].IgnoredParts 'the number of unrecognised parts is kept per job'
Assert-Equal $false ($null -ne $plan.IgnoredPartJobs[0].PSObject.Properties['Query']) 'an entry for unrecognised parts carries no query text'
Assert-Equal $invalidRowId $plan.Skipped[0].Id 'the first skipped job is identified'
Assert-Equal 'InvalidIdentityTypes' $plan.Skipped[0].ReasonCode 'the first skipped job reports its reason'
Assert-Equal $misplacedRowId $plan.Skipped[1].Id 'the second skipped job is identified'
Assert-Equal 'MisplacedIdentityTypes' $plan.Skipped[1].ReasonCode 'the second skipped job reports its reason'
Assert-Equal "$validRowId,$oddRowId" (($plan.Changes | ForEach-Object { $_.Id }) -join ',') 'skipped jobs are excluded from the changes'
Assert-Equal $false ($null -ne $plan.Skipped[0].PSObject.Properties['Query']) 'a skipped entry carries no query text'

$allSkippedPlan = Get-SqlIdentityTypesMigrationPlan -Rows @(
    [PSCustomObject]@{ Id = $invalidRowId; Query = $invalidSelections[0] },
    [PSCustomObject]@{ Id = $misplacedRowId; Query = $nestedSelections[0] }
)
Assert-Equal 'NoChanges' $allSkippedPlan.Status 'a batch with only skipped jobs has nothing to change'
Assert-Equal 2 $allSkippedPlan.JobsSkipped 'the skipped jobs are still counted'
Assert-Equal 0 @($allSkippedPlan.Changes).Count 'no change is planned for skipped jobs'

$emptyPlan = Get-SqlIdentityTypesMigrationPlan -Rows @()
Assert-Equal 'NoChanges' $emptyPlan.Status 'an empty table has nothing to change'
Assert-Equal 0 $emptyPlan.JobsInspected 'an empty table inspects no jobs'

$converterScript = Join-Path $scriptsRoot 'PostDeploymentMigrations\Set-UpdateSourceQuery.ps1'
. $converterScript

$preservationCases = @(
    '[{"type":"SqlMembership","source":{"manager":{"id":1},"filter":"users.EmployeeType = ''FTE''"},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{"filter":"agents.BlueprintId = ''alpha''"},"identityTypes":["agents"]}]',
    '[{"type":"SqlMembership","source":{"manager":{"id":1},"filter":"users.EmployeeType IN (''FTE'',''Vendor'')"},"identityTypes":["users","agents"]}]',
    '[{"type":"SqlMembership","source":{"filter":"users.EmployeeType != ''Vendor''"},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{"filter":"agents.BlueprintId = \"quoted\""},"identityTypes":["agents","users"]}]'
)

foreach ($query in $preservationCases) {
    $converted = Convert-SqlSourceQuery -Query $query
    Assert-Equal 'NoChanges' (Convert-SqlIdentityTypesQuery -Query $converted.TransformedQuery).Status 'the identity migration remains complete after conversion'
}

$unsupportedPropertyQuery = '[{"type":"SqlMembership","source":{"filter":"agents.BlueprintId = \"quoted\""},"identityTypes":["agents","users"],"future":{"nested":[1,2]}}]'
$unsupportedPropertyConverted = Convert-SqlSourceQuery -Query $unsupportedPropertyQuery
Assert-Equal $unsupportedPropertyQuery $unsupportedPropertyConverted.TransformedQuery 'the converter keeps a part with an unknown property as stored'
$unsupportedPropertyResult = Convert-SqlIdentityTypesQuery -Query $unsupportedPropertyConverted.TransformedQuery
Assert-Equal 'Invalid' $unsupportedPropertyResult.Status 'a part with a property the application rejects is skipped'
Assert-Equal 'UnsupportedProperty' $unsupportedPropertyResult.ReasonCode 'an unknown part property reports its reason'
Assert-Equal $unsupportedPropertyQuery $unsupportedPropertyResult.TransformedQuery 'a part with an unknown property is left unchanged'

$unreadableChainCases = @(
    '[{"type":"SqlMembership","source":{"futureShape":{"version":2}}}]',
    '[{"type":"GroupMembership","source":"00000000-0000-0000-0000-000000000001"},{"type":"SqlMembership","source":{"futureShape":{"version":2}}}]',
    '[{"type":"SqlMembership","source":{"ids":[7],"filter":"A = 1","future":1}}]'
)

foreach ($query in $unreadableChainCases) {
    $converted = Convert-SqlSourceQuery -Query $query
    Assert-Equal 'NoChanges' $converted.Status "the converter leaves an unreadable SQL part alone: $query"
    Assert-Equal $query $converted.TransformedQuery "the converter keeps an unreadable SQL part byte for byte: $query"
    $migrated = Convert-SqlIdentityTypesQuery -Query $converted.TransformedQuery
    Assert-Equal 'Invalid' $migrated.Status "the identity migration skips an unreadable SQL part after conversion: $query"
    Assert-Equal 'InvalidSource' $migrated.ReasonCode "an unreadable SQL part after conversion reports its reason: $query"
    Assert-Equal $query $migrated.TransformedQuery "an unreadable SQL part after conversion is never rewritten: $query"
}

$futureShape = '[{"type":"SqlMembership","source":{"futureShape":{"version":2}},"identityTypes":["users"],"future":{"nested":[1,2]}}]'
$futureShapeConverted = Convert-SqlSourceQuery -Query $futureShape
$futureShapeResult = Convert-SqlIdentityTypesQuery -Query $futureShapeConverted.TransformedQuery
Assert-Equal 'Invalid' $futureShapeResult.Status 'a source shape the application cannot read is skipped after conversion'
Assert-Equal 'InvalidSource' $futureShapeResult.ReasonCode 'an unreadable source shape reports its reason'
Assert-Equal $futureShapeConverted.TransformedQuery $futureShapeResult.TransformedQuery 'a skipped source shape is left unchanged'

$migratedThenConverted = Convert-SqlSourceQuery -Query $singleResult.TransformedQuery
Assert-Equal $singleResult.TransformedQuery `
    $migratedThenConverted.TransformedQuery `
    'the existing converter cannot undo a first users-only backfill'

$script:TransactionOutcomes = [Collections.Generic.Queue[string]]::new()
$script:StateOutcomes = [Collections.Generic.Queue[string]]::new()
$script:TopLevelRowSequence = [Collections.Generic.Queue[object]]::new()
$script:TransactionCalls = 0
$script:StateCalls = 0
$script:LastTransactionChanges = @()
$script:TopLevelRows = @(
    [PSCustomObject]@{
        Id = [Guid]::NewGuid()
        Query = $single
    }
)

function Set-TopLevelScenario {
    param(
        [string[]]$TransactionOutcomes = @(),
        [string[]]$StateOutcomes = @()
    )

    $script:TransactionOutcomes.Clear()
    $script:StateOutcomes.Clear()
    $script:TopLevelRowSequence.Clear()
    foreach ($outcome in $TransactionOutcomes) {
        $script:TransactionOutcomes.Enqueue($outcome)
    }

    foreach ($outcome in $StateOutcomes) {
        $script:StateOutcomes.Enqueue($outcome)
    }

    $script:TransactionCalls = 0
    $script:StateCalls = 0
    $script:RowReads = 0
    $script:LastTransactionChanges = @()
}

function Invoke-TopLevelMigration {
    param(
        [switch]$Preview,
        [switch]$ShowConfirmations
    )

    if ($Preview) {
        return Set-SqlIdentityTypesMigration `
            -EnvironmentAbbreviation 'test' `
            -SolutionAbbreviation 'gmm' `
            -ConnectionString 'unused'
    }

    return Set-SqlIdentityTypesMigration `
        -EnvironmentAbbreviation 'test' `
        -SolutionAbbreviation 'gmm' `
        -ConnectionString 'unused' `
        -Apply `
        -Confirm:$false `
        -Verbose:$ShowConfirmations
}

function Assert-TopLevelCalls {
    param(
        [int]$Transactions,
        [int]$States,
        [string]$Scenario,
        [int]$Reads = 1
    )

    Assert-Equal $Reads $script:RowReads "$Scenario reads the jobs the expected number of times"
    Assert-Equal $Transactions $script:TransactionCalls "$Scenario runs the expected number of transactions"
    Assert-Equal $States $script:StateCalls "$Scenario runs the expected number of read-backs"
    Assert-Equal 0 ($script:TransactionOutcomes.Count + $script:StateOutcomes.Count) "$Scenario consumes every scripted outcome"
}

function New-SqlIdentityTypesMigrationConnection {
    param([string]$ConnectionString)
    return [Data.SqlClient.SqlConnection]::new()
}

function Get-SqlIdentityTypesMigrationRows {
    param([Data.SqlClient.SqlConnection]$Connection)
    $script:RowReads++
    if ($script:TopLevelRowSequence.Count -gt 0) {
        return $script:TopLevelRowSequence.Dequeue()
    }

    return $script:TopLevelRows
}

function Invoke-SqlIdentityTypesMigrationTransaction {
    param(
        [Data.SqlClient.SqlConnection]$Connection,
        [object[]]$Changes
    )

    $script:TransactionCalls++
    $script:LastTransactionChanges = $Changes
    $outcome = $script:TransactionOutcomes.Dequeue()
    switch ($outcome) {
        'Exception' { throw 'Injected ambiguous transaction failure.' }
        'NoResult' { return $null }
        'CompareAndSwapMiss' {
            return [PSCustomObject]@{
                Outcome = 'CompareAndSwapMiss'
                ReasonCode = 'UnexpectedAffectedRows'
                RowsAffected = 0
            }
        }
        'Committed' {
            return [PSCustomObject]@{
                Outcome = 'Committed'
                ReasonCode = ''
                RowsAffected = $Changes.Count
            }
        }
        'UnexpectedResult' {
            return [PSCustomObject]@{
                Outcome = 'Unexpected'
                ReasonCode = ''
                RowsAffected = 0
            }
        }
        'Rejected' {
            return [PSCustomObject]@{
                Outcome = 'Rejected'
                ReasonCode = 'ServiceNotStopped'
                RowsAffected = 0
            }
        }
        'Failed' {
            return [PSCustomObject]@{
                Outcome = 'Failed'
                ReasonCode = 'TransactionReadBackFailed'
                RowsAffected = 0
            }
        }
        default { throw "Unexpected transaction outcome '$outcome'." }
    }
}

function Get-SqlIdentityTypesMigrationStates {
    param(
        [Data.SqlClient.SqlConnection]$Connection,
        [object[]]$Changes
    )

    $script:StateCalls++
    $outcome = $script:StateOutcomes.Dequeue()
    if ($outcome -eq 'NoResult') {
        return $null
    }

    if ($outcome -notin @('After', 'Before', 'Other')) {
        throw "Unexpected state outcome '$outcome'."
    }

    return @($Changes | ForEach-Object {
        [PSCustomObject]@{
            Id = $_.Id
            State = $outcome
        }
    })
}

foreach ($ambiguousOutcome in @(
    'Exception',
    'NoResult',
    'CompareAndSwapMiss',
    'UnexpectedResult')) {

    Set-TopLevelScenario -TransactionOutcomes $ambiguousOutcome -StateOutcomes 'After'
    $result = Invoke-TopLevelMigration

    Assert-Equal 'AlreadyApplied' `
        $result.Status `
        "an exact after-value classifies the $ambiguousOutcome transaction outcome as AlreadyApplied"
    Assert-Equal 0 `
        $result.JobsChanged `
        "the $ambiguousOutcome transaction outcome is not reported as a new mutation"
    Assert-TopLevelCalls -Transactions 1 -States 1 -Scenario "the $ambiguousOutcome outcome"
}

Set-TopLevelScenario -TransactionOutcomes 'CompareAndSwapMiss', 'CompareAndSwapMiss' -StateOutcomes 'Other', 'Other'
$conflictOutput = @(Invoke-TopLevelMigration 3>&1)
$conflictWarnings = @($conflictOutput | Where-Object { $_ -is [Management.Automation.WarningRecord] })
$conflictResult = @($conflictOutput | Where-Object { $_ -isnot [Management.Automation.WarningRecord] })[0]
Assert-Equal 'Conflict' $conflictResult.Status 'a job that keeps changing is reported as Conflict after one retry'
Assert-Equal 1 $conflictResult.Conflicts 'the conflicting job is included in the summary count'
Assert-Equal 0 $conflictResult.JobsChanged 'a conflict reports no changed job'
Assert-Equal 1 $conflictWarnings.Count 'the retry after a rolled-back conflict is announced once'
Assert-Equal $false ($null -ne $conflictResult.PSObject.Properties['RolledBack']) 'the summary does not expose the internal rollback flag'
Assert-TopLevelCalls -Transactions 2 -States 2 -Reads 2 -Scenario 'a repeated conflict'

Set-TopLevelScenario -TransactionOutcomes 'CompareAndSwapMiss', 'Committed' -StateOutcomes 'Other', 'After'
$conflictRetryResult = @(Invoke-TopLevelMigration 3>$null)[0]
Assert-Equal 'Applied' $conflictRetryResult.Status 'a rolled-back conflict is retried once against freshly read jobs'
Assert-Equal 1 $conflictRetryResult.JobsChanged 'only the committed retry is reported as changed'
Assert-Equal 0 $conflictRetryResult.Conflicts 'a resolved conflict is not reported'
Assert-TopLevelCalls -Transactions 2 -States 2 -Reads 2 -Scenario 'a conflict resolved by the retry'

Set-TopLevelScenario
$declinedResult = @(Set-SqlIdentityTypesMigration -EnvironmentAbbreviation 'test' -SolutionAbbreviation 'gmm' -ConnectionString 'unused' -Apply -WhatIf 3>$null)[0]
Assert-Equal 'Rejected' $declinedResult.Status 'a declined confirmation writes nothing'
Assert-Equal 'ConfirmationDeclined' $declinedResult.ReasonCode 'a declined confirmation reports its reason'
Assert-Equal 0 $declinedResult.JobsChanged 'a declined confirmation changes no jobs'
Assert-TopLevelCalls -Transactions 0 -States 0 -Scenario 'a declined confirmation'

$firstReadRows = $script:TopLevelRows
$secondReadRows = @($script:TopLevelRows) + [PSCustomObject]@{ Id = [Guid]::NewGuid(); Query = $single }
Set-TopLevelScenario -TransactionOutcomes 'CompareAndSwapMiss', 'Committed' -StateOutcomes 'Other', 'After'
$script:TopLevelRowSequence.Enqueue($firstReadRows)
$script:TopLevelRowSequence.Enqueue($secondReadRows)
$growingOutput = @(Invoke-TopLevelMigration -ShowConfirmations 3>$null 4>&1)
$confirmations = @($growingOutput |
    Where-Object { $_ -is [Management.Automation.VerboseRecord] -and $_.Message.StartsWith('Performing the operation') } |
    ForEach-Object { $_.Message })
$growingResult = @($growingOutput | Where-Object { $_ -isnot [Management.Automation.VerboseRecord] })[0]
Assert-Equal 2 $confirmations.Count 'a retry asks for confirmation again'
Assert-True ($confirmations[0].Contains('"1 sync job(s) in dbo.SyncJobs"')) 'the first confirmation names the jobs first read'
Assert-True ($confirmations[1].Contains('"2 sync job(s) in dbo.SyncJobs, read again after a concurrent change"')) 'the retry confirmation names the jobs read again'
Assert-Equal 'Applied' $growingResult.Status 'a retry whose plan grew is applied after it is confirmed'
Assert-Equal 2 $growingResult.JobsChanged 'the retry reports the jobs it changed'
Assert-Equal 2 @($script:LastTransactionChanges).Count 'the retry writes the plan it confirmed'
Assert-TopLevelCalls -Transactions 2 -States 2 -Reads 2 -Scenario 'a retry whose plan grew'

Set-TopLevelScenario -TransactionOutcomes 'Exception' -StateOutcomes 'Other'
$uncertainConflictResult = Invoke-TopLevelMigration
Assert-Equal 'Conflict' $uncertainConflictResult.Status 'a conflict after an uncertain outcome is not retried'
Assert-TopLevelCalls -Transactions 1 -States 1 -Scenario 'a conflict after an uncertain outcome'

Set-TopLevelScenario -TransactionOutcomes 'Committed' -StateOutcomes 'NoResult'
$readBackFailure = Invoke-TopLevelMigration
Assert-Equal 'Failed' $readBackFailure.Status 'missing exact read-back cannot produce a success result'
Assert-Equal 'ReadBackFailed' $readBackFailure.ReasonCode 'missing read-back uses a bounded failure reason'
Assert-TopLevelCalls -Transactions 1 -States 1 -Scenario 'a missing read-back'

Set-TopLevelScenario -TransactionOutcomes 'Committed' -StateOutcomes 'After'
$committedResult = Invoke-TopLevelMigration
Assert-Equal 'Applied' $committedResult.Status 'a committed and verified transaction is Applied'
Assert-Equal 1 $committedResult.JobsChanged 'the committed job is reported as changed'
Assert-Equal '' $committedResult.ReasonCode 'a successful run has no failure reason'
Assert-TopLevelCalls -Transactions 1 -States 1 -Scenario 'a committed transaction'

Set-TopLevelScenario -TransactionOutcomes 'Exception', 'Committed' -StateOutcomes 'Before', 'After'
$retriedResult = Invoke-TopLevelMigration
Assert-Equal 'Applied' $retriedResult.Status 'an uncertain attempt that changed nothing is retried once and then applied'
Assert-Equal 1 $retriedResult.JobsChanged 'the retried job is reported as changed'
Assert-TopLevelCalls -Transactions 2 -States 2 -Scenario 'a retried transaction'

Set-TopLevelScenario -TransactionOutcomes 'Exception', 'Exception' -StateOutcomes 'Before', 'Before'
$notAppliedResult = Invoke-TopLevelMigration
Assert-Equal 'Failed' $notAppliedResult.Status 'two uncertain attempts that changed nothing fail the run'
Assert-Equal 'TransactionNotApplied' $notAppliedResult.ReasonCode 'the failure reports that nothing was applied'
Assert-Equal 0 $notAppliedResult.JobsChanged 'no job is reported as changed'
Assert-TopLevelCalls -Transactions 2 -States 2 -Scenario 'two uncertain attempts'

Set-TopLevelScenario -TransactionOutcomes 'Rejected'
$rejectedResult = Invoke-TopLevelMigration
Assert-Equal 'Rejected' $rejectedResult.Status 'a transaction refused while the service is running is Rejected'
Assert-Equal 'ServiceNotStopped' $rejectedResult.ReasonCode 'the refusal reason is passed through'
Assert-TopLevelCalls -Transactions 1 -States 0 -Scenario 'a refused transaction'

Set-TopLevelScenario -TransactionOutcomes 'Failed'
$failedResult = Invoke-TopLevelMigration
Assert-Equal 'Failed' $failedResult.Status 'a transaction that failed its own check is Failed'
Assert-Equal 'TransactionReadBackFailed' $failedResult.ReasonCode 'the transaction failure reason is passed through'
Assert-TopLevelCalls -Transactions 1 -States 0 -Scenario 'a failed transaction'

$topLevelValidId = $script:TopLevelRows[0].Id
$topLevelSkippedId = [Guid]::NewGuid()
$topLevelOddId = [Guid]::NewGuid()
$skippedQueryMarker = 'SkippedQueryMarker'
$script:TopLevelRows = @(
    [PSCustomObject]@{ Id = $topLevelValidId; Query = $single },
    [PSCustomObject]@{ Id = $topLevelSkippedId; Query = "[{`"type`":`"SqlMembership`",`"source`":{`"filter`":`"$skippedQueryMarker`"},`"identityTypes`":[]}]" },
    [PSCustomObject]@{ Id = $topLevelOddId; Query = $oddMixed }
)

Set-TopLevelScenario -TransactionOutcomes 'Committed' -StateOutcomes 'After'
$skipOutput = @(Invoke-TopLevelMigration 3>&1)
$skipWarnings = @($skipOutput | Where-Object { $_ -is [Management.Automation.WarningRecord] })
$skipResult = @($skipOutput | Where-Object { $_ -isnot [Management.Automation.WarningRecord] })[0]
Assert-Equal 'Applied' $skipResult.Status 'a skipped job does not stop the rest of the batch'
Assert-Equal 3 $skipResult.JobsInspected 'every job is inspected'
Assert-Equal 2 $skipResult.JobsChanged 'only the readable jobs are changed'
Assert-Equal 1 $skipResult.JobsSkipped 'the skipped job is counted in the summary'
Assert-Equal 1 $skipResult.JobsWithIgnoredParts 'the job with unrecognised parts is counted in the summary'
Assert-Equal "$topLevelValidId,$topLevelOddId" (($script:LastTransactionChanges | ForEach-Object { $_.Id }) -join ',') 'the skipped job is not written'
Assert-Equal 2 $skipWarnings.Count 'one warning is written per skipped job and per job with unrecognisable parts'
Assert-True ($skipWarnings[0].Message.Contains([string]$topLevelSkippedId)) 'the warning identifies the skipped job'
Assert-True ($skipWarnings[0].Message.Contains('InvalidIdentityTypes')) 'the warning gives the reason'
Assert-True (-not $skipWarnings[0].Message.Contains($skippedQueryMarker)) 'the warning does not reveal query text'
Assert-True ($skipWarnings[1].Message.Contains([string]$topLevelOddId)) 'the job with unrecognisable parts is identified'
Assert-True ($skipWarnings[1].Message.Contains('leaves 3 unrecognisable part(s) unchanged')) 'the warning gives the number of unrecognisable parts'
Assert-True (-not $skipWarnings[1].Message.Contains('migrated')) 'the unrecognisable-parts warning does not claim a write before the outcome is known'
Assert-True (-not $skipWarnings[1].Message.Contains('"source":"x"')) 'the unrecognisable-parts warning does not reveal query text'
Assert-TopLevelCalls -Transactions 1 -States 1 -Scenario 'a batch with a skipped job'

Set-TopLevelScenario -TransactionOutcomes 'CompareAndSwapMiss', 'CompareAndSwapMiss' -StateOutcomes 'Other', 'Other'
$repeatConflictWarnings = @(@(Invoke-TopLevelMigration 3>&1) |
    Where-Object { $_ -is [Management.Automation.WarningRecord] } |
    ForEach-Object { $_.Message })
Assert-Equal 3 $repeatConflictWarnings.Count 'a retried batch writes the retry notice and each per-job warning once'
Assert-True ($repeatConflictWarnings[2].Contains('retrying once')) 'the per-job warnings come before the retry notice'
Assert-Equal 1 @($repeatConflictWarnings | Where-Object { $_.Contains([string]$topLevelSkippedId) }).Count 'the skipped job is reported once across both attempts'
Assert-Equal 1 @($repeatConflictWarnings | Where-Object { $_.Contains([string]$topLevelOddId) }).Count 'the job with unrecognisable parts is reported once across both attempts'
Assert-TopLevelCalls -Transactions 2 -States 2 -Reads 2 -Scenario 'a repeated conflict with per-job warnings'

function Get-TopLevelEventOrder {
    param([object[]]$Output)

    foreach ($record in $Output) {
        if ($record -is [Management.Automation.VerboseRecord] -and $record.Message.StartsWith('Performing the operation')) {
            'confirm'
        }
        elseif ($record -is [Management.Automation.WarningRecord]) {
            if ($record.Message.Contains('retrying once')) { 'retry' }
            elseif ($record.Message.Contains('no longer applies')) {
                if ($record.Message.Contains([string]$topLevelSkippedId)) { 'skipped-cleared' }
                elseif ($record.Message.Contains([string]$topLevelOddId)) { 'odd-cleared' }
                else { 'other-warning' }
            }
            elseif ($record.Message.Contains([string]$topLevelSkippedId)) { 'skipped' }
            elseif ($record.Message.Contains([string]$topLevelOddId)) { 'odd' }
            else { 'other-warning' }
        }
    }
}

Set-TopLevelScenario -TransactionOutcomes 'Committed' -StateOutcomes 'After'
$orderedOutput = @(Invoke-TopLevelMigration -ShowConfirmations 3>&1 4>&1)
Assert-Equal 'skipped,odd,confirm' ((Get-TopLevelEventOrder -Output $orderedOutput) -join ',') 'the operator sees which jobs will be left alone before confirming'
Assert-TopLevelCalls -Transactions 1 -States 1 -Scenario 'warnings before confirmation'

$retryReadRows = @($script:TopLevelRows[0], $script:TopLevelRows[1])
Set-TopLevelScenario -TransactionOutcomes 'CompareAndSwapMiss', 'Committed' -StateOutcomes 'Other', 'After'
$script:TopLevelRowSequence.Enqueue($retryReadRows)
$script:TopLevelRowSequence.Enqueue($script:TopLevelRows)
$retryOrderOutput = @(Invoke-TopLevelMigration -ShowConfirmations 3>&1 4>&1)
$retryOrderResult = @($retryOrderOutput | Where-Object { $_ -isnot [Management.Automation.VerboseRecord] -and $_ -isnot [Management.Automation.WarningRecord] })[0]
Assert-Equal 'skipped,confirm,retry,odd,confirm' ((Get-TopLevelEventOrder -Output $retryOrderOutput) -join ',') 'a retry warns only about newly read jobs, before confirming again'
Assert-Equal 'Applied' $retryOrderResult.Status 'a retry that found a new job to warn about is applied after it is confirmed'
Assert-TopLevelCalls -Transactions 2 -States 2 -Reads 2 -Scenario 'a retry with a newly read job to warn about'

$skippedThenFixedRows = @(
    $script:TopLevelRows[0],
    [PSCustomObject]@{ Id = $topLevelSkippedId; Query = $single },
    $script:TopLevelRows[2]
)
Set-TopLevelScenario -TransactionOutcomes 'CompareAndSwapMiss', 'Committed' -StateOutcomes 'Other', 'After'
$script:TopLevelRowSequence.Enqueue($retryReadRows)
$script:TopLevelRowSequence.Enqueue($skippedThenFixedRows)
$skippedThenFixedOutput = @(Invoke-TopLevelMigration -ShowConfirmations 3>&1 4>&1)
$skippedThenFixedWarnings = @($skippedThenFixedOutput | Where-Object { $_ -is [Management.Automation.WarningRecord] } | ForEach-Object { $_.Message })
$skippedThenFixedResult = @($skippedThenFixedOutput | Where-Object { $_ -isnot [Management.Automation.VerboseRecord] -and $_ -isnot [Management.Automation.WarningRecord] })[0]
Assert-Equal 'skipped,confirm,retry,skipped-cleared,odd,confirm' ((Get-TopLevelEventOrder -Output $skippedThenFixedOutput) -join ',') 'a job fixed before the retry has its skip warning withdrawn before confirming again'
Assert-True (@($skippedThenFixedWarnings | Where-Object { $_.Contains('no longer applies') })[0].Contains('skip warning')) 'the withdrawal says which warning no longer applies'
Assert-Equal 'Applied' $skippedThenFixedResult.Status 'a job fixed before the retry does not stop the batch'
Assert-Equal 0 $skippedThenFixedResult.JobsSkipped 'a job fixed before the retry is not counted as skipped'
Assert-Equal 3 $skippedThenFixedResult.JobsChanged 'a job fixed before the retry is migrated'
Assert-TopLevelCalls -Transactions 2 -States 2 -Reads 2 -Scenario 'a skipped job fixed before the retry'

$oddFirstRows = @($script:TopLevelRows[0], $script:TopLevelRows[2])
$oddThenPlainRows = @($script:TopLevelRows[0], [PSCustomObject]@{ Id = $topLevelOddId; Query = $single })
Set-TopLevelScenario -TransactionOutcomes 'CompareAndSwapMiss', 'Committed' -StateOutcomes 'Other', 'After'
$script:TopLevelRowSequence.Enqueue($oddFirstRows)
$script:TopLevelRowSequence.Enqueue($oddThenPlainRows)
$oddThenPlainOutput = @(Invoke-TopLevelMigration -ShowConfirmations 3>&1 4>&1)
$oddThenPlainResult = @($oddThenPlainOutput | Where-Object { $_ -isnot [Management.Automation.VerboseRecord] -and $_ -isnot [Management.Automation.WarningRecord] })[0]
Assert-Equal 'odd,confirm,retry,odd-cleared,confirm' ((Get-TopLevelEventOrder -Output $oddThenPlainOutput) -join ',') 'a job whose unrecognisable parts went away before the retry has that warning withdrawn'
Assert-Equal 'Applied' $oddThenPlainResult.Status 'a withdrawn unrecognisable-parts warning does not stop the batch'
Assert-Equal 0 $oddThenPlainResult.JobsWithIgnoredParts 'the summary counts unrecognisable parts from the final read'
Assert-TopLevelCalls -Transactions 2 -States 2 -Reads 2 -Scenario 'unrecognisable parts gone before the retry'

$changedSkipId = [Guid]::NewGuid()
$changedPartsId = [Guid]::NewGuid()
$warningSkips = [Collections.Generic.Dictionary[Guid, string]]::new()
$warningParts = [Collections.Generic.Dictionary[Guid, int]]::new()
function Get-PlanWarningMessage {
    param([string]$ReasonCode, [int]$IgnoredParts)

    $warningPlan = [PSCustomObject]@{
        Skipped = @([PSCustomObject]@{ Id = $changedSkipId; ReasonCode = $ReasonCode })
        IgnoredPartJobs = @([PSCustomObject]@{ Id = $changedPartsId; IgnoredParts = $IgnoredParts })
    }
    @(Write-SqlIdentityTypesMigrationPlanWarning -Plan $warningPlan -ReportedSkips $warningSkips -ReportedIgnoredParts $warningParts 3>&1 |
        ForEach-Object { $_.Message })
}

Assert-Equal 2 @(Get-PlanWarningMessage -ReasonCode 'InvalidJson' -IgnoredParts 2).Count 'the first read warns about each job'
$changedClassification = @(Get-PlanWarningMessage -ReasonCode 'InvalidSource' -IgnoredParts 3)
Assert-Equal 2 $changedClassification.Count 'a job read again with a different reason or part count is warned about again'
Assert-True ($changedClassification[0].Contains('InvalidSource')) 'the new warning gives the new skip reason'
Assert-True ($changedClassification[1].Contains('leaves 3 unrecognisable part(s)')) 'the new warning gives the new part count'
Assert-Equal 0 @(Get-PlanWarningMessage -ReasonCode 'InvalidSource' -IgnoredParts 3).Count 'a job read again unchanged is not warned about again'

$noChangeOddId = [Guid]::NewGuid()
$script:TopLevelRows = @(
    [PSCustomObject]@{ Id = $noChangeOddId; Query = '[7,{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":["users"]}]' },
    [PSCustomObject]@{ Id = [Guid]::NewGuid(); Query = '[{"source":"x"},{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"}]' }
)
Set-TopLevelScenario
$noChangeOutput = @(Invoke-TopLevelMigration 3>&1)
$noChangeWarnings = @($noChangeOutput | Where-Object { $_ -is [Management.Automation.WarningRecord] })
$noChangeResult = @($noChangeOutput | Where-Object { $_ -isnot [Management.Automation.WarningRecord] })[0]
Assert-Equal 'NoChanges' $noChangeResult.Status 'jobs with only unrecognisable parts left to change need no migration'
Assert-Equal 0 $noChangeResult.JobsWithIgnoredParts 'jobs the migration does not change are not counted for unrecognisable parts'
Assert-Equal 0 $noChangeWarnings.Count 'a later run over jobs it does not change writes no unrecognisable-parts warning'
Assert-TopLevelCalls -Transactions 0 -States 0 -Scenario 'jobs with nothing left to change'
$script:TopLevelRows = @(
    [PSCustomObject]@{ Id = $topLevelValidId; Query = $single },
    [PSCustomObject]@{ Id = $topLevelSkippedId; Query = "[{`"type`":`"SqlMembership`",`"source`":{`"filter`":`"$skippedQueryMarker`"},`"identityTypes`":[]}]" },
    [PSCustomObject]@{ Id = $topLevelOddId; Query = $oddMixed }
)

Set-TopLevelScenario
$previewResult = @(Invoke-TopLevelMigration -Preview 3>$null)[0]
Assert-Equal 'Ready' $previewResult.Status 'a preview reports the pending changes'
Assert-Equal 2 $previewResult.JobsToChange 'a preview counts the jobs it would change'
Assert-Equal 0 $previewResult.JobsChanged 'a preview changes nothing'
Assert-TopLevelCalls -Transactions 0 -States 0 -Scenario 'a preview'

$script:TopLevelRows = @(
    [PSCustomObject]@{ Id = $topLevelSkippedId; Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":[]}]' }
)
Set-TopLevelScenario
$allSkippedResult = @(Invoke-TopLevelMigration 3>$null)[0]
Assert-Equal 'NoChanges' $allSkippedResult.Status 'a run where every job is skipped has nothing to change'
Assert-Equal 1 $allSkippedResult.JobsSkipped 'the skipped job is still counted'
Assert-TopLevelCalls -Transactions 0 -States 0 -Scenario 'a run where every job is skipped'

Remove-Item -LiteralPath @(
    'function:New-SqlIdentityTypesMigrationConnection',
    'function:Get-SqlIdentityTypesMigrationRows',
    'function:Invoke-SqlIdentityTypesMigrationTransaction',
    'function:Get-SqlIdentityTypesMigrationStates')

$wiringRoot = Join-Path ([IO.Path]::GetTempPath()) ("gmm-sql-migration-wiring-" + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $wiringRoot 'ReusableModules') -Force
$null = New-Item -ItemType Directory -Path (Join-Path $wiringRoot 'PostDeploymentMigrations') -Force

try {
    Set-Content -LiteralPath (Join-Path $wiringRoot 'ReusableModules\Invoke-SqlOperationWithFirewallRetry.ps1') -Value @'
function Invoke-SqlOperationWithFirewallRetry { param($Operation) }
'@

    foreach ($stub in @(
        @{ File = 'Set-UpdateSourceQuery.ps1'; Function = 'Set-UpdateSourceQuery' },
        @{ File = 'Set-UpdateDestination.ps1'; Function = 'Set-UpdateDestination' }
    )) {
        Set-Content -LiteralPath (Join-Path $wiringRoot "PostDeploymentMigrations\$($stub.File)") -Value @"
function $($stub.Function) {
    [CmdletBinding()]
    param([string]`$ConnectionString, [string]`$EnvironmentAbbreviation, [string]`$SolutionAbbreviation)
    `$null = `$global:SqlIdentityMigrationWiring.Add([PSCustomObject]@{ Name = '$($stub.Function)'; Apply = `$false; Confirmed = `$false })
}
"@
    }

    Set-Content -LiteralPath (Join-Path $wiringRoot 'PostDeploymentMigrations\Set-SqlIdentityTypesMigration.ps1') -Value @'
function Set-SqlIdentityTypesMigration {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param([string]$ConnectionString, [string]$EnvironmentAbbreviation, [string]$SolutionAbbreviation, [switch]$Apply)
    $null = $global:SqlIdentityMigrationWiring.Add([PSCustomObject]@{
        Name = 'Set-SqlIdentityTypesMigration'
        Apply = [bool]$Apply
        Confirmed = $PSBoundParameters.ContainsKey('Confirm') -and -not [bool]$PSBoundParameters['Confirm']
    })
    return [PSCustomObject]@{
        Status = $global:SqlIdentityMigrationResultStatus
        ReasonCode = if ($global:SqlIdentityMigrationResultStatus -eq 'Applied') { '' } else { 'InjectedFailure' }
    }
}
'@

    $global:SqlIdentityMigrationWiring = [Collections.ArrayList]::new()
    $global:SqlIdentityMigrationResultStatus = 'Applied'
    $global:SkipAzLogin = $true
    function Set-AzContext { param([string]$SubscriptionName) }

    . (Join-Path $scriptsRoot 'PostDeploymentMigrations\Set-SqlMigrationsIfNeeded.ps1')
    Set-SqlMigrationsIfNeeded `
        -EnvironmentAbbreviation 'test' `
        -SolutionAbbreviation 'gmm' `
        -SubscriptionName 'test-subscription' `
        -ConnectionString 'Server=unused' `
        -ScriptsDirectory $wiringRoot

    $calls = @($global:SqlIdentityMigrationWiring)
    Assert-Equal 3 $calls.Count 'all three post-deployment migrations run'
    Assert-Equal 'Set-UpdateSourceQuery,Set-UpdateDestination,Set-SqlIdentityTypesMigration' `
        ($calls.Name -join ',') `
        'the identity migration runs after every existing query rewrite'
    Assert-True ($calls[2].Apply) 'the deployment wrapper explicitly applies the migration'
    Assert-True ($calls[2].Confirmed) 'the unattended deployment suppresses the High-impact confirmation prompt'

    foreach ($failureStatus in @('Rejected', 'Failed', 'Conflict')) {
        $global:SqlIdentityMigrationResultStatus = $failureStatus
        Assert-Throws {
            Set-SqlMigrationsIfNeeded `
                -EnvironmentAbbreviation 'test' `
                -SolutionAbbreviation 'gmm' `
                -SubscriptionName 'test-subscription' `
                -ConnectionString 'Server=unused' `
                -ScriptsDirectory $wiringRoot
        } "the deployment task fails for migration status $failureStatus"
    }
}
finally {
    Remove-Item -LiteralPath $wiringRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath 'function:Set-AzContext' -ErrorAction SilentlyContinue
    Remove-Variable -Name SqlIdentityMigrationWiring -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable -Name SqlIdentityMigrationResultStatus -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable -Name SkipAzLogin -Scope Global -ErrorAction SilentlyContinue
}

$childRoot = Join-Path ([IO.Path]::GetTempPath()) ("gmm-sql-migration-child-" + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $childRoot 'ReusableModules') -Force
$null = New-Item -ItemType Directory -Path (Join-Path $childRoot 'PostDeploymentMigrations') -Force

try {
    foreach ($helper in @('Invoke-SqlOperationWithFirewallRetry.ps1', 'Invoke-WithFirewallRetry.ps1')) {
        Copy-Item `
            -LiteralPath (Join-Path $scriptsRoot "ReusableModules\$helper") `
            -Destination (Join-Path $childRoot "ReusableModules\$helper")
    }

    foreach ($stubName in @('Set-UpdateSourceQuery', 'Set-UpdateDestination')) {
        Set-Content -LiteralPath (Join-Path $childRoot "PostDeploymentMigrations\$stubName.ps1") -Value @"
function $stubName {
    [CmdletBinding()]
    param([string]`$ConnectionString, [string]`$EnvironmentAbbreviation, [string]`$SolutionAbbreviation)
}
"@
    }

    $databaseOverrides = @'

function New-SqlIdentityTypesMigrationConnection {
    param([string]$ConnectionString)
    return [Data.SqlClient.SqlConnection]::new()
}

function Get-SqlIdentityTypesMigrationRows {
    param([Data.SqlClient.SqlConnection]$Connection)
    return $global:SqlIdentityChildRows
}

function Invoke-SqlIdentityTypesMigrationTransaction {
    param([Data.SqlClient.SqlConnection]$Connection, [object[]]$Changes)
    $global:SqlIdentityChildChanges = $Changes
    $outcome = $global:SqlIdentityChildTransactionOutcome
    return [PSCustomObject]@{
        Outcome = $outcome
        ReasonCode = if ($outcome -eq 'Rejected') { 'ServiceNotStopped' } else { '' }
        RowsAffected = if ($outcome -eq 'Committed') { $Changes.Count } else { 0 }
    }
}

function Get-SqlIdentityTypesMigrationStates {
    param([Data.SqlClient.SqlConnection]$Connection, [object[]]$Changes)
    return @($Changes | ForEach-Object { [PSCustomObject]@{ Id = $_.Id; State = 'After' } })
}
'@
    $realMigration = [IO.File]::ReadAllText($migrationScript)
    [IO.File]::WriteAllText(
        (Join-Path $childRoot 'PostDeploymentMigrations\Set-SqlIdentityTypesMigration.ps1'),
        $realMigration + $databaseOverrides,
        [Text.UTF8Encoding]::new($false))

    $driverPath = Join-Path $childRoot 'Invoke-ChildDeployment.ps1'
    Set-Content -LiteralPath $driverPath -Value @'
param(
    [string]$ScriptsDirectory,
    [string]$WiringScript,
    [string]$RowsPath,
    [string]$OutputPath,
    [string]$TransactionOutcome
)

$ErrorActionPreference = 'Stop'
$global:SkipAzLogin = $true
$global:SqlIdentityChildTransactionOutcome = $TransactionOutcome
$global:SqlIdentityChildRows = @(
    Get-Content -LiteralPath $RowsPath -Raw |
        ConvertFrom-Json |
        ForEach-Object { [PSCustomObject]@{ Id = [Guid]$_.Id; Query = [string]$_.Query } }
)

function Set-AzContext { param([string]$SubscriptionName) }

. $WiringScript
Set-SqlMigrationsIfNeeded `
    -EnvironmentAbbreviation 'test' `
    -SolutionAbbreviation 'gmm' `
    -SubscriptionName 'test-subscription' `
    -ConnectionString 'Server=unused' `
    -ScriptsDirectory $ScriptsDirectory

$captured = @($global:SqlIdentityChildChanges | ForEach-Object { [PSCustomObject]@{ Id = [string]$_.Id; AfterQuery = $_.AfterQuery } })
Set-Content -LiteralPath $OutputPath -Value (ConvertTo-Json -InputObject $captured) -Encoding utf8NoBOM
exit 0
'@

    $childValidId = '11111111-1111-1111-1111-111111111111'
    $childOddId = '22222222-2222-2222-2222-222222222222'
    $childSkippedId = '33333333-3333-3333-3333-333333333333'
    $rowsPath = Join-Path $childRoot 'rows.json'
    $childRows = @(
        [PSCustomObject]@{ Id = $childValidId; Query = $single },
        [PSCustomObject]@{ Id = $childOddId; Query = $oddMixed },
        [PSCustomObject]@{ Id = $childSkippedId; Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":[]}]' }
    )
    Set-Content -LiteralPath $rowsPath -Value (ConvertTo-Json -InputObject $childRows) -Encoding utf8NoBOM

    $pwshPath = (Get-Process -Id $PID).Path
    $realWiring = Join-Path $scriptsRoot 'PostDeploymentMigrations\Set-SqlMigrationsIfNeeded.ps1'

    function Invoke-ChildDeployment {
        param(
            [string]$TransactionOutcome,
            [string]$OutputPath
        )

        $ErrorActionPreference = 'Continue'
        $output = & $pwshPath -NoProfile -NonInteractive -File $driverPath `
            -ScriptsDirectory $childRoot `
            -WiringScript $realWiring `
            -RowsPath $rowsPath `
            -OutputPath $OutputPath `
            -TransactionOutcome $TransactionOutcome 2>&1
        return [PSCustomObject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output | Out-String)
        }
    }

    $committedPath = Join-Path $childRoot 'committed.json'
    $committedRun = Invoke-ChildDeployment -TransactionOutcome 'Committed' -OutputPath $committedPath
    Assert-Equal 0 $committedRun.ExitCode "the real migration succeeds through the deployment wiring. Output: $($committedRun.Output)"

    $captured = @(Get-Content -LiteralPath $committedPath -Raw | ConvertFrom-Json)
    Assert-Equal "$childValidId,$childOddId" (($captured | ForEach-Object { $_.Id }) -join ',') 'the deployment writes the readable jobs and leaves the skipped job alone'
    Assert-Equal '[{"type":"SqlMembership","source":{"manager":{"id":2066}},"identityTypes":["users"]}]' `
        $captured[0].AfterQuery `
        'the deployment writes the users-only selection into the SQL part'
    Assert-Equal '[1,{"source":"x"},[2,{"a":[3]}],{"type":"SqlMembership","source":{"manager":{"id":7}},"identityTypes":["users"]}]' `
        $captured[1].AfterQuery `
        'the deployment leaves unrecognised parts byte-for-byte and updates only the SQL part'
    Assert-True ($committedRun.Output.Contains("skipped job $childSkippedId (InvalidIdentityTypes)")) 'the deployment log names the skipped job and its reason'

    $rejectedRun = Invoke-ChildDeployment -TransactionOutcome 'Rejected' -OutputPath (Join-Path $childRoot 'rejected.json')
    Assert-True ($rejectedRun.ExitCode -ne 0) 'the deployment task fails when the real migration is refused'
    Assert-True ($rejectedRun.Output.Contains("status 'Rejected' and reason 'ServiceNotStopped'")) 'the deployment failure reports the refusal reason'
}
finally {
    Remove-Item -LiteralPath $childRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ("{0} migration checks passed." -f $script:Passed) -ForegroundColor Green
