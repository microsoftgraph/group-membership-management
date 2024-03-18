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

	Write-Verbose "Set-UpdateSqlDatabaseNames starting..."
	. ($scriptsDirectory + '\Scripts\Set-UpdateSqlDatabaseNames.ps1')
	Set-UpdateSqlDatabaseNames	-SubscriptionName $SubscriptionName  `
								-SolutionAbbreviation $SolutionAbbreviation `
								-EnvironmentAbbreviation $EnvironmentAbbreviation `
								-Verbose

	Write-Verbose "Set-UpdateSqlDatabaseNames completed."
}