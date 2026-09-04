<#
.SYNOPSIS
    Fails GMM's regional resources over to the requested region.

.DESCRIPTION
    The orchestrator. It owns the order of the failover; each step script owns its own mechanic:

      Step 1  Stop GMM and drain it.
      --- point of no return ---
      Step 2  Fail over each regional tier, in order.
      Step 3  Start GMM.

    -WhatIf is a dry run of a real run, not a readiness check: a live run stops GMM first, so a
    rights or reachability problem surfaces with GMM already down. Confirm access before starting.

    Steps are idempotent, so a run that failed part way through can be re-run. The database tier is
    currently the only regional tier.

.PARAMETER TargetRegion
    The role to promote: Secondary fails over, Primary fails back. A role rather than an Azure
    region, because the two read-replica servers are named asymmetrically.

.PARAMETER Force
    Performs a data-loss promotion. Last resort, for when the active region is unreachable.

    KNOWN LIMITATION: Step 1 stops GMM through the WebApi, which runs only in the primary region, so
    a genuine regional outage halts the run before the promotion. Usable today only when the
    database is failing but compute still answers. Remove this note once compute is multi-region.

.EXAMPLE
    ./Invoke-GmmFailover.ps1 -EnvironmentAbbreviation <env> -TargetRegion Secondary -WhatIf

.EXAMPLE
    ./Invoke-GmmFailover.ps1 -EnvironmentAbbreviation <env> -TargetRegion Secondary

.EXAMPLE
    ./Invoke-GmmFailover.ps1 -EnvironmentAbbreviation <env> -TargetRegion Primary

.NOTES
    Exit codes: 0 success, 1 halted. There is no rollback: if the stop succeeded and a later step
    failed, GMM remains stopped - re-run the same command once the cause is understood.
#>
[CmdletBinding(SupportsShouldProcess)]
param (
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$EnvironmentAbbreviation,
    [Parameter(Mandatory = $true)][ValidateSet('Primary', 'Secondary')][string]$TargetRegion,
    [Parameter(Mandatory = $false)][ValidateNotNullOrEmpty()][string]$SolutionAbbreviation = 'gmm',
    [Parameter(Mandatory = $false)][switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

# Order matters. Regional tiers are failed over between the stop and the resume.
$script:Stop       = Join-Path $PSScriptRoot 'Invoke-GmmStop.ps1'
$script:Resume     = Join-Path $PSScriptRoot 'Invoke-GmmResume.ps1'
$script:TierScripts = @(
    (Join-Path $PSScriptRoot 'Invoke-DatabaseFailover.ps1')
    # Future tiers slot in here, in failover order.
)

$common = @{
    EnvironmentAbbreviation = $EnvironmentAbbreviation
    TargetRegion            = $TargetRegion
    SolutionAbbreviation    = $SolutionAbbreviation
}

function Invoke-FailoverStep {
    param (
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $false)][hashtable]$Extra = @{}
    )

    $arguments = $common.Clone()
    foreach ($key in $Extra.Keys) { $arguments[$key] = $Extra[$key] }
    if ($Force) { $arguments['Force'] = $true }

    return & $Path @arguments -WhatIf:$WhatIfPreference
}

function Write-ResultTable {
    param (
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Results
    )

    foreach ($r in $Results) {
        if (-not $r.Ok) {
            # Warn, not Error: Write-DeployLog -Level Error routes through Write-Error, which wraps
            # every row in a script/line/caret block and makes the summary unreadable.
            Write-DeployLog -Level Warn -Message ("{0,-20} FAILED - {1}" -f $r.Step, $r.Detail)
            foreach ($b in $r.Blockers) { Write-DeployLog -Level Warn -Message "                     - $b" }
        }
        else {
            Write-DeployLog -Level Success -Message ("{0,-20} {1}" -f $r.Step, $r.Detail)
        }
    }
}

try {
    if (-not (Get-Module -ListAvailable -Name 'Az.Sql')) {
        throw 'The Az PowerShell module was not found on this machine. Install Az before running a failover.'
    }

    if (-not (Get-AzContext)) {
        throw 'No Azure context is available. Run Connect-AzAccount before running a failover.'
    }

    $context = Get-GmmContext @common

    Write-DeployPhase -Name 'GMM region failover' -Event Begin
    Write-DeployLog -Level Info -Message "Environment:        $EnvironmentAbbreviation"
    Write-DeployLog -Level Info -Message "Target role:        $TargetRegion"
    Write-DeployLog -Level Info -Message "Mode:               $(if ($Force) { 'FORCED (allows data loss)' } else { 'Planned (lossless)' })"
    Write-DeployLog -Level Info -Message "WhatIf:             $WhatIfPreference"

    if ($Force) {
        Write-DeployLog -Level Warn -Message '*****************************************************************************'
        Write-DeployLog -Level Warn -Message '*  FORCED FAILOVER REQUESTED - THIS CAN LOSE ACKNOWLEDGED WRITES SILENTLY.  *'
        Write-DeployLog -Level Warn -Message '*  Use only when the active region is unreachable.                          *'
        Write-DeployLog -Level Warn -Message '*  Jobs left mid-run are reset afterwards.                                  *'
        Write-DeployLog -Level Warn -Message '*****************************************************************************'
    }

    # ----- stop ----------------------------------------------------------------
    $stopResult = Invoke-FailoverStep -Path $script:Stop
    if (-not $stopResult.Ok) {
        throw "HALTED: GMM did not reach the Stopped status ($($stopResult.Blockers -join '; ')). The failover was not attempted, because failing over under live traffic makes every write fail against the newly read-only database."
    }

    Write-DeployLog -Level Warn -Message 'Point of no return: GMM is stopped and the promotion follows. Fail back to return.'

    # ----- tiers ---------------------------------------------------------------
    $tierResults = @()
    foreach ($tier in $script:TierScripts) {
        $result = Invoke-FailoverStep -Path $tier
        $tierResults += $result
        if (-not $result.Ok) {
            throw "HALTED at $($result.Step): $($result.Blockers -join '; ')"
        }
    }

    # Only a forced failover records a boundary, so on a planned run no tier carries one and the
    # lookup must tolerate finding nothing rather than indexing into a null result.
    $boundaryResult = $tierResults | Where-Object { $_.State.ContainsKey('DataGoodThroughUtc') } | Select-Object -First 1
    $dataGoodThrough = if ($boundaryResult) { $boundaryResult.State['DataGoodThroughUtc'] } else { $null }
    $replicationLag = if ($boundaryResult -and $boundaryResult.State.ContainsKey('ReplicationLagSeconds')) { $boundaryResult.State['ReplicationLagSeconds'] } else { $null }

    # ----- resume --------------------------------------------------------------
    $resumeResult = Invoke-FailoverStep -Path $script:Resume
    if (-not $resumeResult.Ok) {
        throw "HALTED at $($resumeResult.Step): $($resumeResult.Blockers -join '; ')"
    }

    # ----- summary -------------------------------------------------------------
    Write-DeployPhase -Name 'Summary' -Event Begin
    Write-ResultTable -Results (@($stopResult) + $tierResults + @($resumeResult))

    Write-DeployLog -Level Info -Message "Active region role: $TargetRegion"
    Write-DeployLog -Level Info -Message "Primary server:     $($context.PromotionServerName)"
    Write-DeployLog -Level Info -Message "Failover mode:      $(if ($Force) { 'forced' } else { 'planned' })"
    if ($Force) {
        $boundaryText = if ($null -ne $dataGoodThrough) {
                            $instant = ([datetime]$dataGoodThrough).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
                            if ($null -ne $replicationLag) { "$instant ($replicationLag seconds behind at promotion)" } else { $instant }
                        }
                        elseif ($WhatIfPreference) { 'not read (WhatIf)' }
                        else { 'unknown - could not be read' }
        Write-DeployLog -Level Info -Message "DataGoodThrough:    $boundaryText"
    }
    Write-DeployPhase -Name 'Summary' -Event End
    Write-DeployPhase -Name 'GMM region failover' -Event End
    Write-DeployResult -Status SUCCESS

    exit 0
}
catch {
    Write-DeployError -Category 'RegionFailover' -Message $_.Exception.Message
    Write-DeployLog -Level Warn -Message 'No rollback was attempted. If GMM was already stopped, it remains stopped.'
    Write-DeployLog -Level Warn -Message 'Re-run the same command once the cause is understood: steps that already reached their target state do nothing.'
    Write-DeployResult -Status FAILED -Reason $_.Exception.Message
    exit 1
}
