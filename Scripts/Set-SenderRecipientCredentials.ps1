$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Stores the sender and secondary recipient information in prereqs keyvault

.PARAMETER SubscriptionName
Subscription Name

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.PARAMETER SenderUsername
Sender Username

.PARAMETER SenderPassword
Sender Password

.PARAMETER SyncCompletedCCEmailAddresses
Comma separated list of email addresseses of secondary recipients to an email when the sync is complete, eg: abc@tenant.com, def@tenant.com

.PARAMETER SyncDisabledCCEmailAddresses
Comma separated list of email addresseses of secondary recipients to an email when the sync is disabled, eg: abc@tenant.com, def@tenant.com

.PARAMETER SupportEmailAddresses
Comma separated list of email addresseses of secondary recipients providing technical support, eg: abc@tenant.com, def@tenant.com

.EXAMPLE

$secureSenderUsername = ConvertTo-SecureString -AsPlainText -Force "<sender username>"
$secureSecurePassword = ConvertTo-SecureString -AsPlainText -Force "<sender password>"
$secureSyncCompletedCCEmailAddresses = ConvertTo-SecureString -AsPlainText -Force "<cc email addresses when sync is completed>"
$secureSyncDisabledCCEmailAddresses = ConvertTo-SecureString -AsPlainText -Force "<cc email addresses when sync is disabled>"
$secureSupportEmailAddresses = ConvertTo-SecureString -AsPlainText -Force "<cc email addresses when sync is disabled>"

Set-SenderRecipientCredentials	-SubscriptionName "<subscription name>" `
								-SolutionAbbreviation "gmm" `
								-EnvironmentAbbreviation "<env>" `
								-SecureSenderUsername $secureSenderUsername `
								-SecureSenderPassword $secureSecurePassword `
								-SecureSyncCompletedCCEmailAddresses $secureSyncCompletedCCEmailAddresses `
								-SecureSyncDisabledCCEmailAddresses $secureSyncDisabledCCEmailAddresses `
								-SecureSupportEmailAddresses $secureSupportEmailAddresses `
								-GmmGraphAppHasMailApplicationPermissions $false `
								-Verbose

#>

function Skip-SenderRecipientCredentials {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation
	)

	$defaultValue = "not-set"
	$defaultSecret = New-Object System.Security.SecureString
	$defaultValue.ToCharArray() | ForEach-Object { $defaultSecret.AppendChar($_) }

	Set-SenderRecipientCredentials `
		-SubscriptionName $SubscriptionName `
		-SolutionAbbreviation $SolutionAbbreviation `
		-EnvironmentAbbreviation $EnvironmentAbbreviation `
		-SecureSenderUsername $defaultSecret `
		-SecureSenderPassword $defaultSecret `
		-SecureSyncCompletedCCEmailAddresses $defaultValue `
		-SecureSyncDisabledCCEmailAddresses $defaultValue `
		-SecureSupportEmailAddresses $defaultValue `
		-GmmGraphAppHasMailApplicationPermissions $false

		$dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
		$appConfigName = "$SolutionAbbreviation-appConfig-$EnvironmentAbbreviation"
		$appConfigObject = Get-AzAppConfigurationStore -ResourceGroupName $dataResourceGroupName -Name $appConfigName;

		Set-AzAppConfigurationKeyValue -Endpoint $appConfigObject.Endpoint `
										-Key "Mail:SkipMailNotifications" `
										-Value "true" `
										-ContentType "boolean" `
										-Etag { tag1="Mail" }

		Write-Host "Set Mail:SkipMailNotifications key with the value 'true'";
}

function Set-SenderRecipientCredentials {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
        [Parameter(Mandatory=$True)]
		[SecureString] $SecureSenderUsername,
		[Parameter(Mandatory=$True)]
		[SecureString] $SecureSenderPassword,
		[Parameter(Mandatory=$False)]
		[SecureString] $SecureSyncCompletedCCEmailAddresses,
		[Parameter(Mandatory=$False)]
		[SecureString] $SecureSyncDisabledCCEmailAddresses,
		[Parameter(Mandatory=$False)]
		[SecureString] $SecureSupportEmailAddresses,
		[Parameter(Mandatory=$False)]
		[boolean] $GmmGraphAppHasMailApplicationPermissions,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-SenderRecipientCredentials starting..."

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent

	. ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
	Add-AzAccountIfNeeded

	Set-AzContext -SubscriptionName $SubscriptionName

	. ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
	Install-AzModuleIfNeeded

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
	{
		throw "The KeyVault ($keyVaultName) does not exist. Unable to continue."
	}

	#region Store Sender Username in KeyVault
    Write-Verbose "Sender Username is $SenderUserName"

    $senderUsernameKeyVaultSecretName = "senderUsername"
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $senderUsernameKeyVaultSecretName `
						 -SecretValue $SecureSenderUsername
	Write-Verbose "$senderUsernameKeyVaultSecretName added to vault..."

	#region Store Sender Password secret in KeyVault
	$senderPasswordKeyVaultSecretName = "senderPassword"
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $senderPasswordKeyVaultSecretName `
						 -SecretValue $SecureSenderPassword
	Write-Verbose "$senderPasswordKeyVaultSecretName added to vault..."

	if(!($SyncCompletedCCEmailAddresses))
    {
        $SyncCompletedCCEmailAddresses = "admin@$tenantName.onmicrosoft.com"
    }

	#region Store SyncCompletedCCEmailAddresses secret in KeyVault
	$syncCompletedCCKeyVaultSecretName = "syncCompletedCCEmailAddresses"
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $syncCompletedCCKeyVaultSecretName `
						 -SecretValue $SecureSyncCompletedCCEmailAddresses
	Write-Verbose "$syncCompletedCCKeyVaultSecretName added to vault..."

	if(!($SyncDisabledCCEmailAddresses))
    {
        $SyncDisabledCCEmailAddresses = "admin@$tenantName.onmicrosoft.com"
    }

	#region Store SyncDisabledCCEmailAddresses secret in KeyVault
	$syncDisabledCCKeyVaultSecretName = "syncDisabledCCEmailAddresses"
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $syncDisabledCCKeyVaultSecretName `
						 -SecretValue $SecureSyncDisabledCCEmailAddresses
	Write-Verbose "$syncDisabledCCKeyVaultSecretName added to vault..."

	#endregion

	if(!($SupportEmailAddresses))
    {
        $SupportEmailAddresses = "admin@$tenantName.onmicrosoft.com"
    }

	#region Store SupportEmailAddresses secret in KeyVault
	$supportEmailAddressesSecretName = "supportEmailAddresses"
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $supportEmailAddressesSecretName `
						 -SecretValue $SecureSupportEmailAddresses
	Write-Verbose "$supportEmailAddressesSecretName added to vault..."

	#endregion

	$dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$appConfigName = "$SolutionAbbreviation-appConfig-$EnvironmentAbbreviation"
	$appConfigObject = Get-AzAppConfigurationStore -ResourceGroupName $dataResourceGroupName -Name $appConfigName;

	#region If GMM Graph App has Application permission for Mail.Send, add app config value to indicate it
	if($GmmGraphAppHasMailApplicationPermissions) {

		Set-AzAppConfigurationKeyValue -Endpoint $appConfigObject.Endpoint `
										-Key "Mail:IsMailApplicationPermissionGranted" `
										-Value "true" `
										-ContentType "boolean" `
										-Etag { tag1="Mail" }

		Write-Host "Updated Mail:IsMailApplicationPermissionGranted key with the value $isMailApplicationPermissionGranted";

	}

	Set-AzAppConfigurationKeyValue -Endpoint $appConfigObject.Endpoint `
		-Key "Mail:SkipMailNotifications" `
		-Value "false" `
		-ContentType "boolean" `
		-Etag { tag1="Mail" }

	#endregion

	Write-Verbose "Set-SenderRecipientCredentials completed."
}