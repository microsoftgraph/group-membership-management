$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Creates the app registrations app roles if it is needed.

.PARAMETER WebApiObjectId
Web Api App object Id.

.PARAMETER TenantId
Azure tenant id where the app registration is located.

.DESCRIPTION
Creates the app registration's app roles if it is needed.

.EXAMPLE
Set-AppRolesIfNeeded	-WebApiObjectId "<web-api-app-registration-id>"  `
                        -TenantId "<tenant-id>" `
                        -Verbose


#>
function Set-AppRolesIfNeeded {
    param(
        [Parameter(Mandatory = $true)]
        [string] $WebApiObjectId,
        [Parameter(Mandatory = $True)]
        [Guid] $TenantId,
        # Retired app role values that must not be removed while principals are still assigned to
        # them. Assigned principals must first be granted their replacement role.
        [Parameter(Mandatory = $False)]
        [hashtable] $RetiredRoleReplacements = @{ "Hyperlink.ReadWrite.All" = "GeneralSettings.ReadWrite.All" },
        # Removes retired app roles even when principals are still assigned to them.
        [Parameter(Mandatory = $False)]
        [switch] $ForceRetiredRoleRemoval
    )
    Write-Host "`nSet-AppRolesIfNeeded starting...`n"

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

    if ($global:SkipModuleInstall -ne $true) {
        . ($scriptsDirectory + '\Install-MSGraphIfNeeded.ps1')
        Install-MSGraphIfNeeded
    }

    if ($global:SkipMsGraphLogin -ne $true) {
        # Disconnect any existing session
        Disconnect-MgGraph -ErrorAction SilentlyContinue 

        $requiredScopes = @(
            "Application.ReadWrite.All", 
            "AppRoleAssignment.ReadWrite.All"
        )
        
        # Connect to Microsoft Graph with required scopes for the target tenant
        Invoke-WithRetry -Operation {
            Connect-MgGraph -TenantId $TenantId -Scopes $requiredScopes
        } -OperationName "Connect to Microsoft Graph for app roles setup"
        
        Write-Host "Successfully connected to Microsoft Graph for tenant $TenantId"
    }

    $WebApiApp = Invoke-WithRetry -Operation {
        Get-MgApplication -ApplicationId $WebApiObjectId
    } -OperationName "Get web api application for app roles"
    if (-not $WebApiApp) {
        Write-Error "Failed to retrieve the Azure AD application with Object Id: $WebApiObjectId"
        throw "Azure AD application not found."
    }

    $memberTypes = "User", "Application"

    $newAppRoles = @(
        @{
            DisplayName        = "Job Reader"
            Description        = "Can read owned destinations in the tenant."
            Value              = "Job.Read.OwnedBy"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Job Owner Enabler/Disabler"
            Description        = "Can enable or disable owned destinations in the tenant."
            Value              = "Job.Enable.OwnedBy"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Job Owner Deleter"
            Description        = "Can delete the job from GMM (disable GMM sync)."
            Value              = "Job.Delete.OwnedBy"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Job Owner Configuration Editor"
            Description        = "Can update owned destinations' configuration."
            Value              = "Job.EditConfiguration.OwnedBy"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Job Owner Writer"
            Description        = "Can create, view, and update owned destinations in the tenant."
            Value              = "Job.ReadWrite.OwnedBy"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Job Tenant Reader"
            Description        = "Can read all destinations in the tenant."
            Value              = "Job.Read.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Job Tenant Writer"
            Description        = "Can create, view, and update all destinations in the tenant."
            Value              = "Job.ReadWrite.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Submission Reviewer"
            Description        = "Can view and manage Submission Requests for all groups."
            Value              = "Submission.ReadWrite.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Submission Rejector"
            Description        = "Can view and reject Submission Requests for all groups."
            Value              = "Submission.Reject.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Custom Membership Provider Administrator"
            Description        = "Can add, update, or remove custom field names."
            Value              = "CustomSource.ReadWrite.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "General Settings Administrator"
            Description        = "Can update general settings."
            Value              = "GeneralSettings.ReadWrite.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Reset Administrator"
            Description        = "Can reset or stop GMM."
            Value              = "Operations.Reset"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Auto Approver Administrator"
            Description        = "Can update automatic approval settings."
            Value              = "AutoApprover.ReadWrite.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "AI Onboarding Chat"
            Description        = "Can access the AI-powered onboarding chat assistant."
            Value              = "AI.Onboarding.Chat"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "AI Settings Administrator"
            Description        = "Can manage AI settings including prompts, temperature, and feature toggles."
            Value              = "AISettings.ReadWrite.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "AI Sync Job Viewer"
            Description        = "Can view AI-generated sync explanations for all destinations."
            Value              = "AI.SyncJob.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Teams Channel Onboarder"
            Description        = "Allows onboarding/creating TeamsChannel sync destinations for owned Teams (paired with Job.ReadWrite.OwnedBy)."
            Value              = "TeamsChannel.Onboard.OwnedBy"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        }

    )

    $newAppRolesLookup = @{}
    foreach ($role in $newAppRoles) {
        $newAppRolesLookup[$role.Value] = $role
    }

    if ($WebApiApp.AppRoles -eq $null) {
        Write-Host "No existing app roles found."
        $currentAppRoles = @()
    } else {
        $currentAppRoles = $WebApiApp.AppRoles | ForEach-Object {
            @{
                DisplayName        = $_.DisplayName
                Description        = $_.Description
                Value              = $_.Value
                Id                 = $_.Id
                IsEnabled          = $_.IsEnabled
                AllowedMemberTypes = $_.AllowedMemberTypes
            }
        }

        # Retired roles that still have assignments are kept enabled so that administrators are not
        # silently locked out. They must be migrated to their replacement role first.
        $retainedRetiredRoleValues = @()
        if (-not $ForceRetiredRoleRemoval) {
            $servicePrincipal = Invoke-WithRetry -Operation {
                Get-MgServicePrincipal -Filter "appId eq '$($WebApiApp.AppId)'" -ErrorAction SilentlyContinue
            } -OperationName "Get web api service principal for retired role inventory"

            if ($servicePrincipal) {
                $assignments = Invoke-WithRetry -Operation {
                    Get-MgServicePrincipalAppRoleAssignedTo -ServicePrincipalId $servicePrincipal.Id -All
                } -OperationName "Get app role assignments for retired role inventory"

                foreach ($retiredRoleValue in $RetiredRoleReplacements.Keys) {
                    $retiredRole = $currentAppRoles | Where-Object { $_.Value -eq $retiredRoleValue }
                    if (-not $retiredRole) {
                        continue
                    }

                    $assignedPrincipals = $assignments | Where-Object { $_.AppRoleId -eq $retiredRole.Id }
                    if ($assignedPrincipals) {
                        $retainedRetiredRoleValues += $retiredRoleValue
                        Write-Warning "App role '$retiredRoleValue' is retired but still assigned to $(@($assignedPrincipals).Count) principal(s). Grant '$($RetiredRoleReplacements[$retiredRoleValue])' to the following principals, remove the retired assignments, then re-run this script (or re-run with -ForceRetiredRoleRemoval):"
                        foreach ($assignment in $assignedPrincipals) {
                            Write-Warning "  - $($assignment.PrincipalDisplayName) ($($assignment.PrincipalId))"
                        }
                    }
                }
            }
        }

        # Disable roles that aren't in the new roles list
        foreach ($role in $currentAppRoles) {
            if (-not $newAppRolesLookup.ContainsKey($role.Value) -and $role.IsEnabled -and $retainedRetiredRoleValues -notcontains $role.Value) {
                Write-Host "Disabling role: $($role.DisplayName)"
                $role.IsEnabled = $false
            }
        }

        try {
            Invoke-WithRetry -Operation {
                Update-MgApplication -ApplicationId $WebApiObjectId -AppRoles $currentAppRoles
            } -OperationName "Disable deprecated app roles"
            Write-Host "Roles have been disabled as needed."
        }
        catch {
            Write-Error "Failed to disable roles: $_"
            throw
        }

        # Keep only roles that are still valid (in the new roles list), plus retired roles that are
        # still assigned and therefore pending migration.
        $currentAppRoles = $currentAppRoles | Where-Object {
            $newAppRolesLookup.ContainsKey($_.Value) -or $retainedRetiredRoleValues -contains $_.Value
        }
    }

    if(-not $currentAppRoles) {
        $currentAppRoles = @()
    }

    # Add any missing roles
    foreach ($role in $newAppRoles) {
        $exists = $currentAppRoles | Where-Object { $_.Value -eq $role.Value }
        if (-not $exists) {
            Write-Host "Adding role: $($role.DisplayName)"
            $currentAppRoles += $role
        }
    }

    # Single update with all changes
    try {
        Invoke-WithRetry -Operation {
            Update-MgApplication -ApplicationId $WebApiObjectId -AppRoles $currentAppRoles
        } -OperationName "Apply app role updates"
        Write-Host "Application updated with new roles and removed obsolete roles."
    }
    catch {
        Write-Error "Failed to update application roles: $_"
        throw
    }

    Write-Host "`nSet-AppRolesIfNeeded completed successfully.`n"
}