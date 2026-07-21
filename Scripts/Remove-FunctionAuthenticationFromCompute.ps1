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
        [Parameter(Mandatory = $True)]
        [string] $SubscriptionName,
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
            [string] $SubscriptionName,
            [Parameter(Mandatory = $True)]
            [string] $SolutionAbbreviation,
            [Parameter(Mandatory = $True)]
            [string] $EnvironmentAbbreviation,
            [Parameter(Mandatory = $False)]
            [switch] $WhatIf,
            [Parameter(Mandatory = $False)]
            [switch] $SkipConfirmation
        )

        function Get-AuthAssessment {
            param(
                [Parameter(Mandatory = $True)]
                [pscustomobject] $AuthSettings
            )

            $platformEnabled = $false
            $globalValidation = $AuthSettings.properties.globalValidation
            $identityProviders = $AuthSettings.properties.identityProviders
            $requiresAuthentication = $false
            $unauthenticatedClientAction = ""
            $hasAadRegistrationClientId = $false

            if ($null -ne $AuthSettings.properties.platform -and ($AuthSettings.properties.platform.PSObject.Properties.Name -contains "enabled")) {
                $platformEnabled = [bool]$AuthSettings.properties.platform.enabled
            }

            if ($null -ne $globalValidation) {
                if ($globalValidation.PSObject.Properties.Name -contains "requireAuthentication") {
                    $requiresAuthentication = [bool]$globalValidation.requireAuthentication
                }

                if ($globalValidation.PSObject.Properties.Name -contains "unauthenticatedClientAction") {
                    $unauthenticatedClientAction = [string]$globalValidation.unauthenticatedClientAction
                }
            }

            if ($null -ne $identityProviders -and ($identityProviders.PSObject.Properties.Name -contains "azureActiveDirectory")) {
                $aadProvider = $identityProviders.azureActiveDirectory
                if ($null -ne $aadProvider -and $null -ne $aadProvider.registration -and ($aadProvider.registration.PSObject.Properties.Name -contains "clientId")) {
                    $hasAadRegistrationClientId = -not [string]::IsNullOrWhiteSpace([string]$aadProvider.registration.clientId)
                }
            }

            $isDesiredState = (-not $platformEnabled) -and (-not $requiresAuthentication) -and ($unauthenticatedClientAction -eq "AllowAnonymous") -and (-not $hasAadRegistrationClientId)

            return [pscustomobject]@{
                IsDesiredState               = $isDesiredState
                HasAadRegistrationClientId   = $hasAadRegistrationClientId
                PlatformEnabled              = $platformEnabled
                RequiresAuthentication       = $requiresAuthentication
                UnauthenticatedClientAction  = $unauthenticatedClientAction
            }
        }

        $subscription = Get-AzSubscription -SubscriptionName $subscriptionName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $subscription) {
            throw "Subscription '$subscriptionName' was not found."
        }

        $currentContext = Get-AzContext -ErrorAction SilentlyContinue
        if ($null -eq $currentContext -or $null -eq $currentContext.Subscription -or $currentContext.Subscription.Id -ne $subscription.Id) {
            $null = Set-AzContext -SubscriptionId $subscription.Id -ErrorAction Stop
        }

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
                Mode        = if ($WhatIf) { "WhatIf" } else { "Apply" }
                Updated     = 0
                WouldUpdate = 0
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
                    Mode        = if ($WhatIf) { "WhatIf" } else { "Apply" }
                    Updated     = 0
                    WouldUpdate = 0
                    Skipped     = $functionApps.Count
                    Errors      = 0
                }
            }
        }

        $updated = 0
        $wouldUpdate = 0
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

                $assessment = Get-AuthAssessment -AuthSettings $current

                if ($assessment.IsDesiredState) {
                    Write-Host "[$EnvironmentAbbreviation] [$functionAppName] already cleaned (normalized target state)."
                    $skipped++
                    continue
                }

                if ($WhatIf) {
                    Write-Host "[$EnvironmentAbbreviation] [$functionAppName] [WhatIf] Would set platform.enabled=false, requireAuthentication=false, unauthenticatedClientAction=AllowAnonymous, and clear AAD registration.clientId (current platformEnabled=$($assessment.PlatformEnabled), requireAuthentication=$($assessment.RequiresAuthentication), unauthenticatedClientAction='$($assessment.UnauthenticatedClientAction)', hasAadRegistrationClientId=$($assessment.HasAadRegistrationClientId))"
                    $wouldUpdate++
                    continue
                }

                if ($null -eq $current.properties.platform) {
                    $current.properties | Add-Member -NotePropertyName platform -NotePropertyValue ([pscustomobject]@{})
                }

                $platform = $current.properties.platform
                if ($platform.PSObject.Properties.Name -contains "enabled") {
                    $platform.enabled = $false
                }
                else {
                    $platform | Add-Member -NotePropertyName enabled -NotePropertyValue $false
                }

                if ($null -eq $current.properties.globalValidation) {
                    $current.properties | Add-Member -NotePropertyName globalValidation -NotePropertyValue ([pscustomobject]@{})
                }

                $globalValidation = $current.properties.globalValidation

                if ($globalValidation.PSObject.Properties.Name -contains "requireAuthentication") {
                    $globalValidation.requireAuthentication = $false
                }
                else {
                    $globalValidation | Add-Member -NotePropertyName requireAuthentication -NotePropertyValue $false
                }

                if ($globalValidation.PSObject.Properties.Name -contains "unauthenticatedClientAction") {
                    $globalValidation.unauthenticatedClientAction = "AllowAnonymous"
                }
                else {
                    $globalValidation | Add-Member -NotePropertyName unauthenticatedClientAction -NotePropertyValue "AllowAnonymous"
                }

                $identityProviders = $current.properties.identityProviders
                if ($null -ne $identityProviders -and ($identityProviders.PSObject.Properties.Name -contains "azureActiveDirectory")) {
                    $aadProvider = $identityProviders.azureActiveDirectory
                    if ($null -ne $aadProvider) {
                        if ($null -eq $aadProvider.registration) {
                            $aadProvider | Add-Member -NotePropertyName registration -NotePropertyValue ([pscustomobject]@{})
                        }

                        if ($aadProvider.registration.PSObject.Properties.Name -contains "clientId") {
                            $aadProvider.registration.clientId = ""
                        }
                        else {
                            $aadProvider.registration | Add-Member -NotePropertyName clientId -NotePropertyValue ""
                        }
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
            Mode        = if ($WhatIf) { "WhatIf" } else { "Apply" }
            Updated     = $updated
            WouldUpdate = $wouldUpdate
            Skipped     = $skipped
            Errors      = $errors
        }
    }

    Write-Verbose "Remove-FunctionAuthenticationFromCompute starting..."

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + "\Scripts\ReusableModules\Invoke-WithRetry.ps1")

    function Invoke-CleanupExecution {
        param(
            [Parameter(Mandatory = $True)]
            [string] $SubscriptionName,
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
        Write-Host "Subscription name: $SubscriptionName"
        Write-Host "Solution abbreviation: $SolutionAbbreviation"
        Write-Host "Environment abbreviation: $normalizedEnvironment"
        if ($WhatIf) {
            Write-Host "Mode: WhatIf"
        }

        $result = Invoke-EnvironmentCleanup `
            -SubscriptionName $SubscriptionName `
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
        -SubscriptionName $SubscriptionName `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -WhatIf:$WhatIf `
        -SkipConfirmation:$SkipConfirmation

    Write-Verbose "Remove-FunctionAuthenticationFromCompute completed."
}
