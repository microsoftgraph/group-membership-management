$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
This script runs the role assignment scripts with elevated permissions

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (or application)

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.PARAMETER AppConfigName
App config name

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-PostDeploymentRoles -SolutionAbbreviation "<solutionAbbreviation>" `
                        -EnvironmentAbbreviation "<environmentAbbreviation>" `
						-Verbose
#>

function Set-PostDeploymentRoles {
    [CmdletBinding()]
	param(
        [Parameter(Mandatory=$True)]
        [string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\PostDeployment\Set-StorageAccountContainerManagedIdentityRoles.ps1')
    Set-StorageAccountContainerManagedIdentityRoles	-SolutionAbbreviation $SolutionAbbreviation `
                                                    -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                                    -Verbose

    . ($scriptsDirectory + '\PostDeployment\Set-KeyVaultSecretsUserRoles.ps1')
    Set-KeyVaultSecretsUserRoles	-SolutionAbbreviation $SolutionAbbreviation `
                                    -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                    -Verbose

    . ($scriptsDirectory + '\PostDeployment\Set-AppConfigurationManagedIdentityRoles.ps1')
    Set-AppConfigurationManagedIdentityRoles    -SolutionAbbreviation $SolutionAbbreviation `
                                                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                                -Verbose

    . ($scriptsDirectory + '\PostDeployment\Set-LogAnalyticsReaderRole.ps1')
    Set-LogAnalyticsReaderRole	-SolutionAbbreviation $SolutionAbbreviation `
                                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                -Verbose
}