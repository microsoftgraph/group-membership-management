
$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal for UI.
Be aware that running this in VS Code doesn't work for some reason, it works better if you run it in a regular Powershell session.
You may have to open the created Azure AD app in your demo tenant and consent to the permissions!

Basically, this script is designed to create an Azure AD app and write its credentials to a key vault in another tenant.
This should be able to work when the AD app and the target key vault are in the same tenant. Just pass the same tenant ID to both
parameters.

To find the tenant ID for a tenant, you can run Connect-AzureAD in Powershell, or open the Azure portal, click on "Azure Active Directory",
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
Set-UIAzureADApplication	-SubscriptionName "<subscription-name>" `
								-SolutionAbbreviation "<solution-abbreviation>" `
								-EnvironmentAbbreviation "<environment-abbreviation>" `
								-AppTenantId "<app-tenant-id>" `
								-KeyVaultTenantId "<keyvault-tenant-id>" `
								-Clean $false `
								-Verbose
#>

function Set-UIAzureADApplication {
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
	Write-Verbose "Set-UIAzureADApplication starting..."

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded

    Write-Host "Please sign in as an account that can make Azure AD Apps in your target tenant."
	# Connect to keyvault tenant
	# we are going to create the multitenant app here first
	Connect-AzAccount -Tenant $KeyVaultTenantId
	Set-AzContext -Subscription $SubscriptionName

	#region Delete Application / Service Principal if they already exist
    $uiAppDisplayName = "$SolutionAbbreviation-ui-$EnvironmentAbbreviation"
	$uiApp = (Get-AzADApplication -DisplayName $uiAppDisplayName)

	if($Clean){
		$uiApp | ForEach-Object {

			$displayName = $_.DisplayName;
			$objectId = $_.Id;
			try {
				Remove-AzADApplication -ObjectId $objectId
				Write-Host "Removed $displayName..." -ForegroundColor Green;
				$uiApp = $null
			}
			catch {
				Write-Host "Failed to remove $displayName..." -ForegroundColor Red;
			}
		}
    }
    #endregion

    if ($EnvironmentAbbreviation -eq "prodv2")
	{
		$url = "https://$SolutionAbbreviation.microsoft.com"

	} else {
		$url = "https://$EnvironmentAbbreviation.$SolutionAbbreviation.microsoft.com"
	}

	$replyUrls = @("http://localhost:3000", $url)

	#region Create Appplication
	if($null -eq $uiApp)
	{
		Write-Verbose "Creating Azure AD app $uiAppDisplayName"

		$requiredResourceAccess = @{
			ResourceAppId = "00000003-0000-0000-c000-000000000000";
			ResourceAccess = @(
				@{
					Id = "e1fe6dd8-ba31-4d61-89e7-88639da4683d";
					Type = "Scope"
				}
			)
			}

		$uiApp = New-AzADApplication	-DisplayName $uiAppDisplayName `
											-AvailableToOtherTenants $false `
											-SPARedirectUri $replyUrls `
											-RequiredResourceAccess $requiredResourceAccess

		New-AzADServicePrincipal -ApplicationId $uiApp.AppId

		$webSettings = $uiApp.Web
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

		$appRoles = $uiApp.AppRole
		$appRoles += $readerRole
		$appRoles += $adminRole

        Update-AzADApplication -ObjectId $uiApp.Id `
		                       -IdentifierUris "api://$($uiApp.AppId)" `
							   -DisplayName $uiAppDisplayName `
							   -Web $webSettings `
							   -AppRole $appRoles `
							   -AvailableToOtherTenants $true
	}
	else
	{
		$uiApp.Web.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
		$uiApp.Web.ImplicitGrantSetting.EnableIdTokenIssuance = $true

		Write-Verbose "Updating Azure AD app $uiAppDisplayName"
		Update-AzADApplication	-ObjectId $($uiApp.Id) `
                                -DisplayName $uiAppDisplayName `
								-AvailableToOtherTenants $true `
								-Web $webSettings
    }

	Start-Sleep -Seconds 30

	# These need to go into the key vault
	$uiAppTenantId = $AppTenantId;
	$uiAppClientId = $uiApp.AppId;

	# Create new secret
	$endDate = [System.DateTime]::Now.AddYears(1)
    $uiAppClientSecret = Get-AzADApplication -ApplicationId $uiAppClientId | New-AzADAppCredential -StartDate $(get-date) -EndDate $endDate

   Write-Host (Get-AzContext)

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
	{
		throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
    }

	# Store Application (client) ID in KeyVault
    Write-Verbose "Application (client) ID is $uiAppClientId"

    $uiAppIdKeyVaultSecretName = "uiAppId"
	$uiAppIdSecret = ConvertTo-SecureString -AsPlainText -Force $uiAppClientId
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $uiAppIdKeyVaultSecretName `
						 -SecretValue $uiAppIdSecret
	Write-Verbose "$uiAppIdKeyVaultSecretName added to vault for $uiAppDisplayName."

	# Store Application secret in KeyVault
	$uiAppClientSecretName = "uiPasswordCredentialValue"
    $uiPasswordCredentialValue = ConvertTo-SecureString -AsPlainText -Force $($uiAppClientSecret.SecretText)
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
							-Name $uiAppClientSecretName `
							-SecretValue $uiPasswordCredentialValue
	Write-Verbose "$uiAppClientSecretName added to vault for $uiAppDisplayName."

	# Store tenantID in KeyVault
	$uiTenantSecretName = "uiTenantId"
	$uiTenantSecret = ConvertTo-SecureString -AsPlainText -Force $uiAppTenantId
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $uiTenantSecretName `
						 -SecretValue $uiTenantSecret
    Write-Verbose "$uiTenantSecretName added to vault for $uiAppDisplayName."

	Write-Verbose "Set-UIAzureADApplication completed."
}

Set-UIAzureADApplication	-SubscriptionName "MSFT-STSolution-02" `
								-SolutionAbbreviation "gmm" `
								-EnvironmentAbbreviation "lp" `
								-AppTenantId "47422a28-7e87-4738-bd25-d4f338f6f5a7" `
								-KeyVaultTenantId "72f988bf-86f1-41af-91ab-2d7cd011db47" `
								-Clean $false `
								-Verbose