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
    Write-Verbose "Set-AppRolesIfNeeded starting..."

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded

    $context = Get-AzContext
	$currentTenantId = $context.Tenant.Id

    if($currentTenantId -ne $TenantId) {
		Connect-AzAccount -Tenant $TenantId
	}

    $WebApiApp = Get-AzADApplication -ObjectId $WebApiObjectId
    if (-not $WebApiApp) {
        Write-Error "Failed to retrieve the Azure AD application with Object Id: $WebApiObjectId"
        return
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
            DisplayName        = "Job Writer"
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
            DisplayName        = "Hyperlink Administrator"
            Description        = "Can add, update, or remove custom URLs."
            Value              = "Settings.Hyperlink.ReadWrite.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        },
        @{
            DisplayName        = "Custom Membership Provider Administrator"
            Description        = "Can add, update, or remove custom field names."
            Value              = "Settings.CustomSource.ReadWrite.All"
            Id                 = [Guid]::NewGuid().ToString()
            IsEnabled          = $True
            AllowedMemberTypes = @($memberTypes)
        }

    )

    $newAppRolesLookup = @{}
    foreach ($role in $newAppRoles) {
        $newAppRolesLookup[$role.Value] = $role
    }

    if ($WebApiApp.AppRole -eq $null) {
        Write-Verbose "No existing app roles found. Initializing an empty array."
        $currentAppRoles = @()
    } else {
        $currentAppRoles = $WebApiApp.AppRole.Clone()

        foreach ($role in $currentAppRoles) {
            if (-not $newAppRolesLookup.ContainsKey($role.Value) -and $role.IsEnabled) {
                Write-Verbose "Disabling role: $($role.DisplayName)"
                $role.IsEnabled = $false
            }
        }

        try {
            Update-AzADApplication -ObjectId $WebApiObjectId -AppRole $currentAppRoles
            Write-Host "Roles have been disabled as needed."
        }
        catch {
            Write-Error "Failed to disable roles: $_"
            return
        }

        $currentAppRoles = $currentAppRoles | Where-Object {
            $newAppRolesLookup.ContainsKey($_.Value)
        }
    }

    if(-not $currentAppRoles) {
        $currentAppRoles = @()
    }

    foreach ($role in $newAppRoles) {
        $exists = $currentAppRoles | Where-Object { $_.Value -eq $role.Value }
        if (-not $exists) {
            Write-Verbose "Adding role: $($role.DisplayName)"
            $currentAppRoles += $role
        }
    }

    try {
        Update-AzADApplication -ObjectId $WebApiObjectId -AppRole $currentAppRoles
        Write-Host "Application updated with new roles and removed obsolete roles."
    }
    catch {
        Write-Error "Failed to update application roles: $_"
    }
}
