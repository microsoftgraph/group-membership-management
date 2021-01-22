
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

.PARAMETER TenantIdToCreateAppIn
Azure tenant id where the application is going to be created.

.PARAMETER TenantIdWithKeyVault
Azure tenant id where the prereqs keyvault was created.

.PARAMETER CertificateName
Certificate name

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.EXAMPLE
# these are arbitrary guids and subscription names, you'll have to change them.
Set-GraphCredentialsAzureADApplication	-SubscriptionName "GMM-Preprod" `
									-SolutionAbbreviation "gmm" `
									-EnvironmentAbbreviation "<env>" `
									-TenantIdToCreateAppIn "19589c67-5cfd-4863-a0b6-2fb6726ab368" `
									-TenantIdWithKeyVault "8b7e3ea9-d3b0-410e-b4b1-77e1280842cc" `
									-CertificateName "CertificateName"
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
		[Parameter(Mandatory=$True)]
		[string] $CertificateName,		
		[Parameter(Mandatory=$False)]		
		[boolean] $Clean = $False,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-GraphCredentialsAzureADApplication starting..."

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Install-AzAccountsModuleIfNeeded.ps1')
    Install-AzAccountsModuleIfNeeded

    Write-Host "Please sign in as an account that can make Azure AD Apps in your target tenant."
	Connect-AzAccount -Tenant $TenantIdToCreateAppIn

    . ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
	while ((Set-AzContext -TenantId $TenantIdToCreateAppIn).Tenant.Id -ne $TenantIdToCreateAppIn)
	{
		Write-Host "Please sign in as an account that can make Azure AD Apps in your target tenant."
		Add-AzAccount -TenantId $TenantIdToCreateAppIn
	} 
	
	. ($scriptsDirectory + '\Scripts\Install-AzureADModuleIfNeeded.ps1')
	Install-AzureADModuleIfNeeded

	#region Delete Application / Service Principal if they already exist
	Connect-AzureAD -TenantId $TenantIdToCreateAppIn
    $graphAppDisplayName = "$SolutionAbbreviation-Graph-$EnvironmentAbbreviation"
	$graphApp = (Get-AzureADApplication -Filter "DisplayName eq '$graphAppDisplayName'")
	
	if ($null -ne $graphApp)
	{
		$graphApp = $graphApp[0];
	}

	if($graphApp -and $Clean)
	{
        Write-Verbose "Removing existing $graphAppDisplayName"
		Set-AzureADApplication	-ObjectId $graphApp.ObjectId `
                                -AvailableToOtherTenants $false 

        Write-Verbose "Removing Application for $graphAppDisplayName..."
		do
		{
			$secondsToSleep = 5
            Write-Verbose "Waiting $secondsToSleep seconds for $graphAppDisplayName's Application to be removed."
			Start-Sleep -Seconds $secondsToSleep
			Remove-AzureADApplication 	-ObjectId $graphApp.ObjectId `
										-ErrorAction SilentlyContinue `
										-ErrorVariable $removeAdAppError
		}
		while($removeAdAppError)
        $graphApp = Get-AzureADApplication -Filter "DisplayName eq '$graphAppDisplayName'"
        Write-Verbose "Removed existing $graphAppDisplayName"
	}
    #endregion
    
    # These are the function apps that need to interact with the graph.
    $replyUrls = @("graphupdater", "securitygroup") | 
        ForEach-Object { "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$_.azurewebsites.net"};
    $replyUrls += "http://localhost";
    
    # Basically, read the app permissions ("AppRoles" is what this API calls application permissions) Microsoft Graph has, 
	# then filter out the ones we want so we can give them to our AD app.
	# If you assign them to the AppRoles on New-AzureADApplication, it goes somewhere else. The magic key is 
	# RequiredResourceAccess for some reason. The GUIDs are the same, though, they just need a little massaging.
	# see: https://stackoverflow.com/questions/42164581/how-to-configure-a-new-azure-ad-application-through-powershell
	$requiredResourceAccess = New-Object -TypeName "Microsoft.Open.AzureAD.Model.RequiredResourceAccess"
	$requiredResourceAccess.ResourceAccess = (Get-AzureADServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'").AppRoles `
		| Where-Object { ($_.Value -eq "User.Read.All") -or ($_.Value -eq "GroupMember.Read.All") } `
		| ForEach-Object { New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList $_.Id,"Role" }
	$requiredResourceAccess.ResourceAppId = "00000003-0000-0000-c000-000000000000"

	#region Create Appplication
	if($null -eq $graphApp)
	{
		Write-Verbose "Creating Azure AD app $graphAppDisplayName"
		$appIdGuid = New-Guid
        $graphApp = New-AzureADApplication	-DisplayName $graphAppDisplayName `
                                                -IdentifierUris "api://$appIDGuid" `
                                                -ReplyUrls $replyUrls `
                                                -RequiredResourceAccess $requiredResourceAccess `
												-AvailableToOtherTenants $false `
												-Oauth2AllowImplicitFlow $false `
												-PublicClient $false
	}
	else
	{
		Write-Verbose "Updating Azure AD app $graphAppDisplayName"
		Set-AzureADApplication	-ObjectId $($graphApp.ObjectId) `
                                -DisplayName $graphAppDisplayName `
                                -ReplyUrls $replyUrls `
                                -RequiredResourceAccess $requiredResourceAccess `
								-AvailableToOtherTenants $false `
								-Oauth2AllowImplicitFlow $true `
								-PublicClient $false
    }

	# These need to go into the key vault
	$graphAppTenantId = $TenantIdToCreateAppIn;
	$graphAppClientId = $graphApp.AppId;

	# as well as the secret:
	
	do {
		Write-Host "Please sign in with an account that can write to the prereqs key vault."
        Add-AzAccount -TenantId $TenantIdWithKeyVault -Subscription $SubscriptionName
	} while ((Set-AzContext -TenantId $TenantIdWithKeyVault -Subscription $SubscriptionName).Tenant.Id -ne $TenantIdWithKeyVault);

   Write-Host (Get-AzContext)

	. ($scriptsDirectory + '\Scripts\Install-AzKeyVaultModuleIfNeeded.ps1')
	Install-AzKeyVaultModuleIfNeeded
	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
	{
		throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
    }

	# Store Application (client) ID in KeyVault
    Write-Verbose "Application (client) ID is $graphAppClientId"
	
    $graphClientIdKeyVaultSecretName = "graphAppClientId"
	$graphClientIdSecret = ConvertTo-SecureString -AsPlainText -Force  $graphAppClientId
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $graphClientIdKeyVaultSecretName `
						 -SecretValue $graphClientIdSecret
	Write-Verbose "$graphClientIdKeyVaultSecretName added to vault for $graphAppDisplayName."

	# Store certificate name in KeyVault
	$graphAppCertificateName = "graphAppCertificateName"
	$graphAppCertificateSecret = ConvertTo-SecureString -AsPlainText -Force  $CertificateName
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $graphAppCertificateName `
						 -SecretValue $graphAppCertificateSecret
	Write-Verbose "$graphAppCertificateName added to vault for $graphAppDisplayName."

	# Store tenantID in KeyVault
	$graphTenantSecretName = "graphAppTenantId"
	$graphTenantSecret = ConvertTo-SecureString -AsPlainText -Force  $graphAppTenantId
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $graphTenantSecretName `
						 -SecretValue $graphTenantSecret
    Write-Verbose "$graphTenantSecretName added to vault for $graphAppDisplayName."

	Write-Verbose "Set-GraphCredentialsAzureADApplication completed."
}