<#
.SYNOPSIS
Ad hoc cleanup script to remove function authentication from all function apps in compute resource groups.

.PARAMETER SolutionAbbreviation
Solution abbreviation used in resource-group naming (for example, gmm).

.PARAMETER EnvironmentAbbreviation
A single environment abbreviation (for example, dl).

.PARAMETER WhatIf
Preview mode. Shows what would change without writing updates.

.PARAMETER SkipConfirmation
Skips confirmation prompt.

.EXAMPLE
Remove-FunctionAuthenticationFromCompute -SolutionAbbreviation gmm -EnvironmentAbbreviation dl -WhatIf

.EXAMPLE
Remove-FunctionAuthenticationFromCompute -SolutionAbbreviation gmm -EnvironmentAbbreviation dl -SkipConfirmation
#>

$ErrorActionPreference = "Stop"

function Remove-FunctionAuthenticationFromCompute {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $True)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $False)]
        [switch] $WhatIf,
        [Parameter(Mandatory = $False)]
        [switch] $SkipConfirmation
    )

    function Get-FunctionAppNames {
        param(
            [Parameter(Mandatory = $True)]
            [string] $ResourceGroupName
        )

        return @(Invoke-WithRetry `
            -Operation {
                Get-AzResource `
                    -ResourceGroupName $ResourceGroupName `
                    -ResourceType "Microsoft.Web/sites" `
                    -ErrorAction SilentlyContinue |
                    Where-Object { $_.Kind -like "*functionapp*" } |
                    Select-Object -ExpandProperty Name
            } `
            -OperationName "List function apps in $ResourceGroupName" `
            -MaxAttempts 3 -BaseDelaySeconds 2)
    }

    function Invoke-EnvironmentCleanup {
        param(
            [Parameter(Mandatory = $True)]
            [string] $SolutionAbbreviation,
            [Parameter(Mandatory = $True)]
            [string] $EnvironmentAbbreviation,
            [Parameter(Mandatory = $False)]
            [switch] $WhatIf,
            [Parameter(Mandatory = $False)]
            [switch] $SkipConfirmation
        )

        $subscriptionName = "MSFT-STSolution-$EnvironmentAbbreviation"
        $subscription = Get-AzSubscription -SubscriptionName $subscriptionName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $subscription) {
            throw "Subscription '$subscriptionName' was not found."
        }

        $null = Set-AzContext -SubscriptionId $subscription.Id -ErrorAction Stop

        $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
        $resourceGroup = Get-AzResourceGroup -Name $computeResourceGroup -ErrorAction SilentlyContinue
        if ($null -eq $resourceGroup) {
            throw "Resource group '$computeResourceGroup' was not found in subscription '$subscriptionName'."
        }

        $functionApps = Get-FunctionAppNames -ResourceGroupName $computeResourceGroup
        if ($functionApps.Count -eq 0) {
            Write-Host "[$EnvironmentAbbreviation] No function apps found in $computeResourceGroup."
            return [pscustomobject]@{
                Environment = $EnvironmentAbbreviation
                Updated     = 0
                Skipped     = 0
                Errors      = 0
            }
        }

        Write-Host "[$EnvironmentAbbreviation] Found $($functionApps.Count) function app(s) in $computeResourceGroup."

        if (-not $WhatIf -and -not $SkipConfirmation) {
            $confirm = Read-Host "[$EnvironmentAbbreviation] Remove function auth from all function apps in '$computeResourceGroup'? (y/N)"
            if ($confirm -ne "y" -and $confirm -ne "Y") {
                Write-Host "[$EnvironmentAbbreviation] Skipped by user."
                return [pscustomobject]@{
                    Environment = $EnvironmentAbbreviation
                    Updated     = 0
                    Skipped     = $functionApps.Count
                    Errors      = 0
                }
            }
        }

        $updated = 0
        $skipped = 0
        $errors = 0

        foreach ($functionAppName in $functionApps) {
            try {
                $path = "/subscriptions/$($subscription.Id)/resourceGroups/$computeResourceGroup/providers/Microsoft.Web/sites/$functionAppName/config/authsettingsV2?api-version=2022-09-01"
                try {
                    $currentResponse = Invoke-AzRestMethod -Method GET -Path $path
                    $current = $currentResponse.Content | ConvertFrom-Json -Depth 20
                }
                catch {
                    if ($_.Exception.Message -match "404 \(Not Found\)") {
                        Write-Host "[$EnvironmentAbbreviation] [$functionAppName] authsettingsV2 not found (already removed)."
                        $skipped++
                        continue
                    }

                    throw
                }

                $isEnabled = $current.properties.platform.enabled
                $identityProviders = $current.properties.identityProviders
                $hasAadProvider = $false
                if ($null -ne $identityProviders -and ($identityProviders.PSObject.Properties.Name -contains "azureActiveDirectory")) {
                    $hasAadProvider = $true
                }

                if ($isEnabled -ne $true -and -not $hasAadProvider) {
                    Write-Host "[$EnvironmentAbbreviation] [$functionAppName] already cleaned."
                    $skipped++
                    continue
                }

                if ($WhatIf) {
                    Write-Host "[$EnvironmentAbbreviation] [$functionAppName] [WhatIf] Would disable auth and remove AAD provider config."
                    $updated++
                    continue
                }

                $current.properties.platform.enabled = $false
                if ($hasAadProvider) {
                    $null = $identityProviders.PSObject.Properties.Remove("azureActiveDirectory")
                    if ($identityProviders.PSObject.Properties.Count -eq 0) {
                        $null = $current.properties.PSObject.Properties.Remove("identityProviders")
                    }
                }

                $body = $current | ConvertTo-Json -Depth 20
                $null = Invoke-WithRetry `
                    -Operation { Invoke-AzRestMethod -Method PUT -Path $path -Payload $body } `
                    -OperationName "Update authsettingsV2 for $functionAppName" `
                    -MaxAttempts 3 -BaseDelaySeconds 2

                Write-Host "[$EnvironmentAbbreviation] [$functionAppName] updated."
                $updated++
            }
            catch {
                Write-Host "[$EnvironmentAbbreviation] [$functionAppName] failed: $($_.Exception.Message)"
                $errors++
            }
        }

        return [pscustomobject]@{
            Environment = $EnvironmentAbbreviation
            Updated     = $updated
            Skipped     = $skipped
            Errors      = $errors
        }
    }

    Write-Verbose "Remove-FunctionAuthenticationFromCompute starting..."

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + "\Scripts\Add-AzAccountIfNeeded.ps1")
    . ($scriptsDirectory + "\Scripts\ReusableModules\Invoke-WithRetry.ps1")
    Add-AzAccountIfNeeded | Out-Null

    function Invoke-CleanupExecution {
        param(
            [Parameter(Mandatory = $True)]
            [string] $SolutionAbbreviation,
            [Parameter(Mandatory = $True)]
            [string] $EnvironmentAbbreviation,
            [Parameter(Mandatory = $False)]
            [switch] $WhatIf,
            [Parameter(Mandatory = $False)]
            [switch] $SkipConfirmation
        )

        $normalizedEnvironment = $EnvironmentAbbreviation.ToLowerInvariant().Trim()
        if ([string]::IsNullOrWhiteSpace($normalizedEnvironment)) {
            throw "EnvironmentAbbreviation must be provided."
        }

        Write-Host ""
        Write-Host "Ad hoc function-auth cleanup"
        Write-Host "Solution abbreviation: $SolutionAbbreviation"
        Write-Host "Environment abbreviation: $normalizedEnvironment"
        if ($WhatIf) {
            Write-Host "Mode: WhatIf"
        }

        $result = Invoke-EnvironmentCleanup `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $normalizedEnvironment `
            -WhatIf:$WhatIf `
            -SkipConfirmation:$SkipConfirmation

        Write-Host ""
        Write-Host "Summary:"
        @($result) | Format-Table -AutoSize

        $totalErrors = $result.Errors
        if ($totalErrors -gt 0) {
            Write-Warning "$totalErrors error(s) encountered."
        }
    }

    Invoke-CleanupExecution `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -WhatIf:$WhatIf `
        -SkipConfirmation:$SkipConfirmation

    Write-Verbose "Remove-FunctionAuthenticationFromCompute completed."
}
