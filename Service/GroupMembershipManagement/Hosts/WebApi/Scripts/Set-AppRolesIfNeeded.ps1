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

    Connect-AzAccount -Tenant $TenantId

    $WebApiApp = Get-AzADApplication -ObjectId $WebApiObjectId

    [String[]]$memberTypes = "User", "Application"

    $jobCreatorRole = @{
        DisplayName        = "Job Creator"
        Description        = "Can create jobs and have access to the Membership Management page."
        Value              = "Job.Create"
        Id                 = [Guid]::NewGuid().ToString()
        IsEnabled          = $True
        AllowedMemberTypes = @($memberTypes)
    }

    $jobTenantReaderRole = @{
        DisplayName        = "Job Tenant Reader"
        Description        = "Can read all destinations in the tenant."
        Value              = "Job.Read.All"
        Id                 = [Guid]::NewGuid().ToString()
        IsEnabled          = $True
        AllowedMemberTypes = @($memberTypes)
    }

    $jobTenantWriterRole = @{
        DisplayName        = "Job Tenant Writer"
        Description        = "Can create, view, and update all destinations in the tenant."
        Value              = "Job.ReadWrite.All"
        Id                 = [Guid]::NewGuid().ToString()
        IsEnabled          = $True
        AllowedMemberTypes = @($memberTypes)
    }

    $submissionReviewerRole = @{
        DisplayName        = "Submission Reviewer"
        Description        = "Can view and manage Submission Requests for all groups."
        Value              = "Submission.ReadWrite.All"
        Id                 = [Guid]::NewGuid().ToString()
        IsEnabled          = $True
        AllowedMemberTypes = @($memberTypes)
    }

    $hyperlinkAdministratorRole = @{
        DisplayName        = "Hyperlink Administrator"
        Description        = "Can add, update, or remove custom URLs."
        Value              = "Settings.Hyperlink.ReadWrite.All"
        Id                 = [Guid]::NewGuid().ToString()
        IsEnabled          = $True
        AllowedMemberTypes = @($memberTypes)
    }

    $customMembershipProviderAdministratorRole = @{
        DisplayName        = "Custom Membership Provider Administrator"
        Description        = "Can add, update, or remove custom field names."
        Value              = "Settings.CustomSource.ReadWrite.All"
        Id                 = [Guid]::NewGuid().ToString()
        IsEnabled          = $True
        AllowedMemberTypes = @($memberTypes)
    }

    $newAppRoles = @($jobCreatorRole, 
                         $jobTenantReaderRole, 
                         $jobTenantWriterRole, 
                         $submissionReviewerRole, 
                         $hyperlinkAdministratorRole, 
                         $customMembershipProviderAdministratorRole
                        )

    $currentAppRoles = $WebApiApp.AppRole

    foreach ($role in $newAppRoles) {
        $exists = $currentAppRoles | Where-Object { $_.Value -eq $role.Value }
        if (-not $exists) {
            Write-Verbose "Adding role: $($role.DisplayName)"
            $currentAppRoles += $role
        }
        else {
            Write-Verbose "Role already exists: $($role.DisplayName)"
        }
    }
    try {
        Update-AzADApplication -ObjectId $WebApiObjectId -AppRole $currentAppRoles
        Write-Host "Application updated with new roles."
    }
    catch {
        Write-Error "Failed to update application roles: $_"
    }
}
