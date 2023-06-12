<#
.SYNOPSIS
Post Deployment Script
This script can be used to run custom steps.

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.EXAMPLE
PostDeploymentScript	-SolutionAbbreviation "<solution>" `
						-EnvironmentAbbreviation "<environment>" `
						-Verbose
#>
function PostDeploymentScript {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation
	)
	Write-Host "PostDeploymentScript starting..."

	$verbose = ($true -eq $PSBoundParameters.Verbose)

	. ($PSScriptRoot + '\Confirm-KeyVaultSecrets.ps1')
	Write-Host "Confirm-KeyVaultSecrets starting..."
	Confirm-KeyVaultSecrets -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
	Write-Host "Confirm-KeyVaultSecrets completed."

	. ($PSScriptRoot + '\Copy-SyncJobsToSQL.ps1')
	Write-Host "Copy-SyncJobsToSQL starting..."
	Copy-SyncJobsToSQL -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
	Write-Host "Copy-SyncJobsToSQL completed."

	Write-Host "PostDeploymentScript completed."
}