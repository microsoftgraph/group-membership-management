# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.

. (Join-Path $PSScriptRoot 'Set-SqlIdentityTypesMigration.ps1')

function Remove-SqlIdentityTypesSelection {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions',
        '',
        Justification = 'Returns an edited copy of the query text.')]
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

    $fragment = $script:DefaultSelectionFragment
    $targets = [Collections.Generic.HashSet[int]]::new()
    $removalStarts = [Collections.Generic.List[int]]::new()
    foreach ($partIndex in $PartIndexes) {
        $closeIndex = 0
        if (-not $targets.Add($partIndex) -or
            -not $closeIndexes.TryGetValue($partIndex, [ref]$closeIndex)) {
            return $null
        }

        $removalStart = $closeIndex - $fragment.Length
        if ($removalStart -lt 0 -or
            [string]::CompareOrdinal($Query, $removalStart, $fragment, 0, $fragment.Length) -ne 0) {
            return $null
        }

        $removalStarts.Add($removalStart)
    }

    $removalStarts.Sort()
    $builder = [Text.StringBuilder]::new($Query.Length)
    $copiedUpTo = 0
    foreach ($removalStart in $removalStarts) {
        [void]$builder.Append($Query, $copiedUpTo, $removalStart - $copiedUpTo)
        $copiedUpTo = $removalStart + $fragment.Length
    }

    [void]$builder.Append($Query, $copiedUpTo, $Query.Length - $copiedUpTo)
    $edited = $builder.ToString()

    if (-not (Test-SqlIdentityTypesPartEdit -OriginalQuery $Query -EditedQuery $edited -PartIndexes $targets -Edit Remove)) {
        return $null
    }

    return $edited
}

function Convert-SqlIdentityTypesUndoQuery {
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
        KeptParts        = 0
        ReasonCode       = ''
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

    $partsToRevert = [Collections.Generic.List[int]]::new()
    for ($partIndex = 0; $partIndex -lt $analysis.Parts.Count; $partIndex++) {
        $part = $analysis.Parts[$partIndex]
        if ($part.Kind -ne 'Sql' -or -not $part.HasSelection) {
            continue
        }

        if ($part.EndsWithDefaultSelection) {
            $partsToRevert.Add($partIndex)
        }
        else {
            $result.KeptParts++
        }
    }

    if ($partsToRevert.Count -eq 0) {
        return [PSCustomObject]$result
    }

    $spliced = Remove-SqlIdentityTypesSelection -Query $Query -PartIndexes $partsToRevert
    if ($null -eq $spliced) {
        $result.Status = 'Malformed'
        $result.ReasonCode = 'SpliceFailed'
        return [PSCustomObject]$result
    }

    $result.Status = 'Changed'
    $result.TransformedQuery = $spliced
    $result.ChangedParts = $partsToRevert.ToArray()
    return [PSCustomObject]$result
}

function Get-SqlIdentityTypesUndoPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [Collections.IEnumerable]$Rows
    )

    $changes = [Collections.Generic.List[object]]::new()
    $skipped = [Collections.Generic.List[object]]::new()
    $keptSelectionJobs = [Collections.Generic.List[object]]::new()
    $jobsInspected = 0

    foreach ($row in $Rows) {
        $jobsInspected++
        $transformed = Convert-SqlIdentityTypesUndoQuery -Query ([string]$row.Query)

        if ($transformed.Status -in @('Malformed', 'Invalid')) {
            $skipped.Add([PSCustomObject]@{
                Id = [Guid]$row.Id
                ReasonCode = $transformed.ReasonCode
            })

            continue
        }

        if ($transformed.KeptParts -gt 0) {
            $keptSelectionJobs.Add([PSCustomObject]@{
                Id = [Guid]$row.Id
                KeptParts = $transformed.KeptParts
            })
        }

        if ($transformed.Status -eq 'Changed') {
            $changes.Add([PSCustomObject]@{
                Id = [Guid]$row.Id
                BeforeQuery = [string]$row.Query
                AfterQuery = $transformed.TransformedQuery
                ChangedParts = $transformed.ChangedParts
            })
        }
    }

    $status = if ($changes.Count -gt 0) { 'Ready' } else { 'NoChanges' }

    return [PSCustomObject]@{
        Status = $status
        JobsInspected = $jobsInspected
        JobsToChange = $changes.Count
        JobsSkipped = $skipped.Count
        JobsWithKeptSelections = $keptSelectionJobs.Count
        Skipped = $skipped.ToArray()
        KeptSelectionJobs = $keptSelectionJobs.ToArray()
        Changes = $changes.ToArray()
    }
}

function Undo-SqlIdentityTypesMigration {
    <#
    .SYNOPSIS
    Reverses the SQL identity-types migration.

    .DESCRIPTION
    Removes ,"identityTypes":["users"] only where that exact text ends a SQL membership part, which is
    the form the migration writes. Any other selection is kept. Deployment never runs this script.
    Without -Apply it only previews; -Apply requires the service to be stopped.

    .EXAMPLE
    Undo-SqlIdentityTypesMigration -EnvironmentAbbreviation 'env' -SolutionAbbreviation 'gmm' -ConnectionString $connectionString -Apply
    #>
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

    Write-Host 'Start Undo-SqlIdentityTypesMigration'

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . (Join-Path $scriptsDirectory 'ReusableModules\Invoke-SqlOperationWithFirewallRetry.ps1')

    $connection = New-SqlIdentityTypesMigrationConnection -ConnectionString $ConnectionString
    $summary = [ordered]@{
        Status = 'NoChanges'
        JobsInspected = 0
        JobsToChange = 0
        JobsChanged = 0
        JobsSkipped = 0
        JobsWithKeptSelections = 0
        Conflicts = 0
        ReasonCode = ''
    }

    try {
        $rows = @(
            Invoke-SqlIdentityTypesOperation `
                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                -SolutionAbbreviation $SolutionAbbreviation `
                -Operation { Get-SqlIdentityTypesMigrationRows -Connection $connection }
        )

        $plan = Get-SqlIdentityTypesUndoPlan -Rows $rows
        $summary.JobsInspected = $plan.JobsInspected
        $summary.JobsToChange = $plan.JobsToChange
        $summary.JobsSkipped = $plan.JobsSkipped
        $summary.JobsWithKeptSelections = $plan.JobsWithKeptSelections

        Write-SqlIdentityTypesSkipSummary -Skipped $plan.Skipped -Activity 'undo'
        foreach ($keptSelectionJob in $plan.KeptSelectionJobs) {
            Write-Warning "SQL identity undo kept $($keptSelectionJob.KeptParts) identityTypes selection(s) in job $($keptSelectionJob.Id) because the migration did not write them; a release that does not accept identityTypes will reject this job until they are removed."
        }

        if ($plan.Status -eq 'NoChanges') {
            Write-Host "No SQL jobs contain the users-only selection in the form the migration writes. Inspected $($plan.JobsInspected); skipped $($plan.JobsSkipped)."
            return [PSCustomObject]$summary
        }

        if (-not $Apply) {
            $summary.Status = 'Ready'
            Write-Host "Preview: $($plan.JobsToChange) of $($plan.JobsInspected) job(s) would have the users-only selection removed. No changes were written."
            return [PSCustomObject]$summary
        }

        if (-not $PSCmdlet.ShouldProcess(
            "$($plan.JobsToChange) sync job(s) in dbo.SyncJobs",
            'Remove the users-only identity selection added by the migration')) {
            $summary.Status = 'Rejected'
            $summary.ReasonCode = 'ConfirmationDeclined'
            return [PSCustomObject]$summary
        }

        $outcome = Invoke-SqlIdentityTypesGuardedUpdate `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SolutionAbbreviation $SolutionAbbreviation `
            -Connection $connection `
            -Changes $plan.Changes

        $summary.Status = $outcome.Status
        $summary.ReasonCode = $outcome.ReasonCode
        $summary.JobsChanged = $outcome.JobsChanged
        $summary.Conflicts = $outcome.Conflicts

        if ($outcome.Status -eq 'Applied') {
            Write-Host "Removed the users-only selection from $($summary.JobsChanged) job(s)."
        }
        elseif ($outcome.Status -eq 'AlreadyApplied') {
            Write-Host 'Every proposed removal was already present after exact read-back.'
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

        Write-Host 'Finish Undo-SqlIdentityTypesMigration'
    }
}
