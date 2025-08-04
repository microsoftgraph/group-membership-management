function Set-SqlMigrationsIfNeeded {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory = $True)]
		[string] $ConnectionString,
		[Parameter(Mandatory = $false)]
		[string]$ScriptsDirectory
	)

	Write-Verbose "Set-SqlMigrationsIfNeeded starting..."

	if (-not $ScriptsDirectory) {
		$ScriptsDirectory = Split-Path $PSScriptRoot -Parent
	}

	. ($ScriptsDirectory + '\Add-AzAccountIfNeeded.ps1')
	Add-AzAccountIfNeeded

	Set-AzContext -SubscriptionName $SubscriptionName

	. ($ScriptsDirectory + '\PostDeploymentMigrations\Set-UpdateSourceQuery.ps1')
	Set-UpdateSourceQuery -ConnectionString $ConnectionString `
		-Verbose

	. ($ScriptsDirectory + '\PostDeploymentMigrations\Set-UpdateDestination.ps1')
	Set-UpdateDestination -ConnectionString $ConnectionString `
		-Verbose

	Write-Verbose "Set-SqlMigrationsIfNeeded completed."
}