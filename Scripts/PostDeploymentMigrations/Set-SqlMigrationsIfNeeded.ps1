function Set-SqlMigrationsIfNeeded {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
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
	
	if ($global:SkipAzLogin -ne $true) {
		. (Join-Path $ScriptsDirectory 'Add-AzAccountIfNeeded.ps1')
		Add-AzAccountIfNeeded
	}

	Set-AzContext -SubscriptionName $SubscriptionName

	. ($ScriptsDirectory + '/PostDeploymentMigrations/Set-UpdateSourceQuery.ps1')
	Set-UpdateSourceQuery -ConnectionString $ConnectionString `
		-EnvironmentAbbreviation $EnvironmentAbbreviation `
		-SolutionAbbreviation $SolutionAbbreviation `
		-Verbose

	. ($ScriptsDirectory + '/PostDeploymentMigrations/Set-UpdateDestination.ps1')
	Set-UpdateDestination -ConnectionString $ConnectionString `
		-EnvironmentAbbreviation $EnvironmentAbbreviation `
		-SolutionAbbreviation $SolutionAbbreviation `
		-Verbose

	Write-Verbose "Set-SqlMigrationsIfNeeded completed."
}