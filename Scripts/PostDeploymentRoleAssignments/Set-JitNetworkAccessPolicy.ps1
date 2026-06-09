$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Creates or updates a JIT network access policy for the GMM jumpbox VM.

.DESCRIPTION
Applies a Just-In-Time (JIT) network access policy to the jumpbox VM in the
networking resource group. This enables time-limited SSH (22) and RDP (3389)
access through Azure Security Center JIT requests.

This script uses the Azure REST API via Invoke-AzRestMethod since there is no
native Az PowerShell cmdlet for JIT network access policies.

Prerequisites:
  - The Microsoft.Security resource provider must be registered on the subscription.
  - The caller must have Microsoft.Security/locations/jitNetworkAccessPolicies/write
    permission on the networking resource group.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution (e.g. 'gmm').

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment (e.g. 'int', 'ua', 'prodv2').

.PARAMETER AllowedSourceAddressPrefix
Allowed source address prefix for JIT access requests. Use '*' to allow any source,
or restrict to a CIDR like '10.0.0.0/24'. Default: '*'.

.PARAMETER MaxRequestAccessDuration
Maximum duration for a JIT access request in ISO 8601 format. Default: 'PT3H' (3 hours).

.EXAMPLE
Set-JitNetworkAccessPolicy -SolutionAbbreviation "gmm" `
                            -EnvironmentAbbreviation "int" `
                            -AllowedSourceAddressPrefix "10.0.0.0/24" `
                            -Verbose
#>

function Set-JitNetworkAccessPolicy {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string] $AllowedSourceAddressPrefix,
        [Parameter(Mandatory = $false)]
        [string] $MaxRequestAccessDuration = 'PT3H'
    )

    $resourceGroupName = "$SolutionAbbreviation-networking-$EnvironmentAbbreviation"
    $vmName = "$SolutionAbbreviation-networking-$EnvironmentAbbreviation-management-vm"

    Write-Host "Looking up VM '$vmName' in resource group '$resourceGroupName'..."
    $vm = Get-AzVM -ResourceGroupName $resourceGroupName -Name $vmName -ErrorAction SilentlyContinue
    if (-not $vm) {
        Write-Host "VM '$vmName' not found in resource group '$resourceGroupName'. Skipping JIT policy."
        return
    }

    $subscriptionId = (Get-AzContext).Subscription.Id
    $location = $vm.Location

    $apiPath = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroupName/providers/Microsoft.Security/locations/$location/jitNetworkAccessPolicies/default?api-version=2020-01-01"

    $body = @{
        kind       = 'Basic'
        properties = @{
            virtualMachines = @(
                @{
                    id    = $vm.Id
                    ports = @(
                        @{
                            number                     = 22
                            protocol                   = 'TCP'
                            allowedSourceAddressPrefix = $AllowedSourceAddressPrefix
                            maxRequestAccessDuration   = $MaxRequestAccessDuration
                        }
                        @{
                            number                     = 3389
                            protocol                   = 'TCP'
                            allowedSourceAddressPrefix = $AllowedSourceAddressPrefix
                            maxRequestAccessDuration   = $MaxRequestAccessDuration
                        }
                    )
                }
            )
        }
    } | ConvertTo-Json -Depth 10

    Write-Host "Applying JIT network access policy for VM '$vmName' in '$location'..."
    $response = Invoke-AzRestMethod -Method PUT -Path $apiPath -Payload $body

    if ($response.StatusCode -in @(200, 201)) {
        Write-Host "JIT policy applied successfully (HTTP $($response.StatusCode))."
    }
    else {
        $errorDetail = $response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
        $message = if ($errorDetail.error.message) { $errorDetail.error.message } else { $response.Content }
        throw "Failed to apply JIT policy. HTTP $($response.StatusCode): $message"
    }
}
