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

.EXAMPLE
Set-SenderRecipientCredentials	-SubscriptionName "<subscription name>" `
								-SolutionAbbreviation "gmm" `
								-EnvironmentAbbreviation "<env>" `
								-SenderUsername "<sender username>" `
								-SenderPassword "<sender password>" `
								-SyncCompletedCCEmailAddresses "<cc email addresses when sync is completed>" `
								-SyncDisabledCCEmailAddresses "<cc email addresses when sync is disabled>" `
								-Verbose
#>
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
		[string] $SenderUsername,
		[Parameter(Mandatory=$True)]
		[string] $SenderPassword,
		[Parameter(Mandatory=$False)]
		[string] $SyncCompletedCCEmailAddresses,
		[Parameter(Mandatory=$False)]
		[string] $SyncDisabledCCEmailAddresses,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-SenderRecipientCredentials starting..."

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
		
	. ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
	Add-AzAccountIfNeeded

	Set-AzContext -SubscriptionName $SubscriptionName

	#Connect-AzureAD

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
	$senderUsernameSecret = ConvertTo-SecureString -AsPlainText -Force  $SenderUserName
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $senderUsernameKeyVaultSecretName `
						 -SecretValue $senderUsernameSecret
	Write-Verbose "$senderUsernameKeyVaultSecretName added to vault..."

	#region Store Sender Password secret in KeyVault
	$senderPasswordKeyVaultSecretName = "senderPassword"
	$senderPasswordSecret = ConvertTo-SecureString -AsPlainText -Force  $SenderPassword
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $senderPasswordKeyVaultSecretName `
						 -SecretValue $senderPasswordSecret
	Write-Verbose "$senderPasswordKeyVaultSecretName added to vault..."

	if(!($SyncCompletedCCEmailAddresses))
    {
        $SyncCompletedCCEmailAddresses = "admin@$tenantName.onmicrosoft.com"
    }

	#region Store SyncCompletedCCEmailAddresses secret in KeyVault
	$syncCompletedCCKeyVaultSecretName = "syncCompletedCCEmailAddresses"
	$syncCompletedCCSecret = ConvertTo-SecureString -AsPlainText -Force  $SyncCompletedCCEmailAddresses
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $syncCompletedCCKeyVaultSecretName `
						 -SecretValue $syncCompletedCCSecret
	Write-Verbose "$syncCompletedCCKeyVaultSecretName added to vault..."

	if(!($SyncDisabledCCEmailAddresses))
    {
        $SyncDisabledCCEmailAddresses = "admin@$tenantName.onmicrosoft.com"
    }

	#region Store SyncDisabledCCEmailAddresses secret in KeyVault
	$syncDisabledCCKeyVaultSecretName = "syncDisabledCCEmailAddresses"
	$syncDisabledCCSecret = ConvertTo-SecureString -AsPlainText -Force  $SyncDisabledCCEmailAddresses
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $syncDisabledCCKeyVaultSecretName `
						 -SecretValue $syncDisabledCCSecret
	Write-Verbose "$syncDisabledCCKeyVaultSecretName added to vault..."

	#endregion
	Write-Verbose "Set-SenderRecipientCredentials completed."
}

Set-SenderRecipientCredentials -SubscriptionName "MSFT-STSolution-02" -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "gl" -SenderUsername "gmmmailsender@gracegmm.onmicrosoft.com" -SenderPassword "2IjyP2mHSAkwuMb3sNnmobKjJsK4bp" -SyncCompletedCCEmailAddresses "glovelace@microsoft.com" -SyncDisabledCCEmailAddresses "glovelace@microsoft.com" -Verbose