$ErrorActionPreference = "Stop"

# Global variable to control Azure cmdlet warning suppression
$Global:SuppressAzureWarnings = $false

if ($Global:SuppressAzureWarnings) {
    $WarningPreference = "SilentlyContinue"
}

<#
.SYNOPSIS
Removes specific Azure Function Apps and their associated resources including service plans, storage accounts, and Service Bus subscriptions.

.DESCRIPTION
This script removes predefined function apps (GraphUpdater-medium, GraphUpdater-onboarding,
MessageSplitter-m1, MessageSplitter-o1) and their associated service plans, storage accounts,
and Service Bus subscriptions.

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (e.g., "gmm")

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment (e.g., "dev", "prod")

.PARAMETER WhatIf
SIMULATION MODE: Shows which functions would be removed and what actions would be taken,
without making any actual changes. Use this to safely preview the deletion plan.

.PARAMETER SkipConfirmation
Skip confirmation prompts and proceed automatically. Use for unattended/automated deployments.

.EXAMPLE
Remove-MultiLaneResources -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev" -WhatIf

.EXAMPLE
Remove-MultiLaneResources -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev" -SkipConfirmation

.NOTES
The script will remove the following function apps (if they exist):
- {solution}-compute-{env}-GraphUpdater-medium
- {solution}-compute-{env}-GraphUpdater-onboarding
- {solution}-compute-{env}-MessageSplitter-m1
- {solution}-compute-{env}-MessageSplitter-o1
#>

function Get-WarningAction {
    # Helper function to get warning action based on global setting
    if ($Global:SuppressAzureWarnings) {
        return "SilentlyContinue"
    }
    else {
        return "Continue"
    }
}

$ScriptsDirectory = Split-Path $PSScriptRoot -Parent
. ($ScriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')
. ($ScriptsDirectory + '/FunctionAppCompat.ps1')

function Get-TeamsChannelUpdaterSubscriptions {
    param(
        [string]$NamespaceName,
        [string]$ResourceGroupName,
        [string]$TopicName = "membershipupdaters"
    )

    try {
        $allSubscriptions = Get-AzServiceBusSubscription -ResourceGroupName $ResourceGroupName -NamespaceName $NamespaceName -TopicName $TopicName -ErrorAction SilentlyContinue
        if ($allSubscriptions) {
            $teamsChannelSubscriptions = $allSubscriptions | Where-Object { $_.Name -match '^TeamsChannelUpdater_.*_\d+$' }
            # Filter out any null or empty names and return as array
            $subscriptionNames = $teamsChannelSubscriptions.Name | Where-Object { -not [string]::IsNullOrEmpty($_) }
            return @($subscriptionNames)
        }
        return @()
    }
    catch {
        Write-Warning "Failed to get TeamsChannelUpdater subscriptions: $($_.Exception.Message)"
        return @()
    }
}

function Get-FilteredGraphUpdaterSubscriptions {
    param(
        [string]$NamespaceName,
        [string]$ResourceGroupName,
        [string]$TopicName = "membershipupdaters"
    )

    $filteredSubscriptions = @()

    try {
        # Get all actual subscriptions from the topic
        $allSubscriptions = Get-AzServiceBusSubscription -ResourceGroupName $ResourceGroupName -NamespaceName $NamespaceName -TopicName $TopicName -ErrorAction SilentlyContinue
        if ($allSubscriptions) {
            # Filter for GraphUpdater subscriptions that actually exist
            $graphUpdaterSubscriptions = $allSubscriptions | Where-Object { $_.Name -match '^GraphUpdater_.*_\d+$' }

            foreach ($subscriptionObj in $graphUpdaterSubscriptions) {
                $subscriptionName = $subscriptionObj.Name

                # Special handling for GraphUpdater_large_1 - check session requirement
                if ($subscriptionName -eq "GraphUpdater_large_1") {
                    Write-Host "    🔍 Checking session status for subscription GraphUpdater_large_1..." -ForegroundColor Gray
                    Write-Host "    📋 Subscription found. RequiresSession = $($subscriptionObj.RequiresSession)" -ForegroundColor Gray
                    if ($subscriptionObj.RequiresSession -eq $true) {
                        Write-Host "    ⏭️  Excluding GraphUpdater_large_1 (subscription is session-enabled)" -ForegroundColor Yellow
                        continue
                    }
                    else {
                        Write-Host "    ✅ GraphUpdater_large_1 will be included (subscription is not session-enabled)" -ForegroundColor Green
                    }
                }
                # Special handling for GraphUpdater_small_1 - always skip it
                elseif ($subscriptionName -eq "GraphUpdater_small_1") {
                    Write-Host "    ⏭️  Excluding GraphUpdater_small_1 (keeping this subscription)" -ForegroundColor Yellow
                    continue
                }

                $filteredSubscriptions += $subscriptionName
            }

            Write-Host "    📊 Found $($graphUpdaterSubscriptions.Count) GraphUpdater subscriptions, $($filteredSubscriptions.Count) will be removed" -ForegroundColor Gray
        }
        else {
            Write-Host "    ⚠️  No subscriptions found in topic $TopicName" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Warning "Failed to get GraphUpdater subscriptions: $($_.Exception.Message)"
        return @()
    }

    return $filteredSubscriptions
}

function Get-MessageSplitterSubscriptions {
    param(
        [string]$NamespaceName,
        [string]$ResourceGroupName,
        [string]$TopicName = "messagesplitter"
    )

    $targetSubscriptions = @("Medium", "Onboarding")
    $existingSubscriptions = @()

    try {
        # Get all actual subscriptions from the messagesplitter topic
        $allSubscriptions = Get-AzServiceBusSubscription -ResourceGroupName $ResourceGroupName -NamespaceName $NamespaceName -TopicName $TopicName -ErrorAction SilentlyContinue
        if ($allSubscriptions) {
            foreach ($targetSub in $targetSubscriptions) {
                $found = $allSubscriptions | Where-Object { $_.Name -eq $targetSub }
                if ($found) {
                    $existingSubscriptions += $targetSub
                    Write-Host "    ✅ Found messagesplitter subscription: $targetSub" -ForegroundColor Gray
                } else {
                    Write-Host "    ⏭️  messagesplitter subscription not found: $targetSub" -ForegroundColor DarkGray
                }
            }
        }
        else {
            Write-Host "    ⚠️  No subscriptions found in topic $TopicName" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Warning "Failed to get messagesplitter subscriptions: $($_.Exception.Message)"
        return @()
    }

    return $existingSubscriptions
}

function Remove-ServiceBusSubscriptions {
    param(
        [string]$NamespaceName,
        [hashtable]$TopicSubscriptions,  # Key = TopicName, Value = Array of subscription names
        [string]$ResourceGroupName,
        [switch]$WhatIf
    )
    if ([string]::IsNullOrEmpty($NamespaceName) -or $TopicSubscriptions.Count -eq 0) {
        return
    }

    foreach ($topicName in $TopicSubscriptions.Keys) {
        $subscriptions = $TopicSubscriptions[$topicName]
        if ($subscriptions.Count -eq 0) {
            continue
        }

        foreach ($subscriptionName in $subscriptions) {
            # Skip empty or null subscription names
            if ([string]::IsNullOrEmpty($subscriptionName)) {
                continue
            }

            if ($WhatIf) {
                Write-Host "    [WhatIf] Would remove Service Bus subscription: $subscriptionName from topic $topicName" -ForegroundColor Magenta
            }
            else {
                try {
                    # Check if subscription exists
                    $subscription = Get-AzServiceBusSubscription -ResourceGroupName $ResourceGroupName -NamespaceName $NamespaceName -TopicName $topicName -SubscriptionName $subscriptionName -ErrorAction SilentlyContinue
                    if ($subscription) {
                        Write-Host "    🗑️  Removing Service Bus subscription: $subscriptionName from topic $topicName..." -ForegroundColor Yellow
                        Invoke-WithRetry -Operation {
                            Remove-AzServiceBusSubscription -ResourceGroupName $ResourceGroupName -NamespaceName $NamespaceName -TopicName $topicName -SubscriptionName $subscriptionName -ErrorAction Stop
                        } -OperationName "Remove Service Bus subscription '$subscriptionName'"
                        Write-Host "    ✅ Successfully removed Service Bus subscription" -ForegroundColor Green
                    }
                    else {
                        Write-Host "    ⏭️  Service Bus subscription not found: $subscriptionName in topic $topicName" -ForegroundColor DarkGray
                    }
                }
                catch {
                    Write-Warning "Failed to remove Service Bus subscription '$subscriptionName': $($_.Exception.Message)"
                }
            }
        }
    }
}

function Remove-StorageAccount {
    param(
        [string]$StorageAccountName,
        [string]$ResourceGroupName,
        [switch]$WhatIf
    )
    if ([string]::IsNullOrEmpty($StorageAccountName)) {
        return
    }
    if ($WhatIf) {
        Write-Host "    [WhatIf] Would remove storage account: $StorageAccountName" -ForegroundColor Magenta
        return
    }
    try {
        # Check if storage account exists
        $storageAccount = Get-AzStorageAccount -ResourceGroupName $ResourceGroupName -Name $StorageAccountName -ErrorAction SilentlyContinue
        if ($storageAccount) {
            Write-Host "    🗑️  Removing storage account: $StorageAccountName..." -ForegroundColor Yellow
            Invoke-WithRetry -Operation {
                Remove-AzStorageAccount -ResourceGroupName $ResourceGroupName -Name $StorageAccountName -Force -ErrorAction Stop
            } -OperationName "Remove storage account '$StorageAccountName'"
            Write-Host "    ✅ Successfully removed storage account" -ForegroundColor Green
        }
        else {
            Write-Host "    ⏭️  Storage account not found: $StorageAccountName" -ForegroundColor DarkGray
        }
    }
    catch {
        Write-Warning "Failed to remove storage account '$StorageAccountName': $($_.Exception.Message)"
    }
}

function Test-ServicePlanHasOtherApps {
    param(
        [string]$ServicePlanName,
        [string]$ResourceGroupName,
        [string]$ExcludeFunctionName
    )
    try {
        $warningAction = Get-WarningAction
        # Get all web apps (including function apps) in the resource group
        $allApps = Get-AzWebApp -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue -WarningAction $warningAction
        # Filter apps that use this service plan, excluding the one being removed
        $appsUsingPlan = $allApps | Where-Object {
            $_.ServerFarmId.Split('/')[-1] -eq $ServicePlanName -and
            $_.Name -ne $ExcludeFunctionName
        }
        return ($null -ne $appsUsingPlan -and $appsUsingPlan.Count -gt 0)
    }
    catch {
        Write-Warning "Could not check service plan usage: $($_.Exception.Message)"
        return $true  # Assume it has other apps to be safe
    }
}

function Remove-FunctionAppResources {
    param(
        [string]$FunctionName,
        [string]$ServicePlanName,
        [string]$StorageAccountName,
        [string]$ComputeResourceGroupName,
        [string]$DataResourceGroupName,
        [bool]$ServicePlanShared = $false,
        [switch]$WhatIf
    )
    # Use the passed ServicePlanShared value, or check if not provided
    $servicePlanHasOtherApps = $ServicePlanShared
    if (-not $ServicePlanShared -and -not [string]::IsNullOrEmpty($ServicePlanName)) {
        $servicePlanHasOtherApps = Test-ServicePlanHasOtherApps -ServicePlanName $ServicePlanName `
            -ResourceGroupName $ComputeResourceGroupName `
            -ExcludeFunctionName $FunctionName
    }
    if ($WhatIf) {
        Write-Host "    [WhatIf] Would remove function app: $FunctionName" -ForegroundColor Magenta
        if (-not [string]::IsNullOrEmpty($ServicePlanName)) {
            if ($servicePlanHasOtherApps) {
                Write-Host "    [WhatIf] Would KEEP service plan (has other apps): $ServicePlanName" -ForegroundColor Yellow
            }
            else {
                Write-Host "    [WhatIf] Would remove service plan: $ServicePlanName" -ForegroundColor Magenta
            }
        }
        if (-not [string]::IsNullOrEmpty($StorageAccountName)) {
            Write-Host "    [WhatIf] Would remove storage account: $StorageAccountName" -ForegroundColor Magenta
        }
        # Service Bus topics shown in summary, not per function
        return
    }
    try {
        # Remove function app with retry
        Invoke-WithRetry -OperationName "Removing function app: $FunctionName" -Operation {
            Remove-FunctionAppCompat -ResourceGroupName $ComputeResourceGroupName -Name $FunctionName -ErrorAction Stop
        }

        if (-not [string]::IsNullOrEmpty($ServicePlanName)) {
            if ($servicePlanHasOtherApps) {
                Write-Host "    ⏭️  Keeping service plan (has other apps): $ServicePlanName" -ForegroundColor Yellow
            }
            else {
                # Remove service plan with retry
                Invoke-WithRetry -OperationName "Removing service plan: $ServicePlanName" -Operation {
                    Remove-AzAppServicePlan -ResourceGroupName $ComputeResourceGroupName -Name $ServicePlanName -Force -ErrorAction Stop
                }
            }
        }
        if (-not [string]::IsNullOrEmpty($StorageAccountName)) {
            Remove-StorageAccount -StorageAccountName $StorageAccountName -ResourceGroupName $DataResourceGroupName -WhatIf:$WhatIf
        }
        # Service Bus topics will be removed separately, not per function
        Write-Host "    ✅ Successfully removed resources" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to remove resources: $($_.Exception.Message)"
        throw
    }
}

function Get-TargetFunctionApps {
    param(
        [string]$SolutionAbbreviation,
        [string]$EnvironmentAbbreviation,
        [string]$ResourceGroupName
    )
    # Define the function app suffixes to remove
    $targetSuffixes = @(
        "GraphUpdater-medium",
        "GraphUpdater-onboarding",
        "MessageSplitter-m1",
        "MessageSplitter-o1"
    )
    $warningAction = Get-WarningAction
    $functionsToRemove = @()
    Write-Host "🔍 Scanning for target function apps in resource group: $ResourceGroupName" -ForegroundColor Cyan

    # Get all TeamsChannelUpdater topics to add to removal list
    $serviceBusNamespace = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $teamsChannelSubscriptions = Get-TeamsChannelUpdaterSubscriptions -NamespaceName $serviceBusNamespace -ResourceGroupName $dataResourceGroupName
    # Build complete list of Service Bus subscriptions that will be removed
    $allSubscriptions = @()
    $allSubscriptions += @("GraphUpdater_large_1", "GraphUpdater_large_2", "GraphUpdater_medium_1", "GraphUpdater_medium_2", "GraphUpdater_onboarding_1", "GraphUpdater_onboarding_2", "GraphUpdater_small_2", "GraphUpdater_small_3")  # From membershipupdaters topic
    $allSubscriptions += $teamsChannelSubscriptions  # TeamsChannelUpdater subscriptions from membershipupdaters topic
    $allSubscriptions += @("Medium", "Onboarding")  # From messagesplitter topic

    if ($allSubscriptions.Count -gt 0) {
        Write-Host "  🚌 Service Bus Subscriptions to remove:" -ForegroundColor Gray
        Write-Host "     Topic: membershipupdaters - GraphUpdater_*, TeamsChannelUpdater_* ($($teamsChannelSubscriptions.Count) found)" -ForegroundColor Gray
        Write-Host "     Topic: messagesplitter - Medium, Onboarding" -ForegroundColor Gray
    }
    # Get all function apps in the resource group
    $allFunctionApps = Get-FunctionAppCompat -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue -WarningAction $warningAction
    if ($null -eq $allFunctionApps -or $allFunctionApps.Count -eq 0) {
        Write-Host "  📊 No function apps found in resource group" -ForegroundColor Yellow
        return @()
    }
    # Get all service plans for lookup
    $allServicePlans = Get-AzAppServicePlan -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue -WarningAction $warningAction
    $servicePlanLookup = @{}
    if ($null -ne $allServicePlans) {
        foreach ($plan in $allServicePlans) {
            $servicePlanLookup[$plan.Name] = $plan
        }
    }
    foreach ($suffix in $targetSuffixes) {
        $expectedName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$suffix"
        $functionApp = $allFunctionApps | Where-Object { $_.Name -eq $expectedName }
        if ($functionApp) {
            $servicePlanName = $functionApp.ServerFarmId.Split('/')[-1]
            $servicePlan = $servicePlanLookup[$servicePlanName]
            # Get storage account name from function app settings
            $storageAccountName = ""
            try {
                # Get app settings using Get-FunctionAppSettingCompat for better reliability
                $appSettings = Get-FunctionAppSettingCompat -ResourceGroupName $ResourceGroupName -Name $functionApp.Name -ErrorAction SilentlyContinue

                if ($appSettings -and $appSettings.ContainsKey("AzureWebJobsStorage__accountName")) {
                    $storageAccountName = $appSettings["AzureWebJobsStorage__accountName"]
                }
                elseif ($appSettings -and $appSettings.ContainsKey("AzureWebJobsStorage")) {
                    # Try to parse from connection string if direct account name not found
                    $connectionString = $appSettings["AzureWebJobsStorage"]
                    if ($connectionString -match "AccountName=([^;]+)") {
                        $storageAccountName = $matches[1]
                    }
                }

            }
            catch {
                Write-Warning "Could not retrieve storage account for ${expectedName}: $($_.Exception.Message)"
            }
            # Check if service plan is shared with other apps
            $servicePlanShared = $false
            if ($servicePlanName) {
                $servicePlanShared = Test-ServicePlanHasOtherApps -ServicePlanName $servicePlanName `
                    -ResourceGroupName $ResourceGroupName `
                    -ExcludeFunctionName $functionApp.Name
                if ($servicePlanShared) {
                    Write-Host "    ⚠️  Service Plan is shared with other apps: $servicePlanName" -ForegroundColor Yellow
                }
            }

            # Map function types to their Service Bus subscriptions
            $serviceBusSubscriptions = @{}
            switch ($suffix) {
                "GraphUpdater-medium" {
                    # GraphUpdater functions use subscriptions from membershipupdaters topic
                    $membershipSubscriptions = @("GraphUpdater_large_1", "GraphUpdater_large_2", "GraphUpdater_medium_1", "GraphUpdater_medium_2", "GraphUpdater_onboarding_1", "GraphUpdater_onboarding_2", "GraphUpdater_small_2", "GraphUpdater_small_3")
                    $membershipSubscriptions += $teamsChannelSubscriptions
                    $serviceBusSubscriptions["membershipupdaters"] = $membershipSubscriptions
                }
                "GraphUpdater-onboarding" {
                    # GraphUpdater functions use subscriptions from membershipupdaters topic
                    $membershipSubscriptions = @("GraphUpdater_large_1", "GraphUpdater_large_2", "GraphUpdater_medium_1", "GraphUpdater_medium_2", "GraphUpdater_onboarding_1", "GraphUpdater_onboarding_2", "GraphUpdater_small_2", "GraphUpdater_small_3")
                    $membershipSubscriptions += $teamsChannelSubscriptions
                    $serviceBusSubscriptions["membershipupdaters"] = $membershipSubscriptions
                }
                "MessageSplitter-m1" {
                    # MessageSplitter functions use subscriptions from messagesplitter topic
                    $serviceBusSubscriptions["messagesplitter"] = @("Medium")
                }
                "MessageSplitter-o1" {
                    # MessageSplitter functions use subscriptions from messagesplitter topic
                    $serviceBusSubscriptions["messagesplitter"] = @("Onboarding")
                }
            }

            $functionInfo = @{
                FunctionName       = $functionApp.Name
                FunctionSuffix     = $suffix
                ServicePlanName    = $servicePlanName
                ServicePlanShared  = $servicePlanShared
                StorageAccountName = $storageAccountName
                ServiceBusSubscriptions = $serviceBusSubscriptions
                Exists             = $true
            }
            if ($servicePlan) {
                $functionInfo.CurrentSku = $servicePlan.Sku.Name
                $functionInfo.CurrentTier = $servicePlan.Sku.Tier
            }
            $functionsToRemove += $functionInfo
            Write-Host "  ✅ Found: $expectedName" -ForegroundColor Green
            # Show service plan status
            if (-not [string]::IsNullOrEmpty($servicePlanName)) {
                if ($servicePlanShared) {
                    Write-Host "    🏢 Service Plan: $servicePlanName (shared - will keep)" -ForegroundColor Yellow
                }
                else {
                    Write-Host "    ✅ Service Plan: $servicePlanName (will remove)" -ForegroundColor Green
                }
            }
            # Show storage account status
            if (-not [string]::IsNullOrEmpty($storageAccountName)) {
                Write-Host "    📦 Storage Account: $storageAccountName" -ForegroundColor Gray
            }
            else {
                Write-Host "    📦 Storage Account: (not found or not accessible)" -ForegroundColor DarkGray
            }
        }
        else {
            Write-Host "  ⏭️  Not found: $expectedName" -ForegroundColor DarkGray
        }
    }
    return $functionsToRemove
}

function Show-RemovalSummary {
    param(
        [array]$FunctionsToRemove,
        [string]$SolutionAbbreviation,
        [string]$EnvironmentAbbreviation,
        [switch]$Detailed
    )
    if ($FunctionsToRemove.Count -eq 0) {
        Write-Host ""
        Write-Host "📋 No function apps to remove" -ForegroundColor Cyan
        return
    }
    if ($Detailed) {
        Write-Host ""
        Write-Host "📋 The following actions WOULD be performed:" -ForegroundColor Cyan
        foreach ($func in $FunctionsToRemove) {
            Write-Host ""
            Write-Host "  🔧 Function: $($func.FunctionSuffix)" -ForegroundColor Yellow
            Write-Host "     ├─ Function App: $($func.FunctionName)" -ForegroundColor White
            Write-Host "     ├─ Service Plan: $($func.ServicePlanName)" -ForegroundColor White
            if ($func.StorageAccountName) {
                Write-Host "     ├─ Storage Account: $($func.StorageAccountName)" -ForegroundColor White
            }
            if ($func.ServiceBusTopics -and $func.ServiceBusTopics.Count -gt 0) {
                Write-Host "     ├─ Service Bus Topics: $($func.ServiceBusTopics -join ', ')" -ForegroundColor White
            }
            if ($func.CurrentSku) {
                Write-Host "     ├─ Current SKU: $($func.CurrentSku)/$($func.CurrentTier)" -ForegroundColor White
            }
            $storageAction = if ($func.StorageAccountName) { ", DELETE storage account" } else { "" }
            $serviceBusAction = if ($func.ServiceBusTopics -and $func.ServiceBusTopics.Count -gt 0) { ", DELETE Service Bus topics" } else { "" }
            $servicePlanAction = if ($func.ServicePlanShared) { "KEEP service plan (shared)" } else { "DELETE service plan" }
            Write-Host "     └─ Actions: DELETE function app, $servicePlanAction$storageAction$serviceBusAction" -ForegroundColor Red
        }
    }
    else {
        Write-Host ""
        Write-Host "📊 Removal Summary:" -ForegroundColor Cyan
        Write-Host "   Functions to remove: $($FunctionsToRemove.Count)" -ForegroundColor Yellow
        $FunctionsToRemove | ForEach-Object {
            $skuInfo = if ($_.CurrentSku) { " (SKU: $($_.CurrentSku)/$($_.CurrentTier))" } else { "" }
            Write-Host "   • $($_.FunctionSuffix)$skuInfo" -ForegroundColor White
        }
    }
}

function Remove-MultiLaneResources {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,

        [Parameter(Mandatory = $false)]
        [switch]$WhatIf,

        [Parameter(Mandatory = $false)]
        [switch]$SkipConfirmation
    )
    $startTime = Get-Date
    $computeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $serviceBusNamespace = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $serviceBusSubscription = "membershipupdaters"
    $warningAction = Get-WarningAction

    Write-Host "🚀 Starting Function App Removal Process" -ForegroundColor Cyan
    Write-Host "   🏷️  Solution: $SolutionAbbreviation" -ForegroundColor Gray
    Write-Host "   🏷️  Environment: $EnvironmentAbbreviation" -ForegroundColor Gray
    Write-Host "   🔄 Mode: $(if ($WhatIf) { 'Simulation (WhatIf)' } else { 'Execution' })" -ForegroundColor Gray
    Write-Host ""

    # Verify Azure context
    $context = Get-AzContext -WarningAction $warningAction
    if (-not $context) {
        Write-Host "❌ No Azure context found" -ForegroundColor Red
        Write-Host "   Please run Connect-AzAccount first to authenticate with Azure." -ForegroundColor Yellow
        return
    }

    Write-Host "🎯 Target Resource Groups:" -ForegroundColor Cyan
    Write-Host "   Compute: $computeResourceGroupName" -ForegroundColor Gray
    Write-Host "   Data: $dataResourceGroupName" -ForegroundColor Gray

    # Check if resource group exists
    $resourceGroup = Get-AzResourceGroup -Name $computeResourceGroupName -ErrorAction SilentlyContinue -WarningAction $warningAction
    if (-not $resourceGroup) {
        Write-Host "❌ Resource group '$computeResourceGroupName' not found" -ForegroundColor Red
        Write-Host "   Nothing to remove." -ForegroundColor Yellow
        return
    }
    # Get target function apps
    $functionsToRemove = Get-TargetFunctionApps -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -ResourceGroupName $computeResourceGroupName
    # Always collect Service Bus subscriptions to remove, even if function apps don't exist
    $serviceBusNamespace = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $teamsChannelSubscriptions = Get-TeamsChannelUpdaterSubscriptions -NamespaceName $serviceBusNamespace -ResourceGroupName $dataResourceGroupName
    $graphUpdaterSubscriptions = Get-FilteredGraphUpdaterSubscriptions -NamespaceName $serviceBusNamespace -ResourceGroupName $dataResourceGroupName
    $messageSplitterSubscriptions = Get-MessageSplitterSubscriptions -NamespaceName $serviceBusNamespace -ResourceGroupName $dataResourceGroupName

    # Build Service Bus subscriptions that should be removed
    $serviceBusSubscriptionsToRemove = @{
        "membershipupdaters" = $graphUpdaterSubscriptions + $teamsChannelSubscriptions
        "messagesplitter" = $messageSplitterSubscriptions
    }

    # Check if we have anything to remove (function apps or Service Bus subscriptions)
    $hasServiceBusWork = ($serviceBusSubscriptionsToRemove["membershipupdaters"].Count -gt 0 -or $serviceBusSubscriptionsToRemove["messagesplitter"].Count -gt 0)

    if ($functionsToRemove.Count -eq 0 -and -not $hasServiceBusWork) {
        Write-Host ""
        Write-Host "✅ No target function apps or Service Bus subscriptions found - nothing to remove" -ForegroundColor Green
        return
    }

    if ($functionsToRemove.Count -eq 0) {
        Write-Host ""
        Write-Host "⚠️  No target function apps found, but Service Bus subscriptions will be processed" -ForegroundColor Yellow
    }
    try {
        # Display removal summary
        Show-RemovalSummary -FunctionsToRemove $functionsToRemove `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation
        if ($WhatIf) {
            Write-Host ""
            Write-Host "🔍 SIMULATION MODE (WhatIf) - No changes will be made" -ForegroundColor Magenta
            Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
            # Show detailed actions
            Show-RemovalSummary -FunctionsToRemove $functionsToRemove `
                -SolutionAbbreviation $SolutionAbbreviation `
                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                -Detailed

            # Show Service Bus subscriptions that would be removed
            $allTopicSubscriptions = $serviceBusSubscriptionsToRemove

            if ($allTopicSubscriptions.Count -gt 0) {
                Write-Host ""
                Write-Host "🚌 Service Bus Subscriptions (removed once for all functions):" -ForegroundColor Cyan
                $topicsToDisplay = @($allTopicSubscriptions.Keys)
                foreach ($topicName in $topicsToDisplay) {
                    Write-Host "   Topic: $topicName" -ForegroundColor Yellow
                    $subscriptionsToDisplay = @($allTopicSubscriptions[$topicName])
                    foreach ($subscription in $subscriptionsToDisplay) {
                        Write-Host "      • $subscription" -ForegroundColor White
                    }
                }
            }

            Write-Host ""
            Write-Host "💡 To execute these changes, run the script without the -WhatIf parameter" -ForegroundColor Cyan
            Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
            return
        }
        else {
            if (-not $SkipConfirmation) {
                Write-Host ""
                if ($functionsToRemove.Count -gt 0) {
                    Write-Host "⚠️  WARNING: This will PERMANENTLY DELETE the following function apps:" -ForegroundColor Red
                    $functionsToRemove | ForEach-Object {
                        Write-Host "   • $($_.FunctionName)" -ForegroundColor Yellow
                    }
                }
                if ($hasServiceBusWork) {
                    if ($functionsToRemove.Count -gt 0) {
                        Write-Host ""
                        Write-Host "⚠️  And the following Service Bus subscriptions:" -ForegroundColor Red
                    } else {
                        Write-Host "⚠️  WARNING: This will PERMANENTLY DELETE the following Service Bus subscriptions:" -ForegroundColor Red
                    }
                    $topicsToShow = @($serviceBusSubscriptionsToRemove.Keys)
                    foreach ($topicName in $topicsToShow) {
                        $subscriptionsToShow = @($serviceBusSubscriptionsToRemove[$topicName])
                        if ($subscriptionsToShow.Count -gt 0) {
                            Write-Host "   Topic: $topicName" -ForegroundColor Yellow
                            foreach ($subscription in $subscriptionsToShow) {
                                Write-Host "      • $subscription" -ForegroundColor Yellow
                            }
                        }
                    }
                }
                Write-Host ""
                Write-Host "   This action cannot be undone!" -ForegroundColor Red
                $confirmation = Read-Host "Do you want to continue? (yes/no)"
                if ($confirmation -notmatch '^(y|yes)$') {
                    Write-Host "❌ Removal cancelled by user" -ForegroundColor Red
                    return
                }
            }
            else {
                Write-Host ""
                Write-Host "🚀 Confirmation skipped - proceeding with removal automatically" -ForegroundColor Yellow
            }
        }
        # Execute removals
        Write-Host ""
        Write-Host "🔄 Executing removals..." -ForegroundColor Cyan
        foreach ($func in $functionsToRemove) {
            Write-Host ""
            Write-Host "  🔧 Processing: $($func.FunctionSuffix)" -ForegroundColor Cyan
            # Remove Azure resources
            Remove-FunctionAppResources -FunctionName $func.FunctionName `
                -ServicePlanName $func.ServicePlanName `
                -StorageAccountName $func.StorageAccountName `
                -ComputeResourceGroupName $computeResourceGroupName `
                -DataResourceGroupName $dataResourceGroupName `
                -ServicePlanShared $func.ServicePlanShared `
                -WhatIf:$WhatIf
        }

        # Remove Service Bus subscriptions (even if no function apps were found)
        if (-not $WhatIf -and $hasServiceBusWork) {
            Write-Host ""
            Write-Host "🚌 Removing Service Bus subscriptions..." -ForegroundColor Cyan
            Remove-ServiceBusSubscriptions -NamespaceName $serviceBusNamespace -TopicSubscriptions $serviceBusSubscriptionsToRemove -ResourceGroupName $dataResourceGroupName
        }

        # Final summary
        $duration = (Get-Date) - $startTime
        Write-Host ""
        if ($functionsToRemove.Count -gt 0) {
            Write-Host "✅ Function app removal completed!" -ForegroundColor Green
        } else {
            Write-Host "✅ Service Bus subscription removal completed!" -ForegroundColor Green
        }
        Write-Host "   Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray
        Write-Host "   Functions processed: $($functionsToRemove.Count)" -ForegroundColor Gray

        if (-not $WhatIf) {
            Write-Host ""
            Write-Host "🎯 Removal Results:" -ForegroundColor Cyan
            if ($functionsToRemove.Count -gt 0) {
                Write-Host "   • Function apps and service plans have been deleted" -ForegroundColor White
                Write-Host "   • Storage accounts have been deleted" -ForegroundColor White
            }
            if ($hasServiceBusWork) {
                Write-Host "   • Service Bus subscriptions have been deleted" -ForegroundColor White
            }
            Write-Host "   • Resources can be recreated by running deployment scripts if needed" -ForegroundColor White
        }
    }
    catch {
        Write-Host ""
        Write-Host "❌ Removal failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   Stack trace: $($_.Exception.StackTrace)" -ForegroundColor DarkRed
        return
    }
}