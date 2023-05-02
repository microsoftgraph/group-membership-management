function Set-UpdateQuery {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
    	[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation
	)

	Write-Verbose "Set-UpdateQuery starting..."

  	$scriptsDirectory = Split-Path $PSScriptRoot -Parent

  	. ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
		Add-AzAccountIfNeeded

  	Set-AzContext -SubscriptionName $SubscriptionName

	. ($scriptsDirectory + '\Scripts\Set-UpdateSecurityGroupQuery.ps1')
		Set-UpdateSecurityGroupQuery -SubscriptionName $SubscriptionName `
                                 	 -SolutionAbbreviation $SolutionAbbreviation `
							         -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                 	 -Verbose

  	Write-Verbose "Set-UpdateQuery completed."

	Write-Verbose "Set-UpdateDestination starting..."
	. ($scriptsDirectory + '\Scripts\Set-UpdateDestination.ps1')
	Set-UpdateDestination -SubscriptionName $SubscriptionName `
		-SolutionAbbreviation $SolutionAbbreviation `
		-EnvironmentAbbreviation $EnvironmentAbbreviation `
		-Verbose

	Write-Verbose "Set-UpdateDestination completed."
}