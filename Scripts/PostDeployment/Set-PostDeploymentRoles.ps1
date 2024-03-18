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
		[string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $False)]
		[System.Collections.ArrayList] $UserPrincipalNames,
        [Parameter(Mandatory = $False)]
		[string] $DataResourceGroupName = $null,
        [Parameter(Mandatory = $False)]
		[string] $ComputeResourceGroupName = $null
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '\PostDeployment\Set-StorageAccountContainerManagedIdentityRoles.ps1')

    Set-StorageAccountContainerManagedIdentityRoles	-SolutionAbbreviation $SolutionAbbreviation `
                                                    -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                                    -DataResourceGroupName $DataResourceGroupName `
                                                    -Verbose

    . ($scriptsDirectory + '\PostDeployment\Set-AppConfigurationManagedIdentityRoles.ps1')
    Set-AppConfigurationManagedIdentityRoles    -SolutionAbbreviation $SolutionAbbreviation `
                                                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                                -DataResourceGroupName $DataResourceGroupName `
                                                -Verbose

    . ($scriptsDirectory + '\PostDeployment\Set-LogAnalyticsReaderRole.ps1')
    Set-LogAnalyticsReaderRole	-SolutionAbbreviation $SolutionAbbreviation `
                                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                -DataResourceGroupName $DataResourceGroupName `
                                -Verbose

    . ($scriptsDirectory + '\PostDeployment\Set-ADFManagedIdentityRoles.ps1')
    Set-ADFManagedIdentityRoles	-SolutionAbbreviation $SolutionAbbreviation `
                                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                -UserPrincipalNames $UserPrincipalNames

    . ($scriptsDirectory + '\PostDeployment\Set-ServiceBusManagedIdentityRoles.ps1')
    Set-ServiceBusManagedIdentityRoles -SolutionAbbreviation $SolutionAbbreviation `
                                       -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                       -DataResourceGroupName $DataResourceGroupName `
                                       -ComputeResourceGroupName $ComputeResourceGroupName `
                                       -Verbose

}