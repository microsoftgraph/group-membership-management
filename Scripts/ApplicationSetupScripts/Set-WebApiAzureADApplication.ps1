$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal for WebAPI.
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

.PARAMETER CertificateName
Certificate name
Optional

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.PARAMETER SaveToKeyVault
When set to true, the application-related secrets will be saved to the key vault.
Optional

.PARAMETER SkipIfApplicationExists
When set to true, the script will skip application creation if it already exists.
Optional

.PARAMETER CreateNewSecret
When set to true, a new application secret will be created and stored. When false, secret creation is skipped.
Optional

.EXAMPLE
Set-WebApiAzureADApplication	-SubscriptionName "<subscription-name>" `
                                -SolutionAbbreviation "<solution-abbreviation>" `
                                -EnvironmentAbbreviation "<environment-abbreviation>" `
                                -AppTenantId "<app-tenant-id>" `
                                -KeyVaultTenantId "<keyvault-tenant-id>" `
                                -Clean $false `
                                -Verbose
#>

function Set-WebApiAzureADApplication {
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
		[string] $CertificateName,
		[Parameter(Mandatory = $False)]
		[boolean] $Clean = $False,
		[Parameter(Mandatory = $False)]
		[boolean] $SaveToKeyVault = $True,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipIfApplicationExists = $True,
		[Parameter(Mandatory = $False)]
		[boolean] $CreateNewSecret = $True,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Host "`nSet-WebApiAzureADApplication starting...`n"

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
		} -OperationName "Connect to Azure tenant for key vault"

		Invoke-WithRetry -Operation {
			Set-AzContext -SubscriptionName $SubscriptionName
		} -OperationName "Set Azure subscription context for key vault"
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
		} -OperationName "Connect to Microsoft Graph for app setup"
        
        Write-Host "Successfully connected to Microsoft Graph for tenant $AppTenantId"
    }

	#region Delete Application / Service Principal if they already exist
	$webApiAppDisplayName = "$SolutionAbbreviation-webapi-$EnvironmentAbbreviation"
	$webApiApps = Invoke-WithRetry -Operation {
		Get-MgApplication -Filter "displayName eq '$webApiAppDisplayName'"
	} -OperationName "Lookup Web API app registration"
	
    
	# Validate that we don't have multiple applications with the same name
	if ($null -ne $webApiApps -and $webApiApps.Count -gt 1) {
		Write-Error "Found $($webApiApps.Count) applications with the name '$webApiAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
		throw "Multiple applications found with the same display name: $webApiAppDisplayName"
	}
    
	# Convert to single application object if we have exactly one
	$webApiApp = if ($null -ne $webApiApps -and $webApiApps.Count -eq 1) { $webApiApps } else { $null }
	$updatedAPIPermissions = $false

	if ($null -ne $webApiApp -and $SkipIfApplicationExists -eq $true -and $Clean -eq $false) {
		Write-Host "Application $webApiAppDisplayName already exists. Skipping creation..."

		# Update roles if needed
		. ($scriptsDirectory + '/ApplicationSetupScripts/Set-AppRolesIfNeeded.ps1')
		Set-AppRolesIfNeeded -WebApiObjectId $webApiApp.Id -TenantId $AppTenantId

		return @{ ApplicationId = $webApiApp.AppId; TenantId = $AppTenantId; ApplicationName = $webApiAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions; }
	}

	if ($Clean -eq $true -and $null -ne $webApiApp) {
		$displayName = $webApiApp.DisplayName;
		$objectId = $webApiApp.Id;
		try {
			Remove-MgApplication -ApplicationId $objectId
			Write-Host "Removed $displayName..." -ForegroundColor Green;
			$webApiApp = $null
		}
		catch {
			Write-Host "Failed to remove $displayName..." -ForegroundColor Red;
			throw
		}
	}
	#endregion

	# Pre-authorize UI app if it exists
	$uiAppName = "$SolutionAbbreviation-ui-$EnvironmentAbbreviation"
	$uiApps = Invoke-WithRetry -Operation {
		Get-MgApplication -Filter "displayName eq '$uiAppName'"
	} -OperationName "Lookup UI app registration"
	$uiApp = if ($null -ne $uiApps -and $uiApps.Count -eq 1) { $uiApps } else { $null }

	# Alert users in the event of missing UI app
	if ($null -eq $uiApp) {
		Write-Warning "UI application '$uiAppName' not found in tenant $AppTenantId. Pre-authorization will be skipped. The UI will fail to call the WebAPI unless pre-authorization is configured later."
	}

	# Validate that we don't have multiple applications with the same name
	if ($null -ne $uiApp -and $uiApp.Count -gt 1) {
		Write-Error "Found $($uiApp.Count) applications with the name '$uiAppName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
		throw "Multiple applications found with the same display name: $uiAppName"
	}

	Write-Host "UI app for pre-authorization is $($uiApp.DisplayName) with AppId $($uiApp.AppId)"
	
	if ($null -eq $webApiApp) {
		Write-Host "Creating Azure AD app $webApiAppDisplayName"

		$appCreationParameters = New-WebApiValidationConfiguration 	-SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-UiAppId $uiApp.AppId

		# Create application body for Microsoft Graph
		$webApiApp = Invoke-WithCreateRetry `
			-GetExistingOperation {
				Get-MgApplication -Filter "displayName eq '$webApiAppDisplayName'"
			} `
			-CreateOperation {
				New-MgApplication -BodyParameter $appCreationParameters
			} `
			-OperationName "Create Azure AD app $webApiAppDisplayName" `
			-ExistsMessage "Azure AD app '$webApiAppDisplayName' already exists. Skipping creation."
		
		$updatedAPIPermissions = $true
        
		Invoke-WithCreateRetry `
			-GetExistingOperation {
				Get-MgServicePrincipal -Filter "appId eq '$($webApiApp.AppId)'"
			} `
			-CreateOperation {
				New-MgServicePrincipal -AppId $webApiApp.AppId
			} `
			-OperationName "Create service principal for $webApiAppDisplayName" `
			-ExistsMessage "Service principal for '$webApiAppDisplayName' already exists. Skipping creation." | Out-Null

		$permissionScopeId = ($webApiApp.Api.Oauth2PermissionScopes | Where-Object { $_.AdminConsentDisplayName -eq "WebAPI user impersonation" }).Id

		# Update with identifier URI
		$updatedAppParameters = New-WebApiValidationConfiguration 	-SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-UiAppId $uiApp.AppId `
			-AppId $webApiApp.AppId `
			-PermissionScopeId $permissionScopeId

		Invoke-WithRetry -Operation {
			Update-MgApplication -ApplicationId $webApiApp.Id -BodyParameter $updatedAppParameters
		} -OperationName "Update Web API app identifier uri"
		Write-Host "Created Azure AD app $webApiAppDisplayName"
	}
	else {
		Write-Host "Azure AD app $webApiAppDisplayName already exists."
		Write-Host "Checking if app needs update..."

		$existingPermissionScopeId = $null
		try {
			if ($null -ne $webApiApp -and $null -ne $webApiApp.Api -and $null -ne $webApiApp.Api.Oauth2PermissionScopes) {
				$existingPermissionScopeId = ($webApiApp.Api.Oauth2PermissionScopes | Where-Object { $_.AdminConsentDisplayName -eq "WebAPI user impersonation" }).Id
			}
		}
		catch {
			$existingPermissionScopeId = $null
		}

		$expectedAppConfig = New-WebApiValidationConfiguration 	-SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-UiAppId $uiApp.AppId `
			-AppId $webApiApp.AppId `
			-PermissionScopeId $existingPermissionScopeId

		. ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
		$needsUpdate = -not (Test-AppMatchesConfiguration -AppObject $webApiApp -ExpectedConfiguration $expectedAppConfig)

		if ($needsUpdate) {
			Write-Host "App $webApiAppDisplayName needs update. Updating..."
			Invoke-WithRetry -Operation {
				Update-MgApplication -ApplicationId $webApiApp.Id -BodyParameter $expectedAppConfig
			} -OperationName "Update Azure AD app configuration for $webApiAppDisplayName"
			$updatedAPIPermissions = $true
			Write-Host "Finished updating Azure AD app $webApiAppDisplayName"
		}
		else {
			Write-Host "No update needed for app $webApiAppDisplayName."
		}
	}

	if ($updatedAPIPermissions -eq $true) {
		Write-Host "Waiting 15 seconds for Azure AD replication..."
		Start-Sleep -Seconds 15
		Write-Host "Done waiting for Azure AD replication."
	}

	# Update roles if needed
	. ($scriptsDirectory + '/ApplicationSetupScripts/Set-AppRolesIfNeeded.ps1')
	Set-AppRolesIfNeeded -WebApiObjectId $webApiApp.Id -TenantId $AppTenantId

	# Grant logged in user app roles
	. ($scriptsDirectory + '/ApplicationSetupScripts/Grant-LoggedInUserWebapiAppRoles.ps1')
	Grant-LoggedInUserWebapiAppRoles 	-SolutionAbbreviation $SolutionAbbreviation `
                                  		-EnvironmentAbbreviation $EnvironmentAbbreviation

	if ($SaveToKeyVault -eq $true) {
		Set-WebAPIKeyVaultSecrets `
			-SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-AppTenantId $AppTenantId `
			-WebApiApplicationId $webApiApp.AppId `
			-CertificateName $CertificateName `
			-CreateNewSecret $CreateNewSecret
	}

	# Disconnect from Microsoft Graph before returning
	if ($global:SkipMsGraphLogin -ne $true) {
		Disconnect-MgGraph -ErrorAction SilentlyContinue

		Write-Host "Disconnected from Microsoft Graph." -ForegroundColor Green
	}

	Write-Host "`nSet-WebApiAzureADApplication completed.`n"
	return @{ ApplicationId = $webApiApp.AppId; TenantId = $AppTenantId; ApplicationName = $webApiAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions; }
}

function Set-WebAPIKeyVaultSecrets {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[Guid] $AppTenantId,
		[Parameter(Mandatory = $True)]
		[Guid] $WebApiApplicationId,
		[Parameter(Mandatory = $False)]
		[string] $CertificateName,
		[Parameter(Mandatory = $False)]
		[boolean] $CreateNewSecret = $True,
		[AllowNull()]
		[Parameter(Mandatory = $False)]
		[string] $AppSecret = $null,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')
	. ($scriptsDirectory + '/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')
	. ($scriptsDirectory + '/ReusableModules/Set-KeyVaultSecretWithFirewallRetry.ps1')

	# These need to go into the key vault
	$webApiAppTenantId = $AppTenantId;
	$webApiAppClientId = $WebApiApplicationId;
	$webApiAppDisplayName = "$SolutionAbbreviation-webapi-$EnvironmentAbbreviation"

	# Create new secret if requested
	$webApiAppClientSecret = $AppSecret
	if ($CreateNewSecret -eq $true) {
		$endDate = [System.DateTime]::Now.AddYears(1)
		$passwordCredential = @{
			displayName   = "GMM Generated Secret"
			startDateTime = [System.DateTime]::Now
			endDateTime   = $endDate
		}
		$appObjectId = (Invoke-WithRetry -Operation {
			Get-MgApplication -Filter "appId eq '$webApiAppClientId'"
		} -OperationName "Get web api app by app id for secret creation").Id

		$webApiAppClientSecret = (Invoke-WithRetry -Operation {
			Add-MgApplicationPassword -ApplicationId $appObjectId -PasswordCredential $passwordCredential
		} -OperationName "Create web api app secret").SecretText
		Write-Host "Created new application secret for app $webApiAppClientId"
	}
	else {
		Write-Host "Skipping secret creation as CreateNewSecret is set to false"
	}

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
	$keyVault = Invoke-WithRetry -Operation {
		Get-AzKeyVault -VaultName $keyVaultName
	} -OperationName "Get prereqs key vault for web api secrets"

	if ($null -eq $keyVault) {
		throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
	}

	# Store Application (client) ID in KeyVault
	$webApiClientIdKeyVaultSecretName = "webApiClientId"

	Write-Host "WebApi application (client) ID is $webApiAppClientId"
	$webApiClientIdSecret = New-Object System.Security.SecureString
	$webApiAppClientId.ToString().ToCharArray() | ForEach-Object { $webApiClientIdSecret.AppendChar($_) }

	Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
		-ResourceGroup $keyVault.ResourceGroupName `
		-SecretName $webApiClientIdKeyVaultSecretName `
		-SecretValue $webApiClientIdSecret

	Write-Host "$webApiClientIdKeyVaultSecretName added to vault for $webApiAppDisplayName."

	# Store Application secret in KeyVault (only if a new secret was created)
	if (-not [string]::IsNullOrEmpty($webApiAppClientSecret)) {
		$webApiAppClientSecretName = "webApiClientSecret"

		Write-Host "Storing WebApi application client secret in KeyVault"
		$webApiClientSecret = New-Object System.Security.SecureString
		$webApiAppClientSecret.ToCharArray() | ForEach-Object { $webApiClientSecret.AppendChar($_) }

		Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
			-ResourceGroup $keyVault.ResourceGroupName `
			-SecretName $webApiAppClientSecretName `
			-SecretValue $webApiClientSecret

		Write-Host "$webApiAppClientSecretName added to vault for $webApiAppDisplayName."
	}
	else {
		Write-Host "Skipping application secret storage as no new secret was created"
	}

	# Store tenantID in KeyVault
	$webApiTenantSecretName = "webApiTenantId"

	Write-Host "WebApi tenant ID is $webApiAppTenantId"
	$webApiTenantSecret = New-Object System.Security.SecureString
	$webApiAppTenantId.ToString().ToCharArray() | ForEach-Object { $webApiTenantSecret.AppendChar($_) }

	Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
		-ResourceGroup $keyVault.ResourceGroupName `
		-SecretName $webApiTenantSecretName `
		-SecretValue $webApiTenantSecret

	Write-Host "$webApiTenantSecretName added to vault for $webApiAppDisplayName."

	# Store certificate name in KeyVault
	$webApiAppCertificateName = "webApiCertificateName"
	$webApiAppCertificate = Get-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName -ResourceGroup $keyVault.ResourceGroupName -SecretName $webApiAppCertificateName -AsPlainText
	$setWebApiCertificate = $false

	if (!$webApiAppCertificate -and !$CertificateName) {
		$CertificateName = "not-set"
		$setWebApiCertificate = $true
	}
	elseif ($CertificateName) {
		$setWebApiCertificate = $true
	}

	if ($setWebApiCertificate) {

		Write-Host "Certificate name is $CertificateName"
		$webApiAppCertificateSecret = New-Object System.Security.SecureString
		$CertificateName.ToCharArray() | ForEach-Object { $webApiAppCertificateSecret.AppendChar($_) }

		Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
			-ResourceGroup $keyVault.ResourceGroupName `
			-SecretName $webApiAppCertificateName `
			-SecretValue $webApiAppCertificateSecret

		Write-Host "$webApiAppCertificateName added to vault for $webApiAppDisplayName."
	}

	Write-Host "Set-WebApiAzureADApplication completed."
}

function New-WebApiValidationConfiguration {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string] $SolutionAbbreviation,
        
		[Parameter(Mandatory = $true)]
		[string] $EnvironmentAbbreviation,
        
		[AllowNull()]
		[Parameter(Mandatory = $false)]
		[string] $AppId,  # If provided, will be used for identifier URI

		[Parameter(Mandatory = $true)]
		[AllowNull()]
		[AllowEmptyString()]
		[string] $UiAppId, 

		[Parameter(Mandatory = $false)]
		[AllowNull()]
		[AllowEmptyString()]
		[string] $PermissionScopeId = $null
	)
    
	$webApiAppDisplayName = "$SolutionAbbreviation-webapi-$EnvironmentAbbreviation"
	$replyUrls = @("https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/swagger/oauth2-redirect.html")
    
	$requiredResourceAccess = @{
		ResourceAppId  = "00000003-0000-0000-c000-000000000000"
		ResourceAccess = @(
			@{
				Id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"
				Type = "Scope"
			}
		)
	}
    
	$permissionScope = @{
		id                      = if (-not [string]::IsNullOrWhiteSpace($PermissionScopeId)) { $PermissionScopeId } else { [System.Guid]::NewGuid().ToString() }
		adminConsentDescription = "WebAPI user impersonation"
		adminConsentDisplayName = "WebAPI user impersonation"
		isEnabled               = $true
		type                    = "User"
		userConsentDescription  = "WebAPI user impersonation"
		userConsentDisplayName  = "WebAPI user impersonation"
		value                   = "user_impersonation"
	}

	# Conditionally create pre-authorized applications array
	$preAuthorizedApplications = @()
	if (-not [string]::IsNullOrWhiteSpace($UiAppId)) {
		$preAuthorizedApplications += @{
			appId                  = $UiAppId
			delegatedPermissionIds = @($permissionScope.id)
		}
	}
    
	$config = @{
		displayName            = $webApiAppDisplayName
		signInAudience         = "AzureADMyOrg"
		requiredResourceAccess = @($requiredResourceAccess)
		isFallbackPublicClient = $false
		web                    = @{
			redirectUris          = $replyUrls
			implicitGrantSettings = @{
				enableAccessTokenIssuance = $true
				enableIdTokenIssuance     = $true
			}
		}
		api                    = @{
			oauth2PermissionScopes      = @($permissionScope)
			requestedAccessTokenVersion = 2
			preAuthorizedApplications   = $preAuthorizedApplications
		}
		optionalClaims         = @{
			accessToken = @(
				@{
					name      = "upn"
					source    = $null
					essential = $false
				}
			)
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

function Test-WebApiApplication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $SolutionAbbreviation,
        
        [Parameter(Mandatory = $true)]
        [string] $EnvironmentAbbreviation
    )
    
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
    
    $webApiAppDisplayName = "$SolutionAbbreviation-webapi-$EnvironmentAbbreviation"
    
    Write-Host "`n=== Validating Application: $webApiAppDisplayName ===" -ForegroundColor Cyan

	# Step 1: Check if UI application exists
	Write-Host "`n[1/3] Checking if UI application exists..." -ForegroundColor Yellow
	$uiAppName = "$SolutionAbbreviation-ui-$EnvironmentAbbreviation"
	$uiApps = Get-MgApplication -Filter "displayName eq '$uiAppName'" -All
	if ($null -eq $uiApps -or $uiApps.Count -eq 0) {
		$errorMessage = "UI Application '$uiAppName' does not exist. Please run the setup script to create it or follow manual setup steps in the documentation."
		Write-Host "❌ $errorMessage" -ForegroundColor Red
		return $false
	}
	if ($uiApps.Count -gt 1) {
		$errorMessage = "Found $($uiApps.Count) applications with the name '$uiAppName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
		Write-Host "❌ $errorMessage" -ForegroundColor Red
		return $false
	}
	$uiApp = $uiApps
	Write-Host "✅ UI Application exists." -ForegroundColor Green

    # Step 1: Check if application exists
    Write-Host "`n[1/3] Checking if application exists..." -ForegroundColor Yellow
    $webApiApps = Get-MgApplication -Filter "displayName eq '$webApiAppDisplayName'" -All
    
    if ($null -eq $webApiApps -or $webApiApps.Count -eq 0) {
        $errorMessage = "Application '$webApiAppDisplayName' does not exist. Please run the setup script to create it or follow manual setup steps in the documentation."
        Write-Host "❌ $errorMessage" -ForegroundColor Red
        return $false
    }
    
    Write-Host "✅ Application exists." -ForegroundColor Green
    
    # Step 2: Validate uniqueness
    Write-Host "`n[2/3] Validating uniqueness..." -ForegroundColor Yellow
    if ($webApiApps.Count -gt 1) {
        $errorMessage = "Found $($webApiApps.Count) applications with the name '$webApiAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
        Write-Host "❌ $errorMessage" -ForegroundColor Red
        return $false
    }
    
    $webApiApp = $webApiApps
    Write-Host "   Application ID: $($webApiApp.AppId)" -ForegroundColor Gray
    Write-Host "   Object ID: $($webApiApp.Id)" -ForegroundColor Gray


    # Step 4: Validate configuration
    Write-Host "`n[3/3] Validating configuration..." -ForegroundColor Yellow
    
    $existingPermissionScopeId = $null
    try {
        if ($null -ne $webApiApp -and $null -ne $webApiApp.Api -and $null -ne $webApiApp.Api.Oauth2PermissionScopes) {
            $existingPermissionScopeId = ($webApiApp.Api.Oauth2PermissionScopes | Where-Object { $_.AdminConsentDisplayName -eq "WebAPI user impersonation" }).Id
        }
    }
    catch {
        $existingPermissionScopeId = $null
    }
    
    $expectedAppConfig = New-WebApiValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -UiAppId $uiApp.AppId `
        -AppId $webApiApp.AppId `
        -PermissionScopeId $existingPermissionScopeId
    
    $configurationMatches = Test-AppMatchesConfiguration -AppObject $webApiApp `
        -ExpectedConfiguration $expectedAppConfig `
        -ShowDetailedReport
    
    if ($configurationMatches) {
        Write-Host "✅ Configuration matches expected values." -ForegroundColor Green
    } else {
        Write-Host "❌ Configuration does not match expected values. Please review the configuration settings and make updates as needed." -ForegroundColor Red
        return $false
    }

    Write-Host "`n=== Application Validation Completed Successfully ===" -ForegroundColor Cyan
    
    return $true
}
							