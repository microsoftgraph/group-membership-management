$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Stores the notifier's actionable message provider id in prereqs keyvault.

.PARAMETER SubscriptionName
Subscription Name

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.PARAMETER ProviderId
Provider Id

.EXAMPLE
$secureProviderId = ConvertTo-SecureString -AsPlainText -Force "<provider id>"

Set-NotifierProviderId	-SubscriptionName "<subscription name>" `
						-SolutionAbbreviation "gmm" `
						-EnvironmentAbbreviation "<env>" `
						-SecureProviderId $secureProviderId `
						-Verbose
#>
function Set-NotifierProviderId {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
        [Parameter(Mandatory=$True)]
		[SecureString] $SecureProviderId,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-NotifierProviderId starting..."

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

	#region Store Provider Id in KeyVault
    $notifierProviderIdKeyVaultSecretName = "notifierProviderId"
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $notifierProviderIdKeyVaultSecretName `
						 -SecretValue $SecureProviderId
	Write-Verbose "$notifierProviderIdKeyVaultSecretName added to vault..."

	#endregion
	Write-Verbose "Set-NotifierProviderId completed."
}