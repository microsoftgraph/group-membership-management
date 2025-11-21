<#
.SYNOPSIS
Grants GraphAPI permissions to the User Assigned Managed Identity.

.DESCRIPTION
Grants GraphAPI permissions to the User Assigned Managed Identity.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER SkipPrivilegedDirectoryActions
When set to true, prints manual instructions instead of performing the actions.

.EXAMPLE
Set-UserManagedIdentityPermissions -SolutionAbbreviation "gmm" `
								   -EnvironmentAbbreviation "<env>"
#>

function Write-UserManagedIdentityPermissionsInstructions {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $TenantId
	)

	$uamiName = "$SolutionAbbreviation-identity-$EnvironmentAbbreviation-Graph"
	$appRoles = @("GroupMember.Read.All", "Member.Read.Hidden", "User.Read.All")

	Write-Host ""
	Write-Host "=============================================================================" -ForegroundColor Cyan
	Write-Host "MANUAL ACTIONS REQUIRED: User Managed Identity Permissions" -ForegroundColor Yellow
	Write-Host "=============================================================================" -ForegroundColor Cyan
	Write-Host ""
	Write-Host "The following permissions need to be granted to the User Assigned Managed Identity:" -ForegroundColor White
	Write-Host ""
	Write-Host "  Managed Identity Name: " -NoNewline -ForegroundColor White
	Write-Host $uamiName -ForegroundColor Green
	Write-Host ""
	Write-Host "  Required App Roles (Microsoft Graph API):" -ForegroundColor White
	foreach ($appRole in $appRoles) {
		Write-Host "    - $appRole" -ForegroundColor Green
	}
	Write-Host ""
	Write-Host "=============================================================================" -ForegroundColor Cyan
	Write-Host "OPTION 1: Automated Setup (Recommended)" -ForegroundColor Yellow
	Write-Host "=============================================================================" -ForegroundColor Cyan
	Write-Host ""
	Write-Host "If you have the required directory permissions, you can run:" -ForegroundColor White
	Write-Host ""
	Write-Host "  . `"$PSScriptRoot/Set-UserManagedIdentityPermissions.ps1`"" -ForegroundColor Gray
	Write-Host "  Set-UserManagedIdentityPermissions ``" -ForegroundColor Gray
	Write-Host "      -SolutionAbbreviation `"$SolutionAbbreviation`" ``" -ForegroundColor Gray
	Write-Host "      -EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``" -ForegroundColor Gray
	Write-Host "      -TenantId `"$TenantId`"" -ForegroundColor Gray
	Write-Host ""
	Write-Host "This will automatically grant the required permissions." -ForegroundColor White
	Write-Host ""
	Write-Host "=============================================================================" -ForegroundColor Cyan
	Write-Host "OPTION 2: Manual Setup" -ForegroundColor Yellow
	Write-Host "=============================================================================" -ForegroundColor Cyan
	Write-Host ""
	Write-Host "Steps to grant these permissions manually:" -ForegroundColor White
	Write-Host ""
	Write-Host "  1. Sign in to the Azure Portal with an account that has the" -ForegroundColor White
	Write-Host "     'Privileged Role Administrator' or 'Global Administrator' role" -ForegroundColor White
	Write-Host ""
	Write-Host "  2. Navigate to: Azure Active Directory > Enterprise applications" -ForegroundColor White
	Write-Host ""
	Write-Host "  3. Change the filter to 'Managed Identities' and search for:" -ForegroundColor White
	Write-Host "     $uamiName" -ForegroundColor Green
	Write-Host ""
	Write-Host "  4. Select the managed identity and go to 'Permissions'" -ForegroundColor White
	Write-Host ""
	Write-Host "  5. Click 'Add permission' and select 'Microsoft Graph'" -ForegroundColor White
	Write-Host ""
	Write-Host "  6. Choose 'Application permissions' and add each of the following:" -ForegroundColor White
	foreach ($appRole in $appRoles) {
		Write-Host "     - $appRole" -ForegroundColor Green
	}
	Write-Host ""
	Write-Host "  7. Click 'Add permissions'" -ForegroundColor White
	Write-Host ""
	Write-Host "  8. Click 'Grant admin consent' and confirm" -ForegroundColor White
	Write-Host ""
	Write-Host "=============================================================================" -ForegroundColor Cyan
	Write-Host ""
	Write-Host "Press ENTER after you have completed the above actions to continue..." -ForegroundColor Yellow
	Read-Host
	Write-Host "Continuing with deployment..." -ForegroundColor Green
	Write-Host ""
}

function Set-UserManagedIdentityPermissions {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $False)]
		[string] $TenantId,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipPrivilegedDirectoryActions = $false
	)

	Write-Host "Setting permissions for User Assigned Managed Identity"

	# If skipping privileged directory actions, print manual instructions instead
	if ($SkipPrivilegedDirectoryActions) {
		Write-UserManagedIdentityPermissionsInstructions `
					-SolutionAbbreviation $SolutionAbbreviation `
					-EnvironmentAbbreviation $EnvironmentAbbreviation `
					-TenantId $TenantId
		return
	}

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent

	if ($global:SkipModuleInstall -ne $true) {
		Write-Host "Installing required modules for User Managed Identity permissions setup..."
		
		$requiredGraphModules = @(
			"Microsoft.Graph.Authentication",
			"Microsoft.Graph.Applications"
		)

		. ($scriptsDirectory + '/Install-ModuleIfNeeded.ps1')

		foreach ($module in $requiredGraphModules) {
			Install-ModuleIfNeeded -Name $module -Version "2.17.0" -Verbose
		}

		Write-Host "Required modules installed."
	}

	if ($global:SkipMsGraphLogin -ne $true) {

		# Disconnect any existing session
		Disconnect-MgGraph -ErrorAction SilentlyContinue 
		
		# Connect to Microsoft Graph with required scopes for the target tenant
		Connect-MgGraph -TenantId $TenantId -Scopes "Directory.ReadWrite.All"
		
		# Verify connection to correct tenant
		$newContext = Get-MgContext
		if ($null -eq $newContext -or $newContext.TenantId -ne $TenantId) {
			throw "Failed to connect to the correct tenant. Expected: $TenantId, Actual: $($newContext.TenantId)"
		}
		
		Write-Host "Successfully connected to Microsoft Graph for tenant $TenantId"
	}

	# Get the User Assigned Managed Identity and Graph Service Principal
	$uamiName = "$SolutionAbbreviation-identity-$EnvironmentAbbreviation-Graph"
	$uamiSPN = Get-MgServicePrincipal -Filter "displayName eq '$uamiName'"
	$graphApiSPN = Get-MgServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'"

	$currentAppRoleAssignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $uamiSPN.Id
	$appRoles = @("GroupMember.Read.All", "Member.Read.Hidden", "User.Read.All")
	foreach ($appRoleName in $appRoles) {

		$appRole = $graphApiSPN.AppRoles | Where-Object { $_.Value -eq $appRoleName -and $_.AllowedMemberTypes -contains "Application" }
		$isRoleAssigned = $currentAppRoleAssignments | Where-Object { $_.AppRoleId -eq $appRole.Id }

		if ($isRoleAssigned) {
			Write-Host "Role $appRoleName is already assigned to $uamiName. Skipping..."
			continue
		}

		$bodyParam = @{
			PrincipalId = $uamiSPN.Id
			ResourceId  = $graphApiSPN.Id
			AppRoleId   = $appRole.Id
		}

		New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $uamiSPN.Id -BodyParameter $bodyParam
 	}

	# Disconnect from Microsoft Graph before returning
	if ($global:SkipMsGraphLogin -ne $true) {
		Disconnect-MgGraph -ErrorAction SilentlyContinue
	}
}