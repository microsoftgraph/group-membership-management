function Set-PostDeploymentMigrations {
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
		[switch] $SkipOrphanedAccountRoleAssignmentCleanup
	)

	Write-Verbose "Set-PostDeploymentMigrations starting..."

    $ScriptsDirectory = Split-Path $PSScriptRoot -Parent

    # Perform any necessary Sql migrations
	. ($ScriptsDirectory + '/PostDeploymentMigrations/Set-SqlMigrationsIfNeeded.ps1')
	Set-SqlMigrationsIfNeeded -EnvironmentAbbreviation $EnvironmentAbbreviation -SolutionAbbreviation $SolutionAbbreviation -SubscriptionName $SubscriptionName -ConnectionString $ConnectionString

	# Only run orphaned role assignments cleanup if NOT skipped
	if (-not $SkipOrphanedAccountRoleAssignmentCleanup) {
		Write-Verbose "Running orphaned role assignments cleanup..."
		. ($ScriptsDirectory + '/PostDeploymentMigrations/Set-OrphanedRoleAssignmentsCleanup.ps1')
		$subscriptionId = (Get-AzContext).Subscription.Id
		if ([string]::IsNullOrWhiteSpace($subscriptionId)) {
			throw "Unable to determine the current subscription identifier required for orphaned role assignment cleanup."
		}
		Set-OrphanedRoleAssignmentsCleanup -SubscriptionId $subscriptionId -ScriptsDirectory $ScriptsDirectory -Confirm:$false
	}
	else {
		Write-Verbose "Skipping orphaned role assignments cleanup (SkipOrphanedAccountRoleAssignmentCleanup flag is set)."
		Write-Verbose "To run manually, execute: Set-OrphanedRoleAssignmentsCleanup -Interactive"
	}

	Write-Verbose "Set-PostDeploymentMigrations completed."
}