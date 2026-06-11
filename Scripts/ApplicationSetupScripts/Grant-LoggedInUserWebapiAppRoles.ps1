$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Grants all active Web API app roles to the currently signed-in Azure user.

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.DESCRIPTION
This script assigns all active app roles defined in the Web API Azure AD application 
to the currently signed-in Azure user. The script validates that all required roles 
exist and tracks the assignment status for each role.

.EXAMPLE
Grant-LoggedInUserWebapiAppRoles -SolutionAbbreviation "gmm" `
                                  -EnvironmentAbbreviation "dev"
#>

function Grant-LoggedInUserWebapiAppRoles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string] $EnvironmentAbbreviation
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

    Write-Host "`nGrant-LoggedInUserWebapiAppRoles starting...`n"

    # Get current user and tenant from Microsoft Graph
    $mgContext = Get-MgContext
    if ($null -eq $mgContext) {
        throw "No Microsoft Graph context found. Please run Connect-MgGraph first."
    }

    $currentUser = Get-MgUser -UserId $mgContext.Account
    if ($null -eq $currentUser) {
        throw "Unable to retrieve the currently signed-in user."
    }

    $tenantId = $mgContext.TenantId
    Write-Host "Current user: $($currentUser.UserPrincipalName)"
    Write-Host "Tenant ID: $tenantId"

    # Find Web API Azure AD application
    $webApiAppDisplayName = "$SolutionAbbreviation-webapi-$EnvironmentAbbreviation"
    Write-Host "Looking for application: $webApiAppDisplayName"

    $webApiApp = Get-MgApplication -Filter "displayName eq '$webApiAppDisplayName'"
    if ($null -eq $webApiApp) {
        throw "Web API application '$webApiAppDisplayName' not found."
    }

    Write-Host "Found application: $webApiAppDisplayName (AppId: $($webApiApp.AppId))" -ForegroundColor Green

    # Get service principal
    $servicePrincipal = Get-MgServicePrincipal -Filter "appId eq '$($webApiApp.AppId)'"
    if ($null -eq $servicePrincipal) {
        throw "Service principal for application '$webApiAppDisplayName' not found."
    }

    Write-Host "Found service principal: $($servicePrincipal.Id)" -ForegroundColor Green

    # Get app roles from the application
    $appRoles = $webApiApp.AppRoles
    if ($null -eq $appRoles -or $appRoles.Count -eq 0) {
        throw "No app roles found for application '$webApiAppDisplayName'."
    }

    # Get all enabled app roles
    $enabledAppRoles = @{}
    foreach ($role in $appRoles) {
        if ($role.IsEnabled) {
            $enabledAppRoles[$role.Value] = $role
        }
    }

    if ($enabledAppRoles.Count -eq 0) {
        throw "No enabled app roles found for application '$webApiAppDisplayName'."
    }

    Write-Host "Found $($enabledAppRoles.Count) enabled app roles."

    # Get existing role assignments for the user filtered by this service principal
    $existingAssignments = Get-MgUserAppRoleAssignment -UserId $currentUser.Id -Filter "resourceId eq $($servicePrincipal.Id)"
    $existingRoleIds = @{}
    foreach ($assignment in $existingAssignments) {
        if ($assignment.ResourceId -eq $servicePrincipal.Id) {
            $existingRoleIds[$assignment.AppRoleId] = $true
        }
    }
    
    # Track status for each role
    $roleStatuses = @()

    # Assign each enabled role
    foreach ($roleValue in $enabledAppRoles.Keys) {
        $role = $enabledAppRoles[$roleValue]
        $roleId = $role.Id

        Write-Host "Processing role: $roleValue (Id: $roleId)" -ForegroundColor Cyan

        if ($existingRoleIds.ContainsKey($roleId)) {
            Write-Host "Role '$roleValue' is already assigned to user." -ForegroundColor Yellow
            $roleStatuses += [PSCustomObject]@{
                RoleValue = $roleValue
                Status    = "AlreadyPresent"
            }
        }
        else {
            Write-Host "Assigning role '$roleValue' to user..."

            $bodyParam = @{
                PrincipalId = $currentUser.Id
                ResourceId  = $servicePrincipal.Id
                AppRoleId   = $roleId
            }

            Invoke-WithCreateRetry `
                -GetExistingOperation {
                    Get-MgUserAppRoleAssignment -UserId $currentUser.Id -Filter "resourceId eq $($servicePrincipal.Id)" | Where-Object { $_.AppRoleId -eq $roleId }
                } `
                -CreateOperation {
                    New-MgUserAppRoleAssignment -UserId $currentUser.Id -BodyParameter $bodyParam
                } `
                -OperationName "Assign app role '$roleValue' to user" `
                -ExistsMessage "App role '$roleValue' is already assigned to the current user. Skipping." | Out-Null

            Write-Host "Successfully assigned role '$roleValue'." -ForegroundColor Green
            $roleStatuses += [PSCustomObject]@{
                RoleValue = $roleValue
                Status    = "Assigned"
            }
        }
    }

    Write-Host "`nGrant-LoggedInUserWebapiAppRoles completed successfully.`n"
}