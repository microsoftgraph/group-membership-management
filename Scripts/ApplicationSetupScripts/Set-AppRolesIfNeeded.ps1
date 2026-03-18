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
        [Guid] $TenantId
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
            DisplayName        = "Hyperlink Administrator"
            Description        = "Can add, update, or remove custom URLs."
            Value              = "Hyperlink.ReadWrite.All"
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

        # Disable roles that aren't in the new roles list
        foreach ($role in $currentAppRoles) {
            if (-not $newAppRolesLookup.ContainsKey($role.Value) -and $role.IsEnabled) {
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

        # Keep only roles that are still valid (in the new roles list)
        $currentAppRoles = $currentAppRoles | Where-Object {
            $newAppRolesLookup.ContainsKey($_.Value)
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