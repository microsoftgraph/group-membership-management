<#
.SYNOPSIS
Validates that the current user has the necessary Microsoft Graph API scopes and directory roles to perform privileged administrative tasks.

.DESCRIPTION
This function ensures that the signed-in user is connected to Microsoft Graph with the required delegated permission scopes (e.g., `AppRoleAssignment.ReadWrite.All`, `Directory.ReadWrite.All`) and is assigned one of the required Azure AD roles (e.g., Global Administrator or Privileged Role Administrator). It reports any missing scopes or roles and provides guidance for resolving the deficiencies.

.PARAMETER RequiredScopes
A list of Microsoft Graph delegated scopes the user must have consented to. Defaults to AppRoleAssignment.ReadWrite.All and Directory.ReadWrite.All.

.PARAMETER RequiredRoles
A list of Azure AD directory roles the user must be a member of. Defaults to Global Administrator and Privileged Role Administrator.

.EXAMPLE
Assert-MicrosoftGraphPermissions

Checks if the user is connected to Microsoft Graph with the required scopes and assigned the necessary directory roles.

.NOTES
- This function uses the Microsoft.Graph PowerShell SDK.
- Required Graph scopes must be consented to interactively or by a Global Administrator.
- Useful for validating access before running scripts that assign app roles or modify directory objects.
- Requires the Microsoft.Graph.Authentication, Microsoft.Graph.Identity.DirectoryManagement, and Microsoft.Graph.Users modules to be installed.
#>

function Assert-MicrosoftGraphPermissions {
    param (
        [Parameter(Mandatory = $false)]
        [string[]] $RequiredScopes = @(
            "AppRoleAssignment.ReadWrite.All",
            "Directory.ReadWrite.All"
        ),
        [Parameter(Mandatory = $false)]
        [string[]] $RequiredRoles = @(
            "Global Administrator",
            "Privileged Role Administrator"
        )
    )

    Write-Host "Checking Microsoft Graph connection..." -ForegroundColor Cyan
    $context = Get-MgContext
    if (-not $context -or -not $context.Scopes) {
        throw "❌ Not connected to Microsoft Graph. Please run `Connect-MgGraph -Scopes $($RequiredScopes -join ', ')` to sign in."
    }
    Write-Host "✅ Connected to Microsoft Graph as $($context.Account)" -ForegroundColor Green

    $tokenScopes = $context.Scopes
    $missingScopes = $RequiredScopes | Where-Object { $_ -notin $tokenScopes }

    Write-Host "`nChecking required Graph permission scopes..." -ForegroundColor Cyan
    if ($missingScopes.Count -eq 0) {
        Write-Host "✅ All required Graph permission scopes are present." -ForegroundColor Green
    } else {
        Write-Warning "❌ Missing the following Graph permission scopes: $($missingScopes -join ', ')"
        Write-Host "`n🔐 These permissions require admin consent. Please contact a Global Administrator to grant them using this powershell command:" -ForegroundColor Yellow
        Write-Host "`n    Connect-MgGraph -Scopes "AppRoleAssignment.ReadWrite.All Directory.ReadWrite.All"" -ForegroundColor Cyan
        Write-Host "`nAfter consent is granted, re-run this script" -ForegroundColor Yellow
        throw
    }

    Write-Host "`nChecking required directory roles..." -ForegroundColor Cyan
    $user = Get-MgUser -UserId $context.Account
    $userRoles = Get-MgUserMemberOf -UserId $user.Id | Where-Object {
        $_.AdditionalProperties.'@odata.type' -eq "#microsoft.graph.directoryRole"
    }

    $userRoleNames = @()
    foreach ($role in $userRoles) {
        $roleDetail = Get-MgDirectoryRole -DirectoryRoleId $role.Id
        $userRoleNames += $roleDetail.DisplayName
    }

    $matchedRoles = $RequiredRoles | Where-Object { $userRoleNames -contains $_ }

    if ($matchedRoles.Count -gt 0) {
        Write-Host "✅ User is assigned one of the required directory roles: $($matchedRoles -join ', ')" -ForegroundColor Green
        return $true
    } else {
        Write-Warning "`n❌ User does not have any of the required directory roles: $($RequiredRoles -join ', ')"
        Write-Host "`nAsk a Global Administrator to assign one of these roles using:" -ForegroundColor Yellow
        Write-Host "    Azure Portal → Azure AD → Roles and administrators → <Role> → Add assignments" -ForegroundColor Cyan
        throw
    }
}
