$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal for Outlook Actionable Messages.
You may have to open the created Azure AD app in your demo tenant and consent to the permissions!

To find the tenant ID for a tenant, you can run Connect-AzAccount in Powershell, or open the Azure portal, click on "Microsoft Entra ID",
and it should be there.

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.PARAMETER AppTenantId
Azure tenant id where the application will be created.

.PARAMETER OamProviderId
The Outlook Actionable Message provider ID used in the identifier URI.

.PARAMETER OamAppScope
The scope name to expose in the API. Default is 'Global'.

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.PARAMETER SkipIfApplicationExists
When set to true, the script will skip application creation if it already exists.
Optional

.EXAMPLE
Set-OAMEmailApplication	-SolutionAbbreviation "<solution-abbreviation>" `
                            -EnvironmentAbbreviation "<environment-abbreviation>" `
                            -AppTenantId "<app-tenant-id>" `
                            -OamProviderId "<oam-provider-id>" `
                            -Clean $false `
                            -Verbose
#>

function Set-OAMEmailApplication {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[Guid] $AppTenantId,
		[Parameter(Mandatory = $True)]
		[string] $OamProviderId,
		[Parameter(Mandatory = $False)]
		[string] $OamAppScope = "Global",
		[Parameter(Mandatory = $False)]
		[boolean] $Clean = $False,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipIfApplicationExists = $True,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = 'Stop'
	)
	Write-Host "`nSet-OAMEmailApplication starting...`n"

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent

	if ($global:SkipModuleInstall -ne $true) {
		. ($scriptsDirectory + '/Install-MSGraphIfNeeded.ps1')
		Install-MSGraphIfNeeded
	}

	if ($global:SkipMsGraphLogin -ne $true) {
		# Disconnect any existing session
		Disconnect-MgGraph -ErrorAction SilentlyContinue 

		$requiredScopes = @(
			"Application.ReadWrite.All"
		)
		
		# Connect to Microsoft Graph with required scopes for the target tenant
		Connect-MgGraph -TenantId $AppTenantId -Scopes $requiredScopes
		
		Write-Host "Successfully connected to Microsoft Graph for tenant $AppTenantId"
	}

	#region Delete Application / Service Principal if they already exist
	$oamEmailAppDisplayName = "$SolutionAbbreviation-OAM-$EnvironmentAbbreviation"
	$oamEmailApps = Get-MgApplication -Filter "displayName eq '$oamEmailAppDisplayName'"
	
	# Validate that we don't have multiple applications with the same name
	if ($null -ne $oamEmailApps -and $oamEmailApps.Count -gt 1) {
		Write-Error "Found $($oamEmailApps.Count) applications with the name '$oamEmailAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
		throw "Multiple applications found with the same display name: $oamEmailAppDisplayName"
	}
	
	# Convert to single application object if we have exactly one
	$oamEmailApp = if ($null -ne $oamEmailApps -and $oamEmailApps.Count -eq 1) { $oamEmailApps } else { $null }

	if ($null -ne $oamEmailApp -and $SkipIfApplicationExists -eq $true -and $Clean -eq $false) {
		Write-Host "Application $oamEmailAppDisplayName already exists. Skipping creation..."
		return @{ ApplicationId = $oamEmailApp.AppId; TenantId = $AppTenantId; ApplicationName = $oamEmailAppDisplayName }
	}

	if ($Clean -eq $true -and $null -ne $oamEmailApp) {
		$displayName = $oamEmailApp.DisplayName;
		$objectId = $oamEmailApp.Id;
		try {
			Remove-MgApplication -ApplicationId $objectId
			Write-Host "Removed $displayName..." -ForegroundColor Green;
			$oamEmailApp = $null
		}
		catch {
			Write-Host "Failed to remove $displayName..." -ForegroundColor Red;
			throw
		}
	}
	#endregion

	#region Create Application
	if ($null -eq $oamEmailApp) {
		Write-Host "Creating Azure AD app $oamEmailAppDisplayName"

		# Create app with just display name first
		$oamEmailApp = New-MgApplication -DisplayName $oamEmailAppDisplayName
		
		New-MgServicePrincipal -AppId $oamEmailApp.AppId

		# Generate scope ID for the new app
		$scopeId = [System.Guid]::NewGuid().ToString()

		# First update: Add scope and identifier URI (without pre-authorized apps)
		$initialConfig = @{
			displayName    = $oamEmailAppDisplayName
			identifierUris = @("api://auth-am-$OamProviderId/$($oamEmailApp.AppId)")
			api            = @{
				oauth2PermissionScopes = @(
					@{
						id                      = $scopeId
						adminConsentDescription = "$OamAppScope scope for Outlook Actionable Messages"
						adminConsentDisplayName = $OamAppScope
						isEnabled               = $true
						type                    = "Admin"
						userConsentDescription  = "$OamAppScope scope for Outlook Actionable Messages"
						userConsentDisplayName  = $OamAppScope
						value                   = $OamAppScope
					}
				)
			}
		}
		Update-MgApplication -ApplicationId $oamEmailApp.Id -BodyParameter $initialConfig

		# Second update: Add pre-authorized application now that the scope exists
		$actionableMessagesClientId = "48af08dc-f6d2-435f-b2a7-069abd99c086"
		$preAuthConfig = @{
			api = @{
				oauth2PermissionScopes = @(
					@{
						id                      = $scopeId
						adminConsentDescription = "$OamAppScope scope for Outlook Actionable Messages"
						adminConsentDisplayName = $OamAppScope
						isEnabled               = $true
						type                    = "Admin"
						userConsentDescription  = "$OamAppScope scope for Outlook Actionable Messages"
						userConsentDisplayName  = $OamAppScope
						value                   = $OamAppScope
					}
				)
				preAuthorizedApplications = @(
					@{
						appId                  = $actionableMessagesClientId
						delegatedPermissionIds = @($scopeId)
					}
				)
			}
		}
		Update-MgApplication -ApplicationId $oamEmailApp.Id -BodyParameter $preAuthConfig
		Write-Host "Created Azure AD app $oamEmailAppDisplayName"
	}
	else {
		Write-Host "Azure AD app $oamEmailAppDisplayName already exists."
		Write-Host "Checking if app needs update..."

		# Get existing scope ID if it exists
		$existingScopeId = $null
		if ($null -ne $oamEmailApp.Api -and $null -ne $oamEmailApp.Api.Oauth2PermissionScopes) {
			$existingScopeId = ($oamEmailApp.Api.Oauth2PermissionScopes | Where-Object { $_.Value -eq $OamAppScope }).Id
		}

		$expectedAppConfig = New-OAMEmailValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-OamProviderId $OamProviderId `
			-OamAppScope $OamAppScope `
			-AppId $oamEmailApp.AppId `
			-ScopeId $existingScopeId

		. ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
		$needsUpdate = -not (Test-AppMatchesConfiguration -AppObject $oamEmailApp -ExpectedConfiguration $expectedAppConfig)

		if ($needsUpdate) {
			Write-Host "App $oamEmailAppDisplayName needs update. Updating..."
			Update-MgApplication -ApplicationId $oamEmailApp.Id -BodyParameter $expectedAppConfig
			Write-Host "Finished updating Azure AD app $oamEmailAppDisplayName"
		}
		else {
			Write-Host "No update needed for app $oamEmailAppDisplayName."
		}
	}
	#endregion

	# Disconnect from Microsoft Graph before returning
	if ($global:SkipMsGraphLogin -ne $true) {
		Disconnect-MgGraph -ErrorAction SilentlyContinue

		Write-Host "Disconnected from Microsoft Graph." -ForegroundColor Green
	}

	Write-Host "`nSet-OAMEmailApplication completed.`n"
	return @{ ApplicationId = $oamEmailApp.AppId; TenantId = $AppTenantId; ApplicationName = $oamEmailAppDisplayName }
}

function New-OAMEmailValidationConfiguration {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string] $SolutionAbbreviation,
		
		[Parameter(Mandatory = $true)]
		[string] $EnvironmentAbbreviation,
		
		[Parameter(Mandatory = $true)]
		[string] $OamProviderId,
		
		[Parameter(Mandatory = $false)]
		[string] $OamAppScope = "Global",
		
		[Parameter(Mandatory = $true)]
		[string] $AppId,
		
		[Parameter(Mandatory = $true)]
		[string] $ScopeId
	)
	
	$oamEmailAppDisplayName = "$SolutionAbbreviation-OAM-$EnvironmentAbbreviation"
	$actionableMessagesClientId = "48af08dc-f6d2-435f-b2a7-069abd99c086"
	
	$permissionScope = @{
		id                      = $ScopeId
		adminConsentDescription = "$OamAppScope scope for Outlook Actionable Messages"
		adminConsentDisplayName = $OamAppScope
		isEnabled               = $true
		type                    = "Admin"
		userConsentDescription  = "$OamAppScope scope for Outlook Actionable Messages"
		userConsentDisplayName  = $OamAppScope
		value                   = $OamAppScope
	}
	
	$config = @{
		displayName    = $oamEmailAppDisplayName
		identifierUris = @("api://auth-am-$OamProviderId/$AppId")
		api            = @{
			oauth2PermissionScopes    = @($permissionScope)
			preAuthorizedApplications = @(
				@{
					appId                  = $actionableMessagesClientId
					delegatedPermissionIds = @($ScopeId)
				}
			)
		}
	}
	
	return $config
}

function Test-OAMEmailApplication {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string] $SolutionAbbreviation,
		
		[Parameter(Mandatory = $true)]
		[string] $EnvironmentAbbreviation,
		
		[Parameter(Mandatory = $true)]
		[string] $OamProviderId,
		
		[Parameter(Mandatory = $false)]
		[string] $OamAppScope = "Global"
	)
	
	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
	
	$oamEmailAppDisplayName = "$SolutionAbbreviation-OAM-$EnvironmentAbbreviation"
	
	Write-Host "`n=== Validating Application: $oamEmailAppDisplayName ===" -ForegroundColor Cyan
	
	# Step 1: Check if application exists
	Write-Host "`n[1/3] Checking if application exists..." -ForegroundColor Yellow
	$oamEmailApps = Get-MgApplication -Filter "displayName eq '$oamEmailAppDisplayName'" -All
	
	if ($null -eq $oamEmailApps -or $oamEmailApps.Count -eq 0) {
		$errorMessage = "Application '$oamEmailAppDisplayName' does not exist. Please run the setup script to create it or follow manual setup steps in the documentation."
		Write-Host "❌ $errorMessage" -ForegroundColor Red
		return $false
	}
	
	Write-Host "✅ Application exists." -ForegroundColor Green
	
	# Step 2: Validate uniqueness
	Write-Host "`n[2/3] Validating uniqueness..." -ForegroundColor Yellow
	if ($oamEmailApps.Count -gt 1) {
		$errorMessage = "Found $($oamEmailApps.Count) applications with the name '$oamEmailAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
		Write-Host "❌ $errorMessage" -ForegroundColor Red
		return $false
	}
	
	$oamEmailApp = $oamEmailApps
	Write-Host "   Application ID: $($oamEmailApp.AppId)" -ForegroundColor Gray
	Write-Host "   Object ID: $($oamEmailApp.Id)" -ForegroundColor Gray
	
	# Step 3: Validate configuration
	Write-Host "`n[3/3] Validating configuration..." -ForegroundColor Yellow
	
	# Get existing scope ID if it exists
	$existingScopeId = $null
	if ($null -ne $oamEmailApp.Api -and $null -ne $oamEmailApp.Api.Oauth2PermissionScopes) {
		$existingScopeId = ($oamEmailApp.Api.Oauth2PermissionScopes | Where-Object { $_.Value -eq $OamAppScope }).Id
	}
	
	$expectedAppConfig = New-OAMEmailValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
		-EnvironmentAbbreviation $EnvironmentAbbreviation `
		-OamProviderId $OamProviderId `
		-OamAppScope $OamAppScope `
		-AppId $oamEmailApp.AppId `
		-ScopeId $existingScopeId
	
	$configurationMatches = Test-AppMatchesConfiguration -AppObject $oamEmailApp `
		-ExpectedConfiguration $expectedAppConfig `
		-ShowDetailedReport
	
	if ($configurationMatches) {
		Write-Host "✅ Configuration matches expected values." -ForegroundColor Green
	}
	else {
		Write-Host "❌ Configuration does not match expected values. Please review the configuration settings and make updates as needed." -ForegroundColor Red
		return $false
	}

	Write-Host "`n=== Application Validation Completed Successfully ===" -ForegroundColor Cyan
	
	return $true
}
