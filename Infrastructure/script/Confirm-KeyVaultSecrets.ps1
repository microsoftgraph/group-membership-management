<#
.SYNOPSIS
Confirms that KeyVault Secrets exist with valid values.

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.EXAMPLE
Confirm-KeyVaultSecrets	-SolutionAbbreviation "<solution>" `
						-EnvironmentAbbreviation "<environment>" `
						-Verbose
#>
function Confirm-KeyVaultSecrets {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation
	)
    $prereqsKeyVault = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
	$graphAppCertificateName = Get-AzKeyVaultSecret -VaultName $prereqsKeyVault -Name "graphAppCertificateName"
    if(!$graphAppCertificateName){
		Write-Verbose "The graph app certificate name is not set"
		$secret = Read-Host -AsSecureString -Prompt "Please type 'not-set' here to represent the certificate name to use"

		Set-AzKeyVaultSecret -VaultName $prereqsKeyVault -Name "graphAppCertificateName" -SecretValue $secret
	}
}
