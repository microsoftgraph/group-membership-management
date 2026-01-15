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
		[switch] $SkipPrincipalVerification,
		[Parameter(Mandatory = $false)]
		[switch] $RunInPipeline
	)

	Write-Verbose "Set-PostDeploymentMigrations starting..."

    $ScriptsDirectory = Split-Path $PSScriptRoot -Parent

	# Auto-detect if running in ADO pipeline (unless explicitly overridden)
	if (-not $RunInPipeline) {
		$isADO = -not [string]::IsNullOrWhiteSpace($env:SYSTEM_TEAMFOUNDATIONCOLLECTIONURI) -or `
				 -not [string]::IsNullOrWhiteSpace($env:BUILD_BUILDID) -or `
				 -not [string]::IsNullOrWhiteSpace($env:RELEASE_RELEASEID)
		
		if ($isADO) {
			$RunInPipeline = $true
			Write-Verbose "Detected ADO pipeline environment. Setting RunInPipeline=true"
		}
	}

    # Perform any necessary Sql migrations
	. ($ScriptsDirectory + '/PostDeploymentMigrations/Set-SqlMigrationsIfNeeded.ps1')
	Set-SqlMigrationsIfNeeded -EnvironmentAbbreviation $EnvironmentAbbreviation -SolutionAbbreviation $SolutionAbbreviation -SubscriptionName $SubscriptionName -ConnectionString $ConnectionString

	# Only run orphaned role assignments cleanup if NOT running in ADO pipeline
	if (-not $RunInPipeline) {
		Write-Verbose "Running orphaned role assignments cleanup..."
		. ($ScriptsDirectory + '/PostDeploymentMigrations/Set-OrphanedRoleAssignmentsCleanup.ps1')
		$subscriptionId = (Get-AzContext).Subscription.Id
		if ([string]::IsNullOrWhiteSpace($subscriptionId)) {
			throw "Unable to determine the current subscription identifier required for orphaned role assignment cleanup."
		}
		Set-OrphanedRoleAssignmentsCleanup -SubscriptionId $subscriptionId -ScriptsDirectory $ScriptsDirectory -SkipPrincipalVerification:$SkipPrincipalVerification -Confirm:$false
	}
	else {
		Write-Verbose "Skipping orphaned role assignments cleanup (running in ADO pipeline)."
		Write-Verbose "To run manually, execute: Set-OrphanedRoleAssignmentsCleanup -Interactive"
	}

	Write-Verbose "Set-PostDeploymentMigrations completed."
}