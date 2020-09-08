$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create Azure DevOps Service Connection and its supporting Service Principal

.DESCRIPTION
Create Azure DevOps Service Connection and its supporting Service Principal

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (or application)

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.PARAMETER OrganizationName
Azure DevOps organization name

.PARAMETER ProjectName
Azure DevOps project name

.EXAMPLE
Set-ServicePrincipalAndServiceConnection    -SolutionAbbreviation "<solution>"  `
						                    -EnvironmentAbbreviation "<environment>" `
						                    -OrganizationName "<organizationName>" `
						                    -ProjectName "<projectName>" `
						                    -Verbose
#>

function Set-ServicePrincipalAndServiceConnection {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory=$True)]
        [string] $EnvironmentAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $OrganizationName,
		[Parameter(Mandatory=$True)]
		[string] $ProjectName
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '/Scripts/Set-ServicePrincipal.ps1')
	$subscriptionId = Set-ServicePrincipal 	-ServicePrincipalName ("$solutionAbbreviation-serviceconnection-$environmentAbbreviation") `
											-SolutionAbbreviation $SolutionAbbreviation `
											-EnvironmentAbbreviation $EnvironmentAbbreviation
	
    . ($scriptsDirectory + '/Scripts/Set-ServiceConnection.ps1')
    Set-ServiceConnection	-SolutionAbbreviation $SolutionAbbreviation `
						    -EnvironmentAbbreviation $EnvironmentAbbreviation `
						    -OrganizationName $OrganizationName `
                            -ProjectName $ProjectName `
                            -SubscriptionId $subscriptionId `
						    -Clean $true `
						    -Verbose
}