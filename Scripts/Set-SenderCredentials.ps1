$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Stores the user name and password of the user (mail sender) in prereqs keyvault

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

.PARAMETER SyncCompletedCCEmailAddress
Email address of secondary recipient to an email when the syc is complete

.PARAMETER SyncDisabledCCEmailAddress
Email address of secondary recipient to an email when the syc is disabled

.EXAMPLE
Set-SenderCredentials	-SubscriptionName "<subscription name>" `
                        -SolutionAbbreviation "gmm" `
                        -EnvironmentAbbreviation "<env>" `
                        -SenderUsername "<sender username>" `
                        -SenderPassword "<sender password>" `
						-SyncCompletedCCEmailAddress "<cc email address when sync is completed>" `
						-SyncDisabledCCEmailAddress "<cc email address when sync is disabled>" `
                        -Verbose
#>
function Set-SenderCredentials {
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
		[string] $SyncCompletedCCEmailAddress,
		[Parameter(Mandatory=$False)]
		[string] $SyncDisabledCCEmailAddress,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-SenderCredentials starting..."

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
		
	Set-AzContext -SubscriptionName $SubscriptionName

	Connect-AzureAD

	. ($scriptsDirectory + '\Scripts\Install-AzKeyVaultModuleIfNeeded.ps1')
	Install-AzKeyVaultModuleIfNeeded

	. ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
	Add-AzAccountIfNeeded
	
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

	if(!($SyncCompletedCCEmailAddress))
    {
        $SyncCompletedCCEmailAddress = "admin@$tenantName.onmicrosoft.com"
    }

	#region Store SyncCompletedCCEmailAddress secret in KeyVault
	$syncCompletedCCKeyVaultSecretName = "syncCompletedCCEmailAddress"
	$syncCompletedCCSecret = ConvertTo-SecureString -AsPlainText -Force  $SyncCompletedCCEmailAddress
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $syncCompletedCCKeyVaultSecretName `
						 -SecretValue $syncCompletedCCSecret
	Write-Verbose "$syncCompletedCCKeyVaultSecretName added to vault..."

	if(!($SyncDisabledCCEmailAddress))
    {
        $SyncDisabledCCEmailAddress = "admin@$tenantName.onmicrosoft.com"
    }

	#region Store SyncDisabledCCEmailAddress secret in KeyVault
	$syncDisabledCCKeyVaultSecretName = "syncDisabledCCEmailAddress"
	$syncDisabledCCSecret = ConvertTo-SecureString -AsPlainText -Force  $SyncDisabledCCEmailAddress
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $syncDisabledCCKeyVaultSecretName `
						 -SecretValue $syncDisabledCCSecret
	Write-Verbose "$syncDisabledCCKeyVaultSecretName added to vault..."

	#endregion
	Write-Verbose "Set-SenderCredentials completed."
}