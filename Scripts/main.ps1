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

}