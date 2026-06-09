$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Ensures the Microsoft Cloud Security Benchmark policy initiative is enabled on the subscription.

.DESCRIPTION
Checks whether the Microsoft Cloud Security Benchmark (MCSB) policy initiative is
assigned at the current subscription scope. If not assigned, creates an audit-only
assignment. If assigned but set to DoNotEnforce, updates it to Default enforcement.

MCSB is Microsoft's recommended security baseline. Enabling it allows Defender for
Cloud to evaluate resources against ~200 security controls and surface recommendations
via the Secure Score dashboard. This is audit-only — it does not block deployments
or modify resources.

This script uses the Azure REST API via Invoke-AzRestMethod for version-independent
policy assignment management.

Prerequisites:
  - The caller must have Microsoft.Authorization/policyAssignments/write permission
    (Owner, Security Admin, or Resource Policy Contributor role).

.PARAMETER SolutionAbbreviation
The abbreviation for your solution (e.g. 'gmm'). Accepted for calling convention
consistency — the benchmark is subscription-scoped and does not use this value.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment (e.g. 'int', 'ua', 'prodv2').
Accepted for calling convention consistency — the benchmark is subscription-scoped
and does not use this value.

.EXAMPLE
Set-CloudSecurityBenchmarkPolicy -SolutionAbbreviation "gmm" `
                                  -EnvironmentAbbreviation "int" `
                                  -Verbose
#>

function Set-CloudSecurityBenchmarkPolicy {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string] $EnvironmentAbbreviation
    )

    $benchmarkDefinitionId = "1f3afdf9-d0c9-4c3d-847f-89da613e70a8"
    $assignmentName = "SecurityBenchmark"
    $subscriptionId = (Get-AzContext).Subscription.Id
    $apiVersion = "2022-06-01"

    $assignmentPath = "/subscriptions/$subscriptionId/providers/Microsoft.Authorization/policyAssignments/${assignmentName}?api-version=$apiVersion"

    Write-Host "Checking Microsoft Cloud Security Benchmark policy assignment on subscription '$subscriptionId'..."

    $checkResponse = Invoke-AzRestMethod -Method GET -Path $assignmentPath

    if ($checkResponse.StatusCode -eq 200) {
        $existing = $checkResponse.Content | ConvertFrom-Json
        if ($existing.properties.enforcementMode -eq 'DoNotEnforce') {
            Write-Host "Benchmark assigned but not enforced. Enabling enforcement..."
            $existing.properties.enforcementMode = 'Default'
            $body = $existing | ConvertTo-Json -Depth 10
            $response = Invoke-AzRestMethod -Method PUT -Path $assignmentPath -Payload $body

            if ($response.StatusCode -in @(200, 201)) {
                Write-Host "Enforcement enabled (HTTP $($response.StatusCode))."
            }
            else {
                $errorDetail = $response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
                $message = if ($errorDetail.error.message) { $errorDetail.error.message } else { $response.Content }
                throw "Failed to enable benchmark enforcement. HTTP $($response.StatusCode): $message"
            }
        }
        else {
            Write-Host "Microsoft Cloud Security Benchmark is already enabled."
        }
    }
    else {
        Write-Host "No existing assignment found. Creating Microsoft Cloud Security Benchmark assignment..."
        $body = @{
            properties = @{
                displayName        = "Microsoft Cloud Security Benchmark"
                policyDefinitionId = "/providers/Microsoft.Authorization/policySetDefinitions/$benchmarkDefinitionId"
                enforcementMode    = "Default"
            }
        } | ConvertTo-Json -Depth 10

        $response = Invoke-AzRestMethod -Method PUT -Path $assignmentPath -Payload $body

        if ($response.StatusCode -in @(200, 201)) {
            Write-Host "Microsoft Cloud Security Benchmark assigned and enabled (HTTP $($response.StatusCode))."
        }
        else {
            $errorDetail = $response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
            $message = if ($errorDetail.error.message) { $errorDetail.error.message } else { $response.Content }
            throw "Failed to create benchmark assignment. HTTP $($response.StatusCode): $message"
        }
    }
}
