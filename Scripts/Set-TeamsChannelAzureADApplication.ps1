
$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal that can read and update Teams Channel using Graph.
Be aware that running this in VS Code doesn't work for some reason, it works better if you run it in a regular Powershell session.
You may have to open the created Azure AD app in your demo tenant and consent to the permissions!

Basically, this script is designed to create an Azure AD app with the appropriate permissions in a given tenant
(delegated permissions ChannelMember.ReadWrite.All) and write its credentials to a key vault in another tenant.
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

.PARAMETER TenantIdToCreateAppIn
Azure tenant id where the application is going to be created.

.PARAMETER TenantIdWithKeyVault
Azure tenant id where the prereqs keyvault was created.

.PARAMETER CertificateName
Certificate name
Optional

.PARAMETER SaveToKeyVault
When set to true, the application-related secrets will be saved to the key vault.
Optional

.PARAMETER SkipPrompts
When set to true, the script will not prompt for input and will use the provided values instead for secrets.
Optional

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.EXAMPLE
# these are arbitrary guids and subscription names, you'll have to change them.
Set-TeamsChannelAzureADApplication	-SubscriptionName "<subscription-name>" `
									-SolutionAbbreviation "<solution-abbreviation>" `
									-EnvironmentAbbreviation "<environment-abbreviation>" `
									-TenantIdToCreateAppIn "<app-tenant-id>" `
									-TenantIdWithKeyVault "<keyvault-tenant-id>" `
									-SaveToKeyVault $true `
									-Clean $false `
									-Verbose
#>

function Set-TeamsChannelAzureADApplication {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory=$True)]
		[Guid] $TenantIdToCreateAppIn,
		[Parameter(Mandatory=$True)]
		[Guid] $TenantIdWithKeyVault,
		[Parameter(Mandatory=$False)]
		[string] $CertificateName,
		[Parameter(Mandatory = $False)]
		[boolean] $SaveToKeyVault = $True,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipPrompts = $False,
		[Parameter(Mandatory=$False)]
		[boolean] $Clean = $False,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipIfApplicationExists = $True,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-TeamsChannelAzureADApplication starting..."

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded

	$context = Get-AzContext
	$currentTenantId = $context.Tenant.Id

	if($currentTenantId -ne $TenantIdToCreateAppIn){
		Write-Host "Please sign in as an account that can make Azure AD Apps in your target tenant."
		Connect-AzAccount -Tenant $TenantIdToCreateAppIn
	}

	while ((Set-AzContext -TenantId $TenantIdToCreateAppIn).Tenant.Id -ne $TenantIdToCreateAppIn)
	{
		Write-Host "Please sign in as an account that can make Azure AD Apps in your target tenant."
		Add-AzAccount -TenantId $TenantIdToCreateAppIn
	}

	#region Delete Application / Service Principal if they already exist
    $teamsChannelAppDisplayName = "$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation"
	$teamsChannelApp = (Get-AzADApplication -DisplayName $teamsChannelAppDisplayName)

	if($null -ne $teamsChannelApp -and $Clean -eq $false -and $SkipIfApplicationExists -eq $true)
	{
		Write-Host "Skipping creation of Teams Channel Azure AD application as it already exists."
		Write-Host @{ ApplicationId = $teamsChannelApp.AppId; TenantId = $TenantIdToCreateAppIn; }
		return @{ ApplicationId = $teamsChannelApp.AppId; TenantId = $TenantIdToCreateAppIn; }
	}	
	else {

		if($Clean -eq $true)
		{
			$teamsChannelApp | ForEach-Object {

				$displayName = $_.DisplayName;
				$objectId = $_.Id;
				try {
					Remove-AzADApplication -ObjectId $objectId
					Write-Host "Removed $displayName..." -ForegroundColor Green;
				}
				catch {
					Write-Host "Failed to remove $displayName..." -ForegroundColor Red;
				}
			}
		}
		#endregion

		# These are the function apps that need to interact with the TeamsChannel.
		$replyUrls = @("teamschannelupdater", "teamschannelmembershipobtainer") |
			ForEach-Object { "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$_.azurewebsites.net"};
		$replyUrls += "http://localhost";

		$requiredResourceAccess = @{
			ResourceAppId = "00000003-0000-0000-c000-000000000000";
			ResourceAccess = @()
		}

		$delegatedPermissions = (Get-AzADServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'").Oauth2PermissionScope `
			| Where-Object { ($_.Value -eq "ChannelMember.ReadWrite.All") -or ($_.Value -eq "Channel.ReadBasic.All") } `
			| ForEach-Object { @{Id = $_.Id; Type = "Scope" } }

		$requiredResourceAccess.ResourceAccess = $delegatedPermissions

		#region Create Appplication
		if($null -eq $teamsChannelApp)
		{
			Write-Verbose "Creating Azure AD app $teamsChannelAppDisplayName"
			$teamsChannelApp = New-AzADApplication	-DisplayName $teamsChannelAppDisplayName `
											-ReplyUrls $replyUrls `
											-RequiredResourceAccess $requiredResourceAccess `
											-AvailableToOtherTenants $false `
											-IsFallbackPublicClient

			New-AzADServicePrincipal -ApplicationId $teamsChannelApp.AppId

			$webSettings = $teamsChannelApp.Web
			$webSettings.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
			$webSettings.ImplicitGrantSetting.EnableIdTokenIssuance = $true

			Update-AzADApplication -ObjectId $teamsChannelApp.Id `
								-IdentifierUris "api://$($teamsChannelApp.AppId)" `
								-Web $webSettings
		}
		else
		{
			Write-Verbose "Updating Azure AD app $teamsChannelAppDisplayName"

			$webSettings = $teamsChannelApp.Web
			$webSettings.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
			$webSettings.ImplicitGrantSetting.EnableIdTokenIssuance = $true

			Update-AzADApplication	-ObjectId $($teamsChannelApp.Id) `
									-DisplayName $teamsChannelAppDisplayName `
									-RequiredResourceAccess $requiredResourceAccess `
									-AvailableToOtherTenants $false `
									-Web $webSettings
		}
	}

	if($SaveToKeyVault -eq $false)
	{
		Write-Verbose "Set-TeamsChannelAzureADApplication completed."
		return @{ ApplicationId = $teamsChannelApp.AppId; TenantId = $TenantIdToCreateAppIn; }
	}

	Set-TeamsChannelAppKeyVaultSecrets -SubscriptionName $SubscriptionName `
								-SolutionAbbreviation $SolutionAbbreviation `
								-EnvironmentAbbreviation $EnvironmentAbbreviation `
								-TenantIdToCreateAppIn $TenantIdToCreateAppIn `
								-TenantIdWithKeyVault $TenantIdWithKeyVault `
								-ApplicationClientId $teamsChannelApp.AppId `
								-CertificateName $CertificateName `
								-SkipPrompts $SkipPrompts

	return @{ ApplicationId = $teamsChannelApp.AppId; TenantId = $TenantIdToCreateAppIn; }
	Write-Verbose "Set-TeamsChannelAzureADApplication completed."
}


function Set-TeamsChannelAppKeyVaultSecrets {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory=$True)]
		[Guid] $TenantIdToCreateAppIn,
		[Parameter(Mandatory=$True)]
		[Guid] $TenantIdWithKeyVault,
		[Parameter(Mandatory=$True)]
		[Guid] $ApplicationClientId,
		[Parameter(Mandatory=$False)]
		[string] $CertificateName,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipPrompts = $False
	)

	# These need to go into the key vault
	$teamsChannelAppTenantId = $TenantIdToCreateAppIn;
	$teamsChannelAppClientId = $ApplicationClientId;

	# Create new secret
	$endDate = [System.DateTime]::Now.AddYears(1)
    $teamsChannelAppClientSecret = Get-AzADApplication -ApplicationId $teamsChannelAppClientId | New-AzADAppCredential -StartDate $(get-date) -EndDate $endDate

	if ($TenantIdToCreateAppIn -ne $TenantIdWithKeyVault) {
		Write-Host "Please sign in to your primary tenant."
		Connect-AzAccount -Tenant $TenantIdWithKeyVault
	}

	Set-AzContext -SubscriptionName $SubscriptionName
   	Write-Host (Get-AzContext)

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
	{
		throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
    }

	# Store Application (client) ID in KeyVault
    $teamsClientIdKeyVaultSecretName = "teamsChannelAppClientId"

    Write-Verbose "Teams Channel application (client) ID is $teamsChannelAppClientId"
	if($SkipPrompts){
		$teamsClientIdSecret = New-Object System.Security.SecureString
		$teamsChannelAppClientId.ToString().ToCharArray() | ForEach-Object { $teamsClientIdSecret.AppendChar($_) }
	} else {
		$teamsClientIdSecret = Read-Host -AsSecureString -Prompt "Please take the teams channel application ID from above and paste it here"
	}

	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $teamsClientIdKeyVaultSecretName `
						 -SecretValue $teamsClientIdSecret
	Write-Verbose "$teamsClientIdKeyVaultSecretName added to vault for $teamsChannelAppDisplayName."

	# Store Application secret in KeyVault
	$teamsChannelAppClientSecretName = "teamsChannelAppClientSecret"

    Write-Verbose "Teams Channel application client secret is $($teamsChannelAppClientSecret.SecretText)"
	if($SkipPrompts){
		$teamsClientSecret = New-Object System.Security.SecureString
		$teamsChannelAppClientSecret.SecretText.ToCharArray() | ForEach-Object { $teamsClientSecret.AppendChar($_) }
	} else {
		$teamsClientSecret = Read-Host -AsSecureString -Prompt "Please take the teams channel application client secret from above and paste it here"
	}

	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
							-Name $teamsChannelAppClientSecretName `
							-SecretValue $teamsClientSecret
	Write-Verbose "$teamsChannelAppClientSecretName added to vault for $teamsChannelAppDisplayName."

	# Store tenantID in KeyVault
	$teamsTenantSecretName = "teamsChannelAppTenantId"

    Write-Verbose "Teams Channel application tenant id is $teamsChannelAppTenantId"
	if($SkipPrompts){
		$teamsTenantSecret = New-Object System.Security.SecureString
		$teamsChannelAppTenantId.ToString().ToCharArray() | ForEach-Object { $teamsTenantSecret.AppendChar($_) }
	} else {
		$teamsTenantSecret = Read-Host -AsSecureString -Prompt "Please take the teams channel application tenant id from above and paste it here"
	}

	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $teamsTenantSecretName `
						 -SecretValue $teamsTenantSecret
    Write-Verbose "$teamsTenantSecretName added to vault for $teamsChannelAppDisplayName."

	# Store certificate name in KeyVault
	$teamsChannelAppCertificateName = "teamsChannelAppCertificateName"
	$teamsChannelAppCertificate = Get-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name $teamsChannelAppCertificateName
    $setteamsChannelAppCertificate = $false

	if(!$teamsChannelAppCertificate -and !$CertificateName){
		$CertificateName = "not-set"
		$setteamsChannelAppCertificate = $true
	} elseif ($CertificateName) {
		$setteamsChannelAppCertificate = $true
	}

	if($setteamsChannelAppCertificate){
		Write-Verbose "Certificate name is $CertificateName"
		if($SkipPrompts){
			$teamsChannelAppCertificateSecret = New-Object System.Security.SecureString
			$CertificateName.ToCharArray() | ForEach-Object { $teamsChannelAppCertificateSecret.AppendChar($_) }
		} else {
			$teamsChannelAppCertificateSecret = Read-Host -AsSecureString -Prompt "Please take the certificate name from above and paste it here"
		}

		Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
								-Name $teamsChannelAppCertificateName `
								-SecretValue $teamsChannelAppCertificateSecret
		Write-Verbose "$teamsChannelAppCertificateName added to vault for $teamsChannelAppDisplayName."
	}

	Write-Verbose "Set-TeamsChannelAzureADApplication completed."
}