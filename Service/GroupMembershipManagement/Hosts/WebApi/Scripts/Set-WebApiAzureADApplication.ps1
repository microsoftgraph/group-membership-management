
$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal for WebAPI.
Be aware that running this in VS Code doesn't work for some reason, it works better if you run it in a regular Powershell session.
You may have to open the created Azure AD app in your demo tenant and consent to the permissions!

Basically, this script is designed to create an Azure AD app and write its credentials to a key vault in another tenant.
This should be able to work when the AD app and the target key vault are in the same tenant. Just pass the same tenant ID to both
parameters.

To find the tenant ID for a tenant, you can run Connect-AzAccount in Powershell, or open the Azure portal, click on "Azure Active Directory",
and it should be there.

You'll be promped to sign in twice. First as someone who can create the Azure AD app in the given tenant and assign it permissions,
then as someone who can write to the prereqs key vault in the other. Make sure you set SubscriptionName to the name of the Azure subscription
that contains the key vault.

.PARAMETER SubscriptionName
Subscription Name

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.PARAMETER AppTenantId
Azure tenant id where the application is going to be installed.

.PARAMETER KeyVaultTenantId
Azure tenant id where keyvaults exists.
The application is going to be created in this tenant and its settings stored in the data keyvault.

.PARAMETER CertificateName
Certificate name
Optional

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.EXAMPLE
# these are arbitrary guids and subscription names, you'll have to change them.
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
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory=$True)]
		[Guid] $AppTenantId,
		[Parameter(Mandatory=$True)]
		[Guid] $KeyVaultTenantId,
		[Parameter(Mandatory=$False)]
		[string] $CertificateName,
		[Parameter(Mandatory=$False)]
		[boolean] $Clean = $False,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-WebApiAzureADApplication starting..."

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded

    Write-Host "Please sign in as an account that can make Azure AD Apps in your target tenant."
	# Connect to keyvault tenant
	# we are going to create the multitenant app here first
	Connect-AzAccount -Tenant $KeyVaultTenantId
	Set-AzContext -Subscription $SubscriptionName

	#region Delete Application / Service Principal if they already exist
    $webApiAppDisplayName = "$SolutionAbbreviation-webapi-$EnvironmentAbbreviation"
	$webApiApp = (Get-AzADApplication -DisplayName $webApiAppDisplayName)

	if($Clean){
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

    # These are the function apps that need to interact with swagger.

	# Add this url -> "https://localhost:7224/swagger/oauth2-redirect.html" if you want to test the WebAPI locally.
    $replyUrls = @("https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/swagger/oauth2-redirect.html")

	#region Create Appplication
	if($null -eq $webApiApp)
	{
		Write-Verbose "Creating Azure AD app $webApiAppDisplayName"

		$requiredResourceAccess = @{
			ResourceAppId = "00000003-0000-0000-c000-000000000000";
			ResourceAccess = @(
				@{
					Id = "e1fe6dd8-ba31-4d61-89e7-88639da4683d";
					Type = "Scope"
				}
			)
			}

		$webApiApp = New-AzADApplication	-DisplayName $webApiAppDisplayName `
											-AvailableToOtherTenants $false `
											-ReplyUrls $replyUrls `
											-RequiredResourceAccess $requiredResourceAccess

		New-AzADServicePrincipal -ApplicationId $webApiApp.AppId

		$webSettings = $webApiApp.Web
        $webSettings.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
		$webSettings.ImplicitGrantSetting.EnableIdTokenIssuance = $true

		# Create new app roles
		[String[]]$memberTypes = "User","Application"

		$readerRole = @{
			DisplayName = "Reader"
			Description = "Read-only role"
			Value = "Reader"
			Id = [Guid]::NewGuid().ToString()
			IsEnabled = $True
			AllowedMemberTypes = @($memberTypes)
		}

		$adminRole = @{
			DisplayName = "Admin"
			Description = "Admin role"
			Value = "Admin"
			Id = [Guid]::NewGuid().ToString()
			IsEnabled = $True
			AllowedMemberTypes = @($memberTypes)
		}

		$appRoles = $webApiApp.AppRole
		$appRoles += $readerRole
		$appRoles += $adminRole

        Update-AzADApplication -ObjectId $webApiApp.Id `
		                       -IdentifierUris "api://$($webApiApp.AppId)" `
							   -DisplayName $webApiAppDisplayName `
							   -Web $webSettings `
							   -AppRole $appRoles `
							   -AvailableToOtherTenants $true
	}
	else
	{
		$webApiApp.Web.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
		$webApiApp.Web.ImplicitGrantSetting.EnableIdTokenIssuance = $true

		# Add upn to list of claims if it doesn't exist (required by WebApi)
		$optionalClaim = $webApiApp.OptionalClaim;
		$hasUpnClaim = $false;

		foreach($claim in $optionalClaim.AccessToken) {
			if ("upn" -eq $claim.Name) {
				$hasUpnClaim = $true;
			}
		}

		if (!$hasUpnClaim) {
			$optionalClaim.AccessToken += @{
				Name = "upn"
				Source = $null
				Essential = $false
				AdditionalProperties = @()
			}
		}

		Write-Verbose "Updating Azure AD app $webApiAppDisplayName"
		Update-AzADApplication	-ObjectId $($webApiApp.Id) `
                                -DisplayName $webApiAppDisplayName `
								-AvailableToOtherTenants $true `
								-OptionalClaim $optionalClaim `
								-Web $webSettings
    }

	Start-Sleep -Seconds 30

	# These need to go into the key vault
	$webApiAppTenantId = $AppTenantId;
	$webApiAppClientId = $webApiApp.AppId;

	# Create new secret
	$endDate = [System.DateTime]::Now.AddYears(1)
    $webApiAppClientSecret = Get-AzADApplication -ApplicationId $webApiAppClientId | New-AzADAppCredential -StartDate $(get-date) -EndDate $endDate

   Write-Host (Get-AzContext)

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
	{
		throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
    }

	# Store Application (client) ID in KeyVault
    Write-Verbose "Application (client) ID is $webApiAppClientId"

    $webApiClientIdKeyVaultSecretName = "webApiClientId"
	$webApiClientIdSecret = ConvertTo-SecureString -AsPlainText -Force $webApiAppClientId
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $webApiClientIdKeyVaultSecretName `
						 -SecretValue $webApiClientIdSecret
	Write-Verbose "$webApiClientIdKeyVaultSecretName added to vault for $webApiAppDisplayName."

	# Store Application secret in KeyVault
	$webApiAppClientSecretName = "webApiClientSecret"
    $webApiClientSecret = ConvertTo-SecureString -AsPlainText -Force $($webApiAppClientSecret.SecretText)
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
							-Name $webApiAppClientSecretName `
							-SecretValue $webApiClientSecret
	Write-Verbose "$webApiAppClientSecretName added to vault for $webApiAppDisplayName."

	# Store tenantID in KeyVault
	$webApiTenantSecretName = "webApiTenantId"
	$webApiTenantSecret = ConvertTo-SecureString -AsPlainText -Force $webApiAppTenantId
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $webApiTenantSecretName `
						 -SecretValue $webApiTenantSecret
    Write-Verbose "$webApiTenantSecretName added to vault for $webApiAppDisplayName."

	# Store certificate name in KeyVault
	$webApiAppCertificateName = "webApiCertificateName"
	$webApiAppCertificate = Get-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name $webApiAppCertificateName
    $setWebApiCertificate = $false

	if(!$webApiAppCertificate -and !$CertificateName){
		$CertificateName = "not-set"
		$setWebApiCertificate = $true
	} elseif ($CertificateName) {
		$setWebApiCertificate = $true
	}

	if($setWebApiCertificate){
		$webApiAppCertificateSecret = ConvertTo-SecureString -AsPlainText -Force $CertificateName
		Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
								-Name $webApiAppCertificateName `
								-SecretValue $webApiAppCertificateSecret
		Write-Verbose "$webApiAppCertificateName added to vault for $webApiAppDisplayName."
	}

	Start-Process "https://login.microsoftonline.com/$AppTenantId/oauth2/authorize?client_id=$($webApiApp.AppId)&response_type=code&prompt=admin_consent&redirect_uri=$($replyUrls[0])"
	Write-Host "Your default browser has been launched and directed to a site (link below) that will prompt for admin consent. Make sure to login with you tenant admin account."
	Write-Host "https://login.microsoftonline.com/$AppTenantId/oauth2/authorize?client_id=$($webApiApp.AppId)&response_type=code&prompt=admin_consent&redirect_uri=$($replyUrls[0])"

	Write-Verbose "Set-WebApiAzureADApplication completed."
}