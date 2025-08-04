function Set-PostDeploymentMigrations {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory = $True)]
		[string] $ConnectionString
	)

	Write-Verbose "Set-PostDeploymentMigrations starting..."

    $ScriptsDirectory = Split-Path $PSScriptRoot -Parent

    # Perform any necessary Sql migrations
	. ($ScriptsDirectory + '\PostDeploymentMigrations\Set-SqlMigrationsIfNeeded.ps1')
	Set-SqlMigrationsIfNeeded -SubscriptionName $SubscriptionName -ConnectionString $ConnectionString

	Write-Verbose "Set-PostDeploymentMigrations completed."
}