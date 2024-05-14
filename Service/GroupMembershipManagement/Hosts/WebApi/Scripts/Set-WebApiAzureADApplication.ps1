
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

.PARAMETER TenantId
Azure tenant id where keyvaults exists.

.PARAMETER DevTenantId
If you are testing GMM using a dev tenant, but your Azure Resources exist in a Subscription tied to your organization's tenant, you will need to provide both of these tenant ids.
If you are deploying everything in your organization's tenant, you do not need to provide this value.

.PARAMETER CertificateName
Certificate name
Optional

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.EXAMPLE
Set-WebApiAzureADApplication	-SubscriptionName "<subscription-name>" `
								-SolutionAbbreviation "<solution-abbreviation>" `
								-EnvironmentAbbreviation "<environment-abbreviation>" `
								-TenantId "<tenant-id>" `
								-Clean $false `
								-Verbose
#>

function Set-WebApiAzureADApplication {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[Guid] $TenantId,
		[Parameter(Mandatory = $False)]
		[System.Nullable[Guid]] $DevTenantId,
		[Parameter(Mandatory = $False)]
		[string] $CertificateName,
		[Parameter(Mandatory = $False)]
		[boolean] $Clean = $False,
		[Parameter(Mandatory = $False)]
		[boolean] $SaveToKeyVault = $True,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipPrompts = $False,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipIfApplicationExists = $True,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-WebApiAzureADApplication starting..."

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent

	. ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
	Install-AzModuleIfNeeded

	$context = Get-AzContext
	$currentTenantId = $context.Tenant.Id

	if($null -eq $DevTenantId) {
		$DevTenantId = $TenantId
		Write-Host "Please sign in to your tenant."
	} else {
		Write-Host "Please sign in to your dev tenant."
	}

	if($currentTenantId -ne $DevTenantId) {
		Connect-AzAccount -Tenant $DevTenantId
	}

	#region Delete Application / Service Principal if they already exist
	$webApiAppDisplayName = "$SolutionAbbreviation-webapi-$EnvironmentAbbreviation"
	$webApiApp = (Get-AzADApplication -DisplayName $webApiAppDisplayName)

	if ($null -ne $webApiApp -and $SkipIfApplicationExists -eq $true -and $Clean -eq $false) {
		Write-Host "Application $webApiAppDisplayName already exists. Skipping creation..."
		return @{ ApplicationId = $webApiApp.AppId; TenantId = $DevTenantId; }
	}

	if ($Clean) {
		$webApiApp | ForEach-Object {

			$displayName = $_.DisplayName;
			$objectId = $_.Id;
			try {
				Remove-AzADApplication -ObjectId $objectId
				Write-Host "Removed $displayName..." -ForegroundColor Green;
				$webApiApp = $null
			}
			catch {
				Write-Host "Failed to remove $displayName..." -ForegroundColor Red;
			}
		}
	}
	#endregion

	#region Create Application
	if ($null -eq $webApiApp) {
		Write-Verbose "Creating Azure AD app $webApiAppDisplayName"

		$requiredResourceAccess = @{
			ResourceAppId  = "00000003-0000-0000-c000-000000000000";
			ResourceAccess = @(
				@{
					Id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d";
					Type = "Scope"
				}
			)
		}

		# These are the function apps that need to interact with swagger.
		# Add this url -> "https://localhost:7224/swagger/oauth2-redirect.html" if you want to test the WebAPI locally.
		$replyUrls = @("https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/swagger/oauth2-redirect.html")

		$webApiApp = New-AzADApplication	-DisplayName $webApiAppDisplayName `
			-AvailableToOtherTenants $false `
			-ReplyUrls $replyUrls `
			-RequiredResourceAccess $requiredResourceAccess

		New-AzADServicePrincipal -ApplicationId $webApiApp.AppId

		$permissionScope = New-Object Microsoft.Azure.Powershell.Cmdlets.Resources.MSGraph.Models.ApiV10.MicrosoftGraphPermissionScope
		$permissionScope.Id = New-Guid
		$permissionScope.AdminConsentDescription = "WebAPI user impersonation"
		$permissionScope.AdminConsentDisplayName = "WebAPI user impersonation"
		$permissionScope.IsEnabled = $true
		$permissionScope.Type = "User"
		$permissionScope.UserConsentDescription = "WebAPI user impersonation"
		$permissionScope.UserConsentDisplayName = "WebAPI user impersonation"
		$permissionScope.Value = "user_impersonation"

		$api = $webApiApp.Api
		$api.Oauth2PermissionScope = $permissionScope
		$api.RequestedAccessTokenVersion = 2

		Update-AzADApplication -ApplicationId $webApiApp.AppId -Api $api

		Start-Sleep -Seconds 30

		$webSettings = $webApiApp.Web
		$webSettings.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
		$webSettings.ImplicitGrantSetting.EnableIdTokenIssuance = $true

		Update-AzADApplication  -ObjectId $webApiApp.Id `
								-IdentifierUris "api://$($webApiApp.AppId)" `
								-DisplayName $webApiAppDisplayName `
								-Web $webSettings `
								-AvailableToOtherTenants $false

		Start-Sleep -Seconds 30

		$uiApp = Get-AzADApplication -DisplayName "$SolutionAbbreviation-ui-$EnvironmentAbbreviation"
		if($uiApp) {

			 $preAuthApp = New-Object Microsoft.Azure.PowerShell.Cmdlets.Resources.MSGraph.Models.ApiV10.MicrosoftGraphPreAuthorizedApplication
		     $preAuthApp.AppId = $uiApp.AppId
			 $preAuthApp.DelegatedPermissionId = $permissionScope.Id
			 $api.PreAuthorizedApplication = $preAuthApp

			 Update-AzADApplication -ApplicationId $webApiApp.AppId -Api $api
		}
	}
	else {
		$webApiApp.Web.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
		$webApiApp.Web.ImplicitGrantSetting.EnableIdTokenIssuance = $true

		# Add upn to list of claims if it doesn't exist (required by WebApi)
		$optionalClaim = $webApiApp.OptionalClaim;
		$hasUpnClaim = $false;

		foreach ($claim in $optionalClaim.AccessToken) {
			if ("upn" -eq $claim.Name) {
				$hasUpnClaim = $true;
			}
		}

		if (!$hasUpnClaim) {
			$optionalClaim.AccessToken += @{
				Name                 = "upn"
				Source               = $null
				Essential            = $false
				AdditionalProperties = @()
			}
		}

		Write-Verbose "Updating Azure AD app $webApiAppDisplayName"
		Update-AzADApplication	-ObjectId $($webApiApp.Id) `
								-DisplayName $webApiAppDisplayName `
								-OptionalClaim $optionalClaim `
								-Web $webSettings `
								-AvailableToOtherTenants $false
	}

	Start-Sleep -Seconds 30

	# Update roles if needed
	. ($scriptsDirectory + '\Scripts\Set-AppRolesIfNeeded.ps1')
		Set-AppRolesIfNeeded -WebApiObjectId $webApiApp.Id -TenantId $DevTenantId

	if($SaveToKeyVault -eq $false) {
		Write-Verbose "Set-WebApiAzureADApplication completed."
		return @{ ApplicationId = $webApiApp.AppId; TenantId = $DevTenantId; }
	}

	Set-WebAPIKeyVaultSecrets -SubscriptionName $SubscriptionName `
							  -SolutionAbbreviation $SolutionAbbreviation `
							  -EnvironmentAbbreviation $EnvironmentAbbreviation `
							  -TenantId $TenantId `
							  -WebApiApplicationId $webApiApp.AppId `
							  -DevTenantId $DevTenantId `
							  -CertificateName $CertificateName `
							  -SkipPrompts $SkipPrompts

	return @{ ApplicationId = $webApiApp.AppId; TenantId = $DevTenantId; }
	Write-Verbose "Set-WebApiAzureADApplication completed."
}

function Set-WebAPIKeyVaultSecrets {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[Guid] $TenantId,
		[Parameter(Mandatory = $True)]
		[Guid] $WebApiApplicationId,
		[Parameter(Mandatory = $False)]
		[System.Nullable[Guid]] $DevTenantId,
		[Parameter(Mandatory = $False)]
		[string] $CertificateName,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipPrompts = $False,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	# These need to go into the key vault
	$webApiAppTenantId = $DevTenantId;
	$webApiAppClientId = $WebApiApplicationId;

	# Create new secret
	$endDate = [System.DateTime]::Now.AddYears(1)
	$webApiAppClientSecret = Get-AzADApplication -ApplicationId $webApiAppClientId | New-AzADAppCredential -StartDate $(get-date) -EndDate $endDate

	Write-Host (Get-AzContext)

	if ($TenantId -ne $DevTenantId) {
		Write-Host "Please sign in to your primary tenant."
		Connect-AzAccount -Tenant $TenantId
	}

	Set-AzContext -Subscription $SubscriptionName

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
	$keyVault = Get-AzKeyVault -VaultName $keyVaultName

	if ($null -eq $keyVault) {
		throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
	}

	# Store Application (client) ID in KeyVault
	$webApiClientIdKeyVaultSecretName = "webApiClientId"

	Write-Verbose "WebApi application (client) ID is $webApiAppClientId"
	if($SkipPrompts){
		$webApiClientIdSecret = New-Object System.Security.SecureString
		$webApiAppClientId.ToCharArray() | ForEach-Object { $webApiClientIdSecret.AppendChar($_) }
	} else {
		$webApiClientIdSecret = Read-Host -AsSecureString -Prompt "Please take the WebApi application ID from above and paste it here"
	}

	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $webApiClientIdKeyVaultSecretName `
						 -SecretValue $webApiClientIdSecret
	Write-Verbose "$webApiClientIdKeyVaultSecretName added to vault for $webApiAppDisplayName."

	# Store Application secret in KeyVault
	$webApiAppClientSecretName = "webApiClientSecret"

	Write-Verbose "WebApi application client secret is $($webApiAppClientSecret.SecretText)"
	if($SkipPrompts){
		$webApiClientSecret = New-Object System.Security.SecureString
		$webApiAppClientSecret.SecretText.ToCharArray() | ForEach-Object { $webApiClientSecret.AppendChar($_) }
	} else {
		$webApiClientSecret = Read-Host -AsSecureString -Prompt "Please take the WebApi application client secret from above and paste it here"
	}

	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $webApiAppClientSecretName `
						 -SecretValue $webApiClientSecret
	Write-Verbose "$webApiAppClientSecretName added to vault for $webApiAppDisplayName."

	# Store tenantID in KeyVault
	$webApiTenantSecretName = "webApiTenantId"

	Write-Verbose "WebApi tenant ID is $webApiAppTenantId"
	if($SkipPrompts){
		$webApiTenantSecret = New-Object System.Security.SecureString
		$webApiAppTenantId.ToCharArray() | ForEach-Object { $webApiTenantSecret.AppendChar($_) }
	} else {
		$webApiTenantSecret = Read-Host -AsSecureString -Prompt "Please take the WebApi tenant ID from above and paste it here"
	}

	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $webApiTenantSecretName `
						 -SecretValue $webApiTenantSecret
	Write-Verbose "$webApiTenantSecretName added to vault for $webApiAppDisplayName."

	# Store certificate name in KeyVault
	$webApiAppCertificateName = "webApiCertificateName"
	$webApiAppCertificate = Get-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name $webApiAppCertificateName
	$setWebApiCertificate = $false

	if (!$webApiAppCertificate -and !$CertificateName) {
		$CertificateName = "not-set"
		$setWebApiCertificate = $true
	}
 	elseif ($CertificateName) {
		$setWebApiCertificate = $true
	}

	if ($setWebApiCertificate) {

		Write-Verbose "Certificate name is $CertificateName"
		if($SkipPrompts){
			$webApiAppCertificateSecret = New-Object System.Security.SecureString
			$CertificateName.ToCharArray() | ForEach-Object { $webApiAppCertificateSecret.AppendChar($_) }
		} else {
			$webApiAppCertificateSecret = Read-Host -AsSecureString -Prompt "Please take the certificate name from above and paste it here"
		}

		Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
							 -Name $webApiAppCertificateName `
							 -SecretValue $webApiAppCertificateSecret
		Write-Verbose "$webApiAppCertificateName added to vault for $webApiAppDisplayName."
	}

	Write-Verbose "Set-WebApiAzureADApplication completed."
}