$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal for Function Authentication.
Be aware that running this in VS Code doesn't work for some reason, it works better if you run it in a regular Powershell session.
You may have to open the created Azure AD app in your demo tenant and consent to the permissions!

Basically, this script is designed to create an Azure AD app and write its credentials to a key vault in another tenant.
This should be able to work when the AD app and the target key vault are in the same tenant. Just pass the same tenant ID to both
parameters.

To find the tenant ID for a tenant, you can run Connect-AzAccount in Powershell, or open the Azure portal, click on "Microsoft Entra ID",
and it should be there.

You'll be promped to sign in twice. First as someone who can create the Azure AD app in the given tenant and assign it permissions,
then as someone who can write to the prereqs key vault in the other. Make sure you set SubscriptionName to the name of the Azure subscription
that contains the key vault.

.PARAMETER SubscriptionName
Subscription Name on your primary tenant where the keyvaults exists.

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.PARAMETER AppTenantId
Azure tenant id where the application will be created.

.PARAMETER KeyVaultTenantId
Azure tenant id where keyvaults exists.

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.PARAMETER SaveToKeyVault
When set to true, the application-related secrets will be saved to the key vault.
Optional

.PARAMETER SkipIfApplicationExists
When set to true, the script will skip application creation if it already exists.
Optional

.EXAMPLE
Set-FunctionAuthApplication	-SubscriptionName "<subscription-name>" `
                            -SolutionAbbreviation "<solution-abbreviation>" `
                            -EnvironmentAbbreviation "<environment-abbreviation>" `
                            -AppTenantId "<app-tenant-id>" `
                            -KeyVaultTenantId "<keyvault-tenant-id>" `
                            -Clean $false `
                            -Verbose
#>

function Set-FunctionAuthApplication {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[Guid] $AppTenantId,
		[Parameter(Mandatory = $False)]
		[string] $KeyVaultTenantId,
		[Parameter(Mandatory = $False)]
		[string] $SubscriptionName,
		[Parameter(Mandatory = $False)]
		[boolean] $Clean = $False,
		[Parameter(Mandatory = $False)]
		[boolean] $SaveToKeyVault = $True,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipIfApplicationExists = $True,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Host "`nSet-FunctionAuthApplication starting...`n"

	# Validate required parameters when SaveToKeyVault is enabled
	if ($SaveToKeyVault -eq $true) {
		if ([string]::IsNullOrWhiteSpace($SubscriptionName)) {
			throw "SubscriptionName parameter is required when SaveToKeyVault is set to true."
		}
		if ([string]::IsNullOrWhiteSpace($KeyVaultTenantId)) {
			throw "KeyVaultTenantId parameter is required when SaveToKeyVault is set to true."
		}
	}

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

	if ($global:SkipModuleInstall -ne $true) {
		. ($scriptsDirectory + '/Install-MSGraphIfNeeded.ps1')
		Install-MSGraphIfNeeded

		if ($SaveToKeyVault -eq $true) {
			. ($scriptsDirectory + '/Install-AzModuleIfNeeded.ps1')
			Install-AzModuleIfNeeded
		}
	}

	if ($global:SkipAzLogin -ne $true -and $SaveToKeyVault -eq $true) {
		Invoke-WithRetry -Operation {
			Connect-AzAccount -Tenant $KeyVaultTenantId
		} -OperationName "Connect to Azure tenant for FunctionAuth key vault"

		Invoke-WithRetry -Operation {
			Set-AzContext -SubscriptionName $SubscriptionName
		} -OperationName "Set Azure subscription context for FunctionAuth key vault"
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
			Connect-MgGraph -TenantId $AppTenantId -Scopes $requiredScopes
		} -OperationName "Connect to Microsoft Graph for FunctionAuth app setup"
		
		Write-Host "Successfully connected to Microsoft Graph for tenant $AppTenantId"
	}

	#region Delete Application / Service Principal if they already exist
	$functionAuthAppDisplayName = "$SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation"
	$functionAuthApps = Invoke-WithRetry -Operation {
		Get-MgApplication -Filter "displayName eq '$functionAuthAppDisplayName'"
	} -OperationName "Lookup FunctionAuth app registration"
	
	# Validate that we don't have multiple applications with the same name
	if ($null -ne $functionAuthApps -and $functionAuthApps.Count -gt 1) {
		Write-Error "Found $($functionAuthApps.Count) applications with the name '$functionAuthAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
		throw "Multiple applications found with the same display name: $functionAuthAppDisplayName"
	}
	
	# Convert to single application object if we have exactly one
	$functionAuthApp = if ($null -ne $functionAuthApps -and $functionAuthApps.Count -eq 1) { $functionAuthApps } else { $null }
	$updatedAPIPermissions = $false

	if ($null -ne $functionAuthApp -and $SkipIfApplicationExists -eq $true -and $Clean -eq $false) {
		Write-Host "Application $functionAuthAppDisplayName already exists. Skipping creation..."
		return @{ ApplicationId = $functionAuthApp.AppId; TenantId = $AppTenantId; ApplicationName = $functionAuthAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions; }
	}

	if ($Clean -eq $true -and $null -ne $functionAuthApp) {
		$displayName = $functionAuthApp.DisplayName;
		$objectId = $functionAuthApp.Id;
		try {
			Remove-MgApplication -ApplicationId $objectId
			Write-Host "Removed $displayName..." -ForegroundColor Green;
			$functionAuthApp = $null
		}
		catch {
			Write-Host "Failed to remove $displayName..." -ForegroundColor Red;
			throw
		}
	}
	#endregion

	#region Create Application
	if ($null -eq $functionAuthApp) {
		Write-Host "Creating Azure AD app $functionAuthAppDisplayName"

		$appCreationParameters = New-FunctionAuthValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation

		# Create application body for Microsoft Graph
		$functionAuthApp = Invoke-WithCreateRetry `
			-GetExistingOperation {
				Get-MgApplication -Filter "displayName eq '$functionAuthAppDisplayName'"
			} `
			-CreateOperation {
				New-MgApplication -BodyParameter $appCreationParameters
			} `
			-OperationName "Create Azure AD app $functionAuthAppDisplayName" `
			-ExistsMessage "Azure AD app '$functionAuthAppDisplayName' already exists. Skipping creation."
		$updatedAPIPermissions = $true
		
		Invoke-WithCreateRetry `
			-GetExistingOperation {
				Get-MgServicePrincipal -Filter "appId eq '$($functionAuthApp.AppId)'"
			} `
			-CreateOperation {
				New-MgServicePrincipal -AppId $functionAuthApp.AppId
			} `
			-OperationName "Create service principal for $functionAuthAppDisplayName" `
			-ExistsMessage "Service principal for '$functionAuthAppDisplayName' already exists. Skipping creation." | Out-Null

		$permissionScopeId = ($functionAuthApp.Api.Oauth2PermissionScopes | Where-Object { $_.AdminConsentDisplayName -eq "FunctionAuth client impersonation" }).Id

		# Update with identifier URI
		$updatedAppParameters = New-FunctionAuthValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-AppId $functionAuthApp.AppId `
			-PermissionScopeId $permissionScopeId

		Invoke-WithRetry -Operation {
			Update-MgApplication -ApplicationId $functionAuthApp.Id -BodyParameter $updatedAppParameters
		} -OperationName "Update FunctionAuth app identifier uri"
		Write-Host "Created Azure AD app $functionAuthAppDisplayName"
	}
	else {
		Write-Host "Azure AD app $functionAuthAppDisplayName already exists."
		Write-Host "Checking if app needs update..."

		$existingPermissionScopeId = $null
		try {
			if ($null -ne $functionAuthApp -and $null -ne $functionAuthApp.Api -and $null -ne $functionAuthApp.Api.Oauth2PermissionScopes) {
				$existingPermissionScopeId = ($functionAuthApp.Api.Oauth2PermissionScopes | Where-Object { $_.AdminConsentDisplayName -eq "FunctionAuth client impersonation" }).Id
			}
		}
		catch {
			$existingPermissionScopeId = $null
		}

		$expectedAppConfig = New-FunctionAuthValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-AppId $functionAuthApp.AppId `
			-PermissionScopeId $existingPermissionScopeId

		. ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
		$needsUpdate = -not (Test-AppMatchesConfiguration -AppObject $functionAuthApp -ExpectedConfiguration $expectedAppConfig)

		if ($needsUpdate) {
			Write-Host "App $functionAuthAppDisplayName needs update. Updating..."
			Invoke-WithRetry -Operation {
				Update-MgApplication -ApplicationId $functionAuthApp.Id -BodyParameter $expectedAppConfig
			} -OperationName "Update FunctionAuth app configuration"
			$updatedAPIPermissions = $true
			Write-Host "Finished updating Azure AD app $functionAuthAppDisplayName"
		}
		else {
			Write-Host "No update needed for app $functionAuthAppDisplayName."
		}
	}
	#endregion

	if ($updatedAPIPermissions -eq $true) {
		Write-Host "Waiting 15 seconds for Azure AD replication..."
		Start-Sleep -Seconds 15
		Write-Host "Done waiting for Azure AD replication."
	}

	if ($SaveToKeyVault -eq $true) {
		Set-FunctionAuthKeyVaultSecrets `
			-SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-AppTenantId $AppTenantId `
			-FunctionAuthAppClientId $functionAuthApp.AppId
	}

	# Disconnect from Microsoft Graph before returning
	if ($global:SkipMsGraphLogin -ne $true) {
		Disconnect-MgGraph -ErrorAction SilentlyContinue

		Write-Host "Disconnected from Microsoft Graph." -ForegroundColor Green
	}

	Write-Host "`nSet-FunctionAuthApplication completed.`n"
	return @{ ApplicationId = $functionAuthApp.AppId; TenantId = $AppTenantId; ApplicationName = $functionAuthAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions; }
}

function Set-FunctionAuthKeyVaultSecrets {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[Guid] $AppTenantId,
		[Parameter(Mandatory = $True)]
		[Guid] $FunctionAuthAppClientId,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')
	. ($scriptsDirectory + '/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')
	. ($scriptsDirectory + '/ReusableModules/Set-KeyVaultSecretWithFirewallRetry.ps1')

	$functionAuthAppDisplayName = "$SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation"

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
	$keyVault = Invoke-WithRetry -Operation {
		Get-AzKeyVault -VaultName $keyVaultName
	} -OperationName "Get prereqs key vault for FunctionAuth secrets"

	if ($null -eq $keyVault) {
		throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
	}

	# Store Application (client) ID in KeyVault
	$functionAuthAppClientIdKeyVaultSecretName = "functionAuthAppClientId"

	Write-Host "FunctionAuth application (client) ID is $FunctionAuthAppClientId"

    Write-Host "Storing $functionAuthAppClientIdKeyVaultSecretName in Key Vault $($keyVault.VaultName)..."
	$functionAuthAppClientIdSecret = New-Object System.Security.SecureString
	$FunctionAuthAppClientId.ToString().ToCharArray() | ForEach-Object { $functionAuthAppClientIdSecret.AppendChar($_) }
	Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
		-ResourceGroup $keyVault.ResourceGroupName `
		-SecretName $functionAuthAppClientIdKeyVaultSecretName `
		-SecretValue $functionAuthAppClientIdSecret
	Write-Host "$functionAuthAppClientIdKeyVaultSecretName added to vault for $functionAuthAppDisplayName."

	Write-Host "Set-FunctionAuthKeyVaultSecrets completed."
}

function New-FunctionAuthValidationConfiguration {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string] $SolutionAbbreviation,
		
		[Parameter(Mandatory = $true)]
		[string] $EnvironmentAbbreviation,
		
		[AllowNull()]
		[Parameter(Mandatory = $false)]
		[string] $AppId,  # If provided, will be used for identifier URI

		[Parameter(Mandatory = $false)]
		[AllowNull()]
		[AllowEmptyString()]
		[string] $PermissionScopeId = $null
	)
	
	$functionAuthAppDisplayName = "$SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation"
	
	$permissionScope = @{
		id                      = if (-not [string]::IsNullOrWhiteSpace($PermissionScopeId)) { $PermissionScopeId } else { [System.Guid]::NewGuid().ToString() }
		adminConsentDescription = "FunctionAuth client impersonation"
		adminConsentDisplayName = "FunctionAuth client impersonation"
		isEnabled               = $true
		type                    = "User"
		userConsentDescription  = "FunctionAuth client impersonation"
		userConsentDisplayName  = "FunctionAuth client impersonation"
		value                   = "client_impersonation"
	}
	
	$config = @{
		displayName            = $functionAuthAppDisplayName
		signInAudience         = "AzureADMyOrg"
		isFallbackPublicClient = $false
		api                    = @{
			oauth2PermissionScopes      = @($permissionScope)
			requestedAccessTokenVersion = 2
		}
	}
	
	# Add identifier URIs if AppId is provided
	if (-not [string]::IsNullOrWhiteSpace($AppId)) {
		$config.identifierUris = @("api://$AppId")
	}
	else {
		$config.identifierUris = @()  # Empty array for initial creation
	}
	
	return $config
}

function Test-FunctionAuthApplication {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string] $SolutionAbbreviation,
		
		[Parameter(Mandatory = $true)]
		[string] $EnvironmentAbbreviation
	)
	
	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
	
	$functionAuthAppDisplayName = "$SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation"
	
	Write-Host "`n=== Validating Application: $functionAuthAppDisplayName ===" -ForegroundColor Cyan
	
	# Step 1: Check if application exists
	Write-Host "`n[1/3] Checking if application exists..." -ForegroundColor Yellow
	$functionAuthApps = Get-MgApplication -Filter "displayName eq '$functionAuthAppDisplayName'" -All
	
	if ($null -eq $functionAuthApps -or $functionAuthApps.Count -eq 0) {
		$errorMessage = "Application '$functionAuthAppDisplayName' does not exist. Please run the setup script to create it or follow manual setup steps in the documentation."
		Write-Host "❌ $errorMessage" -ForegroundColor Red
		return $false
	}
	
	Write-Host "✅ Application exists." -ForegroundColor Green
	
	# Step 2: Validate uniqueness
	Write-Host "`n[2/3] Validating uniqueness..." -ForegroundColor Yellow
	if ($functionAuthApps.Count -gt 1) {
		$errorMessage = "Found $($functionAuthApps.Count) applications with the name '$functionAuthAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
		Write-Host "❌ $errorMessage" -ForegroundColor Red
		return $false
	}
	
	$functionAuthApp = $functionAuthApps
	Write-Host "   Application ID: $($functionAuthApp.AppId)" -ForegroundColor Gray
	Write-Host "   Object ID: $($functionAuthApp.Id)" -ForegroundColor Gray
	
	# Step 3: Validate configuration
	Write-Host "`n[3/3] Validating configuration..." -ForegroundColor Yellow
	
	$existingPermissionScopeId = $null
	try {
		if ($null -ne $functionAuthApp -and $null -ne $functionAuthApp.Api -and $null -ne $functionAuthApp.Api.Oauth2PermissionScopes) {
			$existingPermissionScopeId = ($functionAuthApp.Api.Oauth2PermissionScopes | Where-Object { $_.AdminConsentDisplayName -eq "FunctionAuth client impersonation" }).Id
		}
	}
	catch {
		$existingPermissionScopeId = $null
	}
	
	$expectedAppConfig = New-FunctionAuthValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
		-EnvironmentAbbreviation $EnvironmentAbbreviation `
		-AppId $functionAuthApp.AppId `
		-PermissionScopeId $existingPermissionScopeId
	
	$configurationMatches = Test-AppMatchesConfiguration -AppObject $functionAuthApp `
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
