<#
.SYNOPSIS
Validates that the current Azure user has the required role-based access control (RBAC) permissions for deploying a specified solution in a given environment.

.DESCRIPTION
This function checks whether the currently signed-in Azure user has all required actions and data actions across multiple resource scopes, including the subscription and associated resource groups (data, compute, prereqs). It compares the user's role assignments against a predefined permission set and reports any missing permissions. If any required permissions are missing, the function exits with an error to prevent partial or failed deployments.

.PARAMETER SolutionAbbreviation
The short name used to identify the solution or application (e.g., 'gmm').

.PARAMETER EnvironmentAbbreviation
The abbreviation of the deployment environment (e.g., 'env', 'int').

.EXAMPLE
Assert-RbacPermissionsForDeployment -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "int"

This example checks if the current user has sufficient Azure RBAC permissions to deploy the GMM solution in the 'int' environment.

.NOTES
- This function must be run in a context where the user is authenticated using `Connect-AzAccount`.
- It provides a progress bar during the check and prints missing permissions for manual review or escalation.
#>

function Assert-RbacPermissionsForDeployment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )

    $SubscriptionId = (Get-AzContext).Subscription.Id
    $scopePrefix = "/subscriptions/$SubscriptionId"
    
    # Define the permissions required per scope
    $ScopedPermissions = @{
        "subscription"                                           = @{
            Actions     = @(
                "Microsoft.Resources/subscriptions/resourceGroups/write",
                "Microsoft.Resources/deployments/validate/action"
            )
            DataActions = @()
        }
        "$SolutionAbbreviation-data-$EnvironmentAbbreviation"    = @{
            Actions     = @(
                "Microsoft.Authorization/roleAssignments/write",
                "Microsoft.Resources/deployments/write",
                "Microsoft.KeyVault/vaults/write",
                "Microsoft.KeyVault/vaults/secrets/write",
                "Microsoft.KeyVault/vaults/deploy/action",
                "Microsoft.Sql/servers/azureADOnlyAuthentications/write",
                "Microsoft.Sql/servers/firewallRules/write",
                "Microsoft.Sql/servers/databases/write",
                "Microsoft.Sql/servers/auditingSettings/write",
                "Microsoft.Sql/servers/azureADOnlyAuthentications/write",
                "Microsoft.Sql/servers/write",
                "Microsoft.Sql/servers/databases/write",
                "Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies/write",
                "Microsoft.Insights/DiagnosticSettings/Write",
                "Microsoft.Insights/ActionGroups/Write",
                "Microsoft.Storage/storageAccounts/write",
                "Microsoft.Storage/storageAccounts/managementPolicies/write",
                "Microsoft.ManagedIdentity/userAssignedIdentities/write",
                "Microsoft.ServiceBus/namespaces/queues/write",
                "Microsoft.ServiceBus/namespaces/topics/write",
                "Microsoft.ServiceBus/namespaces/topics/subscriptions/write",
                "Microsoft.ServiceBus/namespaces/topics/subscriptions/rules/write",
                "Microsoft.Portal/dashboards/write",
                "Microsoft.AppConfiguration/configurationStores/write",
                "Microsoft.OperationalInsights/workspaces/write",
                "Microsoft.OperationalInsights/workspaces/listKeys/action",
                "Microsoft.Storage/storageAccounts/listKeys/action",
                "Microsoft.ServiceBus/namespaces/write",
                "Microsoft.Insights/components/write",
                "Microsoft.Insights/scheduledQueryRules/write",
                "Microsoft.OperationalInsights/workspaces/sharedKeys/action",
                "Microsoft.Insights/metricAlerts/write",
                "Microsoft.ManagedIdentity/userAssignedIdentities/assign/action",
                "Microsoft.Storage/storageAccounts/blobServices/containers/write",
                "Microsoft.DataFactory/factories/write",
                "Microsoft.DataFactory/factories/linkedservices/write",
                "Microsoft.DataFactory/factories/pipelines/write",
                "Microsoft.DataFactory/factories/datasets/write",
                "Microsoft.DataFactory/factories/dataflows/write"
            )
            DataActions = @(
                "Microsoft.KeyVault/vaults/secrets/setSecret/action",
                "Microsoft.KeyVault/vaults/secrets/getSecret/action",
                "Microsoft.AppConfiguration/configurationStores/keyValues/write"
            )
        }
        "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" = @{
            Actions     = @(
                "Microsoft.Authorization/roleAssignments/write",
                "Microsoft.Resources/deployments/write",
                "Microsoft.KeyVault/vaults/write",
                "Microsoft.KeyVault/vaults/secrets/write"
            )
            DataActions = @(
                "Microsoft.KeyVault/vaults/secrets/setSecret/action", 
                "Microsoft.KeyVault/vaults/secrets/getSecret/action"
            )
        }
        "$SolutionAbbreviation-compute-$EnvironmentAbbreviation" = @{
            Actions     = @(
                "Microsoft.Web/sites/config/write",
                "Microsoft.SignalRService/signalR/write",
                "Microsoft.Web/serverfarms/write",
                "Microsoft.Web/sites/write",
                "Microsoft.Web/sites/basicPublishingCredentialsPolicies/write",
                "Microsoft.Insights/diagnosticSettings/write",
                "Microsoft.Resources/deployments/write",
                "Microsoft.Web/sites/host/listkeys/action",
                "Microsoft.Web/staticSites/write",
                "Microsoft.Web/sites/config/list/action",
                "Microsoft.Authorization/roleAssignments/write",
                "Microsoft.Web/sites/publish/action",
                "Microsoft.Web/staticSites/listSecrets/action",
                "Microsoft.Web/sites/start/action"
            )
            DataActions = @()
        }
    }
    
    Write-Host "🔍 Checking user permissions..." -ForegroundColor Cyan
    
    # Get current user
    $currentUserUpn = (Get-AzContext).Account.Id
    $currentUser = Invoke-WithRetry `
        -Operation { Get-AzADUser -UserPrincipalName $currentUserUpn } `
        -OperationName "Get current Azure AD user" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $objectId = $currentUser.Id

    Write-Host "Validating permissions for user: $($currentUser.UserPrincipalName)" -ForegroundColor Yellow
    
    # Get all role assignments within the subscription
    Write-Host "Retrieving role assignments..." -NoNewline
    $allAssignments = Invoke-WithRetry `
        -Operation {
            Get-AzRoleAssignment -ObjectId $objectId | Where-Object {
                $_.Scope -like "$scopePrefix*"
            }
        } `
        -OperationName "Get role assignments" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    Write-Host "Completed retrieval of role assignments!" -ForegroundColor Green

    if (-not $allAssignments) {
        Write-Warning "No role assignments found for user $($currentUser.UserPrincipalName) under $scopePrefix."
        throw
    }
    
    Write-Host "Found $($allAssignments.Count) role assignments." -ForegroundColor Green

    foreach ($assignment in $allAssignments) {
        if ($assignment.RoleDefinitionName -eq "Owner" -and $assignment.Scope -eq $scopePrefix) {
            Write-Host "✅ User has 'Owner' role at scope: $($assignment.Scope). All permissions are granted." -ForegroundColor Green
            return
        }
    }
    
    # Count total permissions to check for progress calculation
    $totalPermissionsCount = 0
    foreach ($scopeKey in $ScopedPermissions.Keys) {
        $scope = $ScopedPermissions[$scopeKey]  
        $totalPermissionsCount += ($scope.Actions.Count + $scope.DataActions.Count)
    }
    
    Write-Host "Total permissions to check: $totalPermissionsCount" -ForegroundColor Yellow
    Start-Sleep -Seconds 1 # Pause briefly so user can see the initial count
    
    $missingPermissions = @()
    $processedPermissions = 0
    
    foreach ($scopeKey in $ScopedPermissions.Keys) {
        $scopePermissions = $ScopedPermissions[$scopeKey]
        $actionPermissions = $scopePermissions.Actions
        $dataActionPermissions = $scopePermissions.DataActions

        # Determine full scope path
        if ($scopeKey -eq "subscription") {
            $targetScope = "$scopePrefix"
        }
        else {
            $targetScope = "$scopePrefix/resourceGroups/$scopeKey"
        }
        
        Write-Host "`nChecking scope: $scopeKey" -ForegroundColor Cyan

        foreach ($requiredPermission in $actionPermissions) {
            $processedPermissions++
            $percentComplete = [math]::Floor(($processedPermissions / $totalPermissionsCount) * 100)
            # Display the progress bar using PowerShell's built-in Write-Progress
            Write-Progress -Activity "Checking permissions" -Status "$requiredPermission" -PercentComplete $percentComplete -Id 1 -CurrentOperation "Scope: $scopeKey"
            
            $permissionFound = $false

            foreach ($assignment in $allAssignments) {
                # Scope inheritance: if assignment scope is equal to or a parent of the target scope
                if ($targetScope -like "$($assignment.Scope)*") {
                    $roleDef = Invoke-WithRetry `
                        -Operation { Get-AzRoleDefinition -Id $assignment.RoleDefinitionId } `
                        -OperationName "Get role definition '$($assignment.RoleDefinitionName)'" `
                        -MaxAttempts 3 -BaseDelaySeconds 2

                    if (Test-Permission -required $requiredPermission -actions $roleDef.Actions -notActions $roleDef.NotActions) {
                        $permissionFound = $true
                        break
                    }
                }
            }

            if (-not $permissionFound) {
                $missingPermissions += [PSCustomObject]@{
                    Scope          = $scopeKey
                    Permission     = $requiredPermission
                    PermissionType = "Action"
                }
            }
            
            # Brief pause to make the progress bar visible
            Start-Sleep -Milliseconds 50
        }

        foreach ($requiredDataAction in $dataActionPermissions) {
            $processedPermissions++
            $percentComplete = [math]::Floor(($processedPermissions / $totalPermissionsCount) * 100)
            Write-Progress -Activity "Checking permissions" -Status "$requiredDataAction" -PercentComplete $percentComplete -Id 1 -CurrentOperation "Scope: $scopeKey"

            $permissionFound = $false

            foreach ($assignment in $allAssignments) {
                if ($targetScope -like "$($assignment.Scope)*") {
                    $roleDef = Invoke-WithRetry `
                        -Operation { Get-AzRoleDefinition -Id $assignment.RoleDefinitionId } `
                        -OperationName "Get role definition '$($assignment.RoleDefinitionName)'" `
                        -MaxAttempts 3 -BaseDelaySeconds 2

                    if (Test-Permission -required $requiredDataAction -actions $roleDef.DataActions -notActions $roleDef.NotDataActions) {
                        $permissionFound = $true
                        break
                    }
                }
            }

            if (-not $permissionFound) {
                $missingPermissions += [PSCustomObject]@{
                    Scope          = $scopeKey
                    Permission     = $requiredDataAction
                    PermissionType = "DataAction"
                }
            }

            Start-Sleep -Milliseconds 50
        }
    }   

    Write-Progress -Activity "Checking permissions" -Completed -Id 1
    
    # Report result
    if ($missingPermissions.Count -eq 0) {
        Write-Host "`n✅ User has all required permissions across all scopes." -ForegroundColor Green
    }
    else {
        Write-Warning "`n❌ User is missing the following permissions:"
        $missingPermissions | ForEach-Object {
            Write-Host "- Scope: $($_.Scope), Type: $($_.PermissionType), Permission: $($_.Permission)" -ForegroundColor Red
        }

        Write-Warning "`n⚠️ If this is the initial deployment, please ensure the user has all necessary permissions at the subscription scope."
        throw
    }
}

function Test-Permission {
    param (
        [string] $required,
        [string[]] $actions,
        [string[]] $notActions
    )

    foreach ($action in $actions) {
        if ($action -eq '*' -or $required -like $action) {
            foreach ($deny in $notActions) {
                if ($required -like $deny) {
                    return $false
                }
            }
            return $true
        }
    }
    return $false
}