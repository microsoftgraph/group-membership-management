<#
.SYNOPSIS
    Stops GMM in the currently active region. Step 1 of the region failover.

.DESCRIPTION
    Calls the WebApi Operations Stop endpoint and waits for the service to report Stopped. This is
    a full drain - function apps, queues, topics and in-progress jobs - so it takes several minutes.

    It must complete before the promotion: a SQL failover underneath a running GMM produces
    'database is read-only' errors that Entity Framework's EnableRetryOnFailure will not retry.

.EXAMPLE
    ./Invoke-GmmStop.ps1 -EnvironmentAbbreviation <env> -TargetRegion Secondary
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

$stepName = 'StopGmm'

try {
    $context = Get-GmmContext -EnvironmentAbbreviation $EnvironmentAbbreviation -TargetRegion $TargetRegion -SolutionAbbreviation $SolutionAbbreviation
    $baseUrl = Get-OperationsBaseUrl -Context $context

    Write-DeployPhase -Name 'Step 1 - Stop GMM' -Event Begin
    Write-DeployLog -Level Info -Message "Environment: $EnvironmentAbbreviation | Target role: $TargetRegion"

    $token = Get-WebApiAccessToken -PrereqsKeyVaultName $context.PrereqsKeyVaultName
    $status = Get-GmmServiceStatus -BaseUrl $baseUrl -Token $token
    $token = $null

    if ($status -eq 1) {
        Write-DeployLog -Level Info -Message 'GMM is already Stopped; nothing to do.'
        Write-DeployPhase -Name 'Step 1 - Stop GMM' -Event End
        return New-StepResult -Step $stepName -State @{ ServiceStatus = 'Stopped' } -Detail 'Already stopped; nothing to do.'
    }

    $null = Invoke-GmmOperation -OperationName 'Stop' -ExpectedStatus 1 -BaseUrl $baseUrl `
        -PrereqsKeyVaultName $context.PrereqsKeyVaultName -WhatIf:$WhatIfPreference

    Write-DeployPhase -Name 'Step 1 - Stop GMM' -Event End
    return New-StepResult -Step $stepName -State @{ ServiceStatus = 'Stopped' } -Detail 'GMM stopped.'
}
catch {
    return New-StepResult -Step $stepName -Ok $false -Blockers @($_.Exception.Message) -Detail 'Stop failed.'
}
