<#
.SYNOPSIS
    Restarts GMM in the newly active region. Step 3 of the region failover.

.DESCRIPTION
    Calls the WebApi Operations Start endpoint and waits for the service to report Running.

    The database tier must have verified the read-write listener first. A forced promotion returns
    in about 14 seconds, inside the listener's 30 second DNS TTL, so starting GMM before the CNAME
    has moved can send writes to the demoted, read-only server.

.EXAMPLE
    ./Invoke-GmmResume.ps1 -EnvironmentAbbreviation <env> -TargetRegion Secondary
#>
[CmdletBinding(SupportsShouldProcess)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', 'Force',
    Justification = 'Part of the uniform step contract; the orchestrator passes the same parameters to every step.')]
param (
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$EnvironmentAbbreviation,
    [Parameter(Mandatory = $true)][ValidateSet('Primary', 'Secondary')][string]$TargetRegion,
    [Parameter(Mandatory = $false)][ValidateNotNullOrEmpty()][string]$SolutionAbbreviation = 'gmm',
    [Parameter(Mandatory = $false)][switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$stepName = 'ResumeGmm'

try {
    $context = Get-GmmContext -EnvironmentAbbreviation $EnvironmentAbbreviation -TargetRegion $TargetRegion -SolutionAbbreviation $SolutionAbbreviation
    $baseUrl = Get-OperationsBaseUrl -Context $context

    Write-DeployPhase -Name 'Step 3 - Restart GMM' -Event Begin
    Write-DeployLog -Level Info -Message "Environment: $EnvironmentAbbreviation | Active region: $TargetRegion"

    # The database step repoints the WebApi's own read setting, which recycles the app, so this
    # first call can land while it is still restarting. Retry harder than the default: failing here
    # halts the run with GMM stopped and the database already promoted.
    $token = Get-WebApiAccessToken -PrereqsKeyVaultName $context.PrereqsKeyVaultName
    $status = Get-GmmServiceStatus -BaseUrl $baseUrl -Token $token -MaxAttempts 6 -BaseDelaySeconds 5
    $token = $null

    if ($status -eq 0) {
        Write-DeployLog -Level Info -Message 'GMM is already Running; nothing to do.'
        Write-DeployPhase -Name 'Step 3 - Restart GMM' -Event End
        return New-StepResult -Step $stepName -State @{ ServiceStatus = 'Running' } -Detail 'Already running; nothing to do.'
    }

    $null = Invoke-GmmOperation -OperationName 'Start' -ExpectedStatus 0 -BaseUrl $baseUrl `
        -PrereqsKeyVaultName $context.PrereqsKeyVaultName -WhatIf:$WhatIfPreference

    Write-DeployPhase -Name 'Step 3 - Restart GMM' -Event End
    return New-StepResult -Step $stepName -State @{ ServiceStatus = 'Running' } -Detail 'GMM running.'
}
catch {
    return New-StepResult -Step $stepName -Ok $false -Blockers @($_.Exception.Message) -Detail 'Restart failed.'
}
