
$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal that can read and update the Graph.
Be aware that running this in VS Code doesn't work for some reason, it works better if you run it in a regular Powershell session.
You may have to open the created Azure AD app in your demo tenant and consent to the permissions!

Basically, this script is designed to create an Azure AD app with the appropriate permissions in a given tenant
(application permissions User.Read.All and GroupMember.Read.All) and write its credentials to a key vault in another tenant.
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

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.EXAMPLE
# these are arbitrary guids and subscription names, you'll have to change them.
Set-GraphCredentialsAzureADApplication	-SubscriptionName "<subscription-name>" `
									-SolutionAbbreviation "<solution-abbreviation>" `
									-EnvironmentAbbreviation "<environment-abbreviation>" `
									-TenantIdToCreateAppIn "<app-tenant-id>" `
									-TenantIdWithKeyVault "<keyvault-tenant-id>" `
									-Clean $false `
									-Verbose
#>

function Set-GraphCredentialsAzureADApplication {
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
		[Parameter(Mandatory=$False)]
		[boolean] $Clean = $False,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-GraphCredentialsAzureADApplication starting..."

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded

    Write-Host "Please sign in as an account that can make Azure AD Apps in your target tenant."
	Connect-AzAccount -Tenant $TenantIdToCreateAppIn

	while ((Set-AzContext -TenantId $TenantIdToCreateAppIn).Tenant.Id -ne $TenantIdToCreateAppIn)
	{
		Write-Host "Please sign in as an account that can make Azure AD Apps in your target tenant."
		Add-AzAccount -TenantId $TenantIdToCreateAppIn
	}

	#region Delete Application / Service Principal if they already exist
    $graphAppDisplayName = "$SolutionAbbreviation-Graph-$EnvironmentAbbreviation"
	$graphApps = (Get-AzADApplication -DisplayName $graphAppDisplayName)

	$graphApps | ForEach-Object {

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
    #endregion

    # These are the function apps that need to interact with the graph.
    $replyUrls = @("graphupdater", "groupmembershipobtainer") |
        ForEach-Object { "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$_.azurewebsites.net"};
    $replyUrls += "http://localhost";

	$requiredResourceAccess = @{
		ResourceAppId = "00000003-0000-0000-c000-000000000000";
		ResourceAccess = @()
	}

	$permissions = (Get-AzADServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'").AppRole `
		| Where-Object { ($_.Value -eq "User.Read.All") -or ($_.Value -eq "GroupMember.Read.All") -or ($_.Value -eq "ChannelMember.ReadWrite.All")} `
        | ForEach-Object { @{Id = $_.Id; Type = "Role" } }

	$requiredResourceAccess.ResourceAccess = $permissions

	#region Create Appplication
	if($null -eq $graphApp)
	{
		Write-Verbose "Creating Azure AD app $graphAppDisplayName"
		$graphApp = New-AzADApplication	-DisplayName $graphAppDisplayName `
                                        -ReplyUrls $replyUrls `
                                        -RequiredResourceAccess $requiredResourceAccess `
										-AvailableToOtherTenants $false `
										-IsFallbackPublicClient

        New-AzADServicePrincipal -ApplicationId $graphApp.AppId

		$webSettings = $graphApp.Web
		$webSettings.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
		$webSettings.ImplicitGrantSetting.EnableIdTokenIssuance = $true

		Update-AzADApplication -ObjectId $graphApp.Id `
		                       -IdentifierUris "api://$($graphApp.AppId)" `
							   -Web $webSettings
	}
	else
	{
		Write-Verbose "Updating Azure AD app $graphAppDisplayName"

		$webSettings = $graphApp.Web
		$webSettings.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
		$webSettings.ImplicitGrantSetting.EnableIdTokenIssuance = $true

		Update-AzADApplication	-ObjectId $($graphApp.Id) `
                                -DisplayName $graphAppDisplayName `
                                -ReplyUrls $replyUrls `
                                -RequiredResourceAccess $requiredResourceAccess `
								-AvailableToOtherTenants $false `
								-Web $webSettings
    }

	# These need to go into the key vault
	$graphAppTenantId = $TenantIdToCreateAppIn;
	$graphAppClientId = $graphApp.AppId;

	# Create new secret
	$endDate = [System.DateTime]::Now.AddYears(1)
    $graphAppClientSecret = Get-AzADApplication -ApplicationId $graphAppClientId | New-AzADAppCredential -StartDate $(get-date) -EndDate $endDate

	do {
		Write-Host "Please sign in with an account that can write to the prereqs key vault."
        Add-AzAccount -TenantId $TenantIdWithKeyVault -Subscription $SubscriptionName
	} while ((Set-AzContext -TenantId $TenantIdWithKeyVault -Subscription $SubscriptionName).Tenant.Id -ne $TenantIdWithKeyVault);

   Write-Host (Get-AzContext)

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
	{
		throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
    }

	# Store Application (client) ID in KeyVault
    Write-Verbose "Application (client) ID is $graphAppClientId"

    $graphClientIdKeyVaultSecretName = "graphAppClientId"
	$graphClientIdSecret = ConvertTo-SecureString -AsPlainText -Force $graphAppClientId
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $graphClientIdKeyVaultSecretName `
						 -SecretValue $graphClientIdSecret
	Write-Verbose "$graphClientIdKeyVaultSecretName added to vault for $graphAppDisplayName."

	# Store Application secret in KeyVault
	$graphAppClientSecretName = "graphAppClientSecret"
    $graphClientSecret = ConvertTo-SecureString -AsPlainText -Force $($graphAppClientSecret.SecretText)
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
							-Name $graphAppClientSecretName `
							-SecretValue $graphClientSecret
	Write-Verbose "$graphAppClientSecretName added to vault for $graphAppDisplayName."

	# Store tenantID in KeyVault
	$graphTenantSecretName = "graphAppTenantId"
	$graphTenantSecret = ConvertTo-SecureString -AsPlainText -Force  $graphAppTenantId
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $graphTenantSecretName `
						 -SecretValue $graphTenantSecret
    Write-Verbose "$graphTenantSecretName added to vault for $graphAppDisplayName."

	# Store certificate name in KeyVault
	$graphAppCertificateName = "graphAppCertificateName"
	$graphAppCertificate = Get-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name $graphAppCertificateName
    $setGraphAppCertificate = $false

	if(!$graphAppCertificate -and !$CertificateName){
		$CertificateName = "not-set"
		$setGraphAppCertificate = $true
	} elseif ($CertificateName) {
		$setGraphAppCertificate = $true
	}

	if($setGraphAppCertificate){
		$graphAppCertificateSecret = ConvertTo-SecureString -AsPlainText -Force $CertificateName
		Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
								-Name $graphAppCertificateName `
								-SecretValue $graphAppCertificateSecret
		Write-Verbose "$graphAppCertificateName added to vault for $graphAppDisplayName."
	}

	Write-Verbose "Set-GraphCredentialsAzureADApplication completed."
}