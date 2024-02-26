function Update-GmmMigrationIfNeeded {
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

	. ($scriptsDirectory + '\Scripts\Set-UpdateGroupMembershipQuery.ps1')
		Set-UpdateGroupMembershipQuery -SubscriptionName $SubscriptionName `
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

	Write-Verbose "Set-UpdateSqlDatabaseNames starting..."
	. ($scriptsDirectory + '\Scripts\Set-UpdateSqlDatabaseNames.ps1')
	Set-UpdateSqlDatabaseNames	-SubscriptionName $SubscriptionName  `
								-SolutionAbbreviation $SolutionAbbreviation `
								-EnvironmentAbbreviation $EnvironmentAbbreviation `
								-Verbose

	Write-Verbose "Set-UpdateSqlDatabaseNames completed."
}