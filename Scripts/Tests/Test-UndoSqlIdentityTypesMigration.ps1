# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.
#
# Dependency-free checks for reversing the SQL identity-types migration.

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

$scriptsRoot = Split-Path $PSScriptRoot -Parent
$undoScript = Join-Path $scriptsRoot 'PostDeploymentMigrations\Undo-SqlIdentityTypesMigration.ps1'
Assert-True (Test-Path -LiteralPath $undoScript) 'the undo script exists next to the migration'

. $undoScript

Assert-True ($null -ne (Get-Command Convert-SqlIdentityTypesQuery -ErrorAction SilentlyContinue)) 'the undo script loads the migration it reverses'
Assert-True ($null -ne (Get-Command Convert-SqlIdentityTypesUndoQuery -ErrorAction SilentlyContinue)) 'dot-sourcing defines the pure undo transform'

$wiringText = [IO.File]::ReadAllText((Join-Path $scriptsRoot 'PostDeploymentMigrations\Set-SqlMigrationsIfNeeded.ps1'))
Assert-True (-not $wiringText.Contains('Undo-SqlIdentityTypesMigration')) 'deployment never runs the undo'

$single = '[{"type":"SqlMembership","source":{"manager":{"id":2066}}}]'
$mixed = '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"},{"type":"SqlMembership","source":{"manager":{"id":7}}}]'
$multi = '[{"type":"SqlMembership","source":{"manager":{"id":1}}},{"type":"SqlMembership","source":{"manager":{"id":2}},"identityTypes":["agents"]},{"type":"SqlMembership","source":{"manager":{"id":3}}}]'
$oddMixed = '[1,{"source":"x"},[2,{"a":[3]}],{"type":"SqlMembership","source":{"manager":{"id":7}}}]'
$nonAscii = '[{"type":"SqlMembership","source":{"filter":"Name = ''' + [char]0x00F1 + [char]::ConvertFromUtf32(0x1F600) + '''"}}]'

$roundTrips = [ordered]@{
    'a single SQL part' = $single
    'a mixed query' = $mixed
    'several SQL parts around an explicit selection' = $multi
    'unrecognised parts before the SQL part' = $oddMixed
    'spaces and indentation' = "[`r`n  {`r`n    `"type`": `"SqlMembership`",`r`n    `"source`": { `"filter`": `"Department = '$([char]0x00ED)'`" }`r`n  }`r`n]"
    'whitespace before the closing brace' = '[{"type":"SqlMembership","source":{"manager":{"id":1}} }]'
    'escaped quotes' = '[{"type":"SqlMembership","source":{"filter":"Title = \"Principal\""}}]'
    'braces in a string' = '[{"type":"SqlMembership","source":{"filter":"Note = ''a{b}c''"}}]'
    'escaped newline text' = '[{"type":"SqlMembership","source":{"filter":"A = ''1''\r\nAND B = ''2''"}}]'
    'Windows path' = '[{"type":"SqlMembership","source":{"filter":"C:\\dir\\sub\\"}}]'
    'non-ASCII and emoji text' = $nonAscii
    'an exclusionary part' = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"exclusionary":true}]'
    'manager and filter together' = '[{"type":"SqlMembership","source":{"manager":{"id":1},"filter":"A = 1"}}]'
    'an empty filter' = '[{"type":"SqlMembership","source":{"filter":""}}]'
}

foreach ($name in $roundTrips.Keys) {
    $original = $roundTrips[$name]
    $migrated = Convert-SqlIdentityTypesQuery -Query $original
    Assert-Equal 'Changed' $migrated.Status "the migration changes $name"

    $undone = Convert-SqlIdentityTypesUndoQuery -Query $migrated.TransformedQuery
    Assert-Equal 'Changed' $undone.Status "the undo changes $name"
    Assert-Equal $original $undone.TransformedQuery "the undo restores every original byte for $name"
    Assert-Equal (@($migrated.ChangedParts) -join ',') (@($undone.ChangedParts) -join ',') "the undo reverts exactly the parts the migration changed for $name"

    $rerun = Convert-SqlIdentityTypesUndoQuery -Query $undone.TransformedQuery
    Assert-Equal 'NoChanges' $rerun.Status "a second undo changes nothing for $name"
}

$multiUndone = Convert-SqlIdentityTypesUndoQuery -Query (Convert-SqlIdentityTypesQuery -Query $multi).TransformedQuery
Assert-Equal 1 $multiUndone.KeptParts 'the explicit agents selection in the middle part is kept and counted'

$keptSelections = [ordered]@{
    'an agents selection' = '[{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":["agents"]}]'
    'a users and agents selection' = '[{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":["users","agents"]}]'
    'a users selection written with spaces' = '[{"type":"SqlMembership","source":{"manager":{"id":1}}, "identityTypes": ["users"]}]'
    'a users selection that is not the last property' = '[{"type":"SqlMembership","identityTypes":["users"],"source":{"manager":{"id":1}}}]'
    'a users selection followed by whitespace' = '[{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":["users"] }]'
}

foreach ($name in $keptSelections.Keys) {
    $query = $keptSelections[$name]
    $result = Convert-SqlIdentityTypesUndoQuery -Query $query
    Assert-Equal 'NoChanges' $result.Status "the undo keeps $name"
    Assert-Equal 1 $result.KeptParts "the undo counts $name as kept"
    Assert-Equal $query $result.TransformedQuery "the undo leaves $name byte-identical"
}

$partial = '[{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":["users"]},{"type":"SqlMembership","source":{"manager":{"id":2}},"identityTypes":["agents"]}]'
$partialResult = Convert-SqlIdentityTypesUndoQuery -Query $partial
Assert-Equal 'Changed' $partialResult.Status 'a query with one removable selection is changed'
Assert-Equal '0' (@($partialResult.ChangedParts) -join ',') 'only the part in the migration form is reverted'
Assert-Equal 1 $partialResult.KeptParts 'the other selection is kept and counted'
Assert-Equal '[{"type":"SqlMembership","source":{"manager":{"id":1}}},{"type":"SqlMembership","source":{"manager":{"id":2}},"identityTypes":["agents"]}]' `
    $partialResult.TransformedQuery `
    'the kept selection is byte-identical'

$nonSqlParts = @(
    '[{"type":"GroupMembership","source":"x","identityTypes":["users"]}]',
    '[{"source":"x","identityTypes":["users"]}]'
)

foreach ($query in $nonSqlParts) {
    $result = Convert-SqlIdentityTypesUndoQuery -Query $query
    Assert-Equal 'NoChanges' $result.Status "the undo never edits a part that is not SQL: $query"
    Assert-Equal 0 $result.KeptParts "a part that is not SQL is not counted as kept: $query"
    Assert-Equal $query $result.TransformedQuery "a part that is not SQL is byte-identical: $query"
}

$skipCases = @(
    @{ Query = 'not json'; Status = 'Malformed'; Reason = 'InvalidJson' },
    @{ Query = '{"type":"SqlMembership"}'; Status = 'Malformed'; Reason = 'RootNotArray' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":[]}]'; Status = 'Invalid'; Reason = 'InvalidIdentityTypes' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"IdentityTypes":["agents"],"identityTypes":["users"]}]'; Status = 'Invalid'; Reason = 'WrongCaseIdentityTypes' },
    @{ Query = '[{"type":"SqlMembership","identityTypes":["users"]}]'; Status = 'Invalid'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":0}},"identityTypes":["users"]}]'; Status = 'Invalid'; Reason = 'InvalidSource' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"note":1,"identityTypes":["users"]}]'; Status = 'Invalid'; Reason = 'UnsupportedProperty' }
)

foreach ($case in $skipCases) {
    $result = Convert-SqlIdentityTypesUndoQuery -Query $case.Query
    Assert-Equal $case.Status $result.Status "a query the undo cannot read is reported: $($case.Query)"
    Assert-Equal $case.Reason $result.ReasonCode "a query the undo cannot read reports its reason: $($case.Query)"
    Assert-Equal $case.Query $result.TransformedQuery "a query the undo cannot read is left unchanged: $($case.Query)"
}

Assert-Equal 'NoChanges' (Convert-SqlIdentityTypesUndoQuery -Query '').Status 'an empty query has nothing to undo'
$neverMigrated = Convert-SqlIdentityTypesUndoQuery -Query $single
Assert-Equal 'NoChanges' $neverMigrated.Status 'a query without a selection has nothing to undo'
Assert-Equal 0 $neverMigrated.KeptParts 'a query without a selection has nothing to keep'

$migratedSingle = (Convert-SqlIdentityTypesQuery -Query $single).TransformedQuery
Assert-True ($null -eq (Remove-SqlIdentityTypesSelection -Query $single -PartIndexes 0)) 'a part without the migration fragment cannot be edited'
Assert-True ($null -eq (Remove-SqlIdentityTypesSelection -Query $migratedSingle -PartIndexes 0, 0)) 'a part cannot be targeted twice'
Assert-True ($null -eq (Remove-SqlIdentityTypesSelection -Query $migratedSingle -PartIndexes 1)) 'a missing part cannot be targeted'
Assert-True ($null -eq (Remove-SqlIdentityTypesSelection -Query ($migratedSingle + '"') -PartIndexes 0)) 'unbalanced text cannot be edited'

$migratedRowId = [Guid]::NewGuid()
$keptRowId = [Guid]::NewGuid()
$invalidRowId = [Guid]::NewGuid()
$neverMigratedRowId = [Guid]::NewGuid()
$oddRowId = [Guid]::NewGuid()
$migratedOdd = (Convert-SqlIdentityTypesQuery -Query $oddMixed).TransformedQuery
$planRows = @(
    [PSCustomObject]@{ Id = $migratedRowId; Query = $migratedSingle },
    [PSCustomObject]@{ Id = $keptRowId; Query = $keptSelections['an agents selection'] },
    [PSCustomObject]@{ Id = $invalidRowId; Query = $skipCases[2].Query },
    [PSCustomObject]@{ Id = $neverMigratedRowId; Query = $single },
    [PSCustomObject]@{ Id = $oddRowId; Query = $migratedOdd }
)
$plan = Get-SqlIdentityTypesUndoPlan -Rows $planRows
Assert-Equal 'Ready' $plan.Status 'jobs that cannot be reverted do not block the rest of the batch'
Assert-Equal 5 $plan.JobsInspected 'every job is inspected'
Assert-Equal 2 $plan.JobsToChange 'only jobs in the migration form are planned for change'
Assert-Equal 1 $plan.JobsSkipped 'the unreadable job is counted'
Assert-Equal 1 $plan.JobsWithKeptSelections 'the job with a kept selection is counted'
Assert-Equal $keptRowId $plan.KeptSelectionJobs[0].Id 'the job with a kept selection is identified'
Assert-Equal 1 $plan.KeptSelectionJobs[0].KeptParts 'the kept selections in that job are counted'
Assert-True ($null -eq $plan.KeptSelectionJobs[0].PSObject.Properties['Query']) 'the kept-selection list carries no query text'
Assert-Equal $invalidRowId $plan.Skipped[0].Id 'the skipped job is identified'
Assert-Equal 'InvalidIdentityTypes' $plan.Skipped[0].ReasonCode 'the skipped job reports its reason'
Assert-Equal "$migratedRowId,$oddRowId" (($plan.Changes | ForEach-Object { $_.Id }) -join ',') 'only reverted jobs are planned'
Assert-Equal $migratedSingle $plan.Changes[0].BeforeQuery 'the plan guards on the current migrated text'
Assert-Equal $single $plan.Changes[0].AfterQuery 'the plan restores the original text'
Assert-Equal $oddMixed $plan.Changes[1].AfterQuery 'the plan restores unrecognised parts byte-for-byte'

$allKeptPlan = Get-SqlIdentityTypesUndoPlan -Rows @(
    [PSCustomObject]@{ Id = $keptRowId; Query = $keptSelections['an agents selection'] }
)
Assert-Equal 'NoChanges' $allKeptPlan.Status 'a batch with only kept selections has nothing to change'
Assert-Equal 1 $allKeptPlan.JobsWithKeptSelections 'the kept selection is still counted'

$emptyPlan = Get-SqlIdentityTypesUndoPlan -Rows @()
Assert-Equal 'NoChanges' $emptyPlan.Status 'an empty table has nothing to undo'
Assert-Equal 0 $emptyPlan.JobsInspected 'an empty table inspects no jobs'

$script:TopLevelRows = $planRows
$script:TransactionOutcome = 'Committed'
$script:TransactionCalls = 0
$script:StateCalls = 0
$script:LastTransactionChanges = @()

function Reset-TopLevelCalls {
    $script:TransactionCalls = 0
    $script:StateCalls = 0
    $script:LastTransactionChanges = @()
}

function New-SqlIdentityTypesMigrationConnection {
    param([string]$ConnectionString)
    return [Data.SqlClient.SqlConnection]::new()
}

function Get-SqlIdentityTypesMigrationRows {
    param([Data.SqlClient.SqlConnection]$Connection)
    return $script:TopLevelRows
}

function Invoke-SqlIdentityTypesMigrationTransaction {
    param(
        [Data.SqlClient.SqlConnection]$Connection,
        [object[]]$Changes
    )

    $script:TransactionCalls++
    $script:LastTransactionChanges = $Changes
    if ($script:TransactionOutcome -eq 'Rejected') {
        return [PSCustomObject]@{
            Outcome = 'Rejected'
            ReasonCode = 'ServiceNotStopped'
            RowsAffected = 0
        }
    }

    return [PSCustomObject]@{
        Outcome = 'Committed'
        ReasonCode = ''
        RowsAffected = $Changes.Count
    }
}

function Get-SqlIdentityTypesMigrationStates {
    param(
        [Data.SqlClient.SqlConnection]$Connection,
        [object[]]$Changes
    )

    $script:StateCalls++
    return @($Changes | ForEach-Object { [PSCustomObject]@{ Id = $_.Id; State = 'After' } })
}

$undoParameters = @{
    EnvironmentAbbreviation = 'test'
    SolutionAbbreviation = 'gmm'
    ConnectionString = 'unused'
}

Reset-TopLevelCalls
$previewResult = @(Undo-SqlIdentityTypesMigration @undoParameters 3>$null)[0]
Assert-Equal 'Ready' $previewResult.Status 'a preview reports the pending removals'
Assert-Equal 2 $previewResult.JobsToChange 'a preview counts the jobs it would change'
Assert-Equal 1 $previewResult.JobsWithKeptSelections 'a preview counts the kept selections'
Assert-Equal 0 $script:TransactionCalls 'a preview writes nothing'

Reset-TopLevelCalls
$applyOutput = @(Undo-SqlIdentityTypesMigration @undoParameters -Apply -Confirm:$false 3>&1)
$applyWarnings = @($applyOutput | Where-Object { $_ -is [Management.Automation.WarningRecord] })
$applyResult = @($applyOutput | Where-Object { $_ -isnot [Management.Automation.WarningRecord] })[0]
Assert-Equal 'Applied' $applyResult.Status 'a committed and verified undo is Applied'
Assert-Equal 2 $applyResult.JobsChanged 'the reverted jobs are reported as changed'
Assert-Equal 1 $applyResult.JobsSkipped 'the skipped job is counted'
Assert-Equal 1 $script:TransactionCalls 'the undo runs one guarded transaction'
Assert-Equal 1 $script:StateCalls 'the undo reads back the result once'
Assert-Equal "$single|$oddMixed" (($script:LastTransactionChanges | ForEach-Object { $_.AfterQuery }) -join '|') 'the undo writes the original queries'
Assert-Equal 2 $applyWarnings.Count 'one warning is written per skipped job and per job with a kept selection'
Assert-True ($applyWarnings[0].Message.Contains("undo skipped job $invalidRowId (InvalidIdentityTypes)")) 'the warning identifies the skipped job and its reason'
Assert-True (-not $applyWarnings[0].Message.Contains('A = 1')) 'the warning does not reveal query text'
Assert-True ($applyWarnings[1].Message.Contains("kept 1 identityTypes selection(s) in job $keptRowId")) 'the warning identifies the job with a kept selection'
Assert-True ($applyWarnings[1].Message.Contains('will reject this job')) 'the warning says an older release will reject the job'
Assert-True (-not $applyWarnings[1].Message.Contains('manager') -and -not $applyWarnings[1].Message.Contains('"agents"')) 'the kept-selection warning does not reveal query text'

Reset-TopLevelCalls
$previewOutput = @(Undo-SqlIdentityTypesMigration @undoParameters 3>&1)
Assert-Equal 2 (@($previewOutput | Where-Object { $_ -is [Management.Automation.WarningRecord] })).Count 'a preview writes the same warnings'

Reset-TopLevelCalls
$declinedResult = @(Undo-SqlIdentityTypesMigration @undoParameters -Apply -WhatIf 3>$null)[0]
Assert-Equal 'Rejected' $declinedResult.Status 'a declined confirmation writes nothing'
Assert-Equal 'ConfirmationDeclined' $declinedResult.ReasonCode 'a declined confirmation reports its reason'
Assert-Equal 0 $script:TransactionCalls 'a declined confirmation runs no transaction'

Reset-TopLevelCalls
$script:TransactionOutcome = 'Rejected'
$rejectedResult = @(Undo-SqlIdentityTypesMigration @undoParameters -Apply -Confirm:$false 3>$null)[0]
Assert-Equal 'Rejected' $rejectedResult.Status 'an undo refused while the service is running is Rejected'
Assert-Equal 'ServiceNotStopped' $rejectedResult.ReasonCode 'the refusal reason is passed through'
Assert-Equal 0 $script:StateCalls 'a refused undo is not read back'

Reset-TopLevelCalls
$script:TransactionOutcome = 'Committed'
$script:TopLevelRows = @(
    [PSCustomObject]@{ Id = $keptRowId; Query = $keptSelections['an agents selection'] },
    [PSCustomObject]@{ Id = $neverMigratedRowId; Query = $single }
)
$noChangesResult = Undo-SqlIdentityTypesMigration @undoParameters -Apply -Confirm:$false
Assert-Equal 'NoChanges' $noChangesResult.Status 'a table without migrated selections has nothing to undo'
Assert-Equal 0 $script:TransactionCalls 'nothing is written when there is nothing to undo'

Remove-Item -LiteralPath @(
    'function:New-SqlIdentityTypesMigrationConnection',
    'function:Get-SqlIdentityTypesMigrationRows',
    'function:Invoke-SqlIdentityTypesMigrationTransaction',
    'function:Get-SqlIdentityTypesMigrationStates')

Write-Host ("{0} undo checks passed." -f $script:Passed) -ForegroundColor Green
