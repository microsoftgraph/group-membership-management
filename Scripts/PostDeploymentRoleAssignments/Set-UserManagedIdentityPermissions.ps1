<#
.SYNOPSIS
Grants GraphAPI permissions to the User Assigned Managed Identity.

.DESCRIPTION
Grants GraphAPI permissions to the User Assigned Managed Identity.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.EXAMPLE
Set-UserManagedIdentityPermissions -SolutionAbbreviation "gmm" `
								   -EnvironmentAbbreviation "<env>"
#>

function Set-UserManagedIdentityPermissions {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation
	)

	Write-Host "Setting permissions for User Assigned Managed Identity"

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

	# Connect to Microsoft Graph
	if ($global:SkipMSGraphLogin -ne $true) {
		$currentTenantId = (Get-AzContext).Tenant.Id
		Connect-MgGraph -Scopes "Directory.ReadWrite.All" -TenantId $currentTenantId
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
}