function Test-OrphanedRoleAssignmentPrincipalExists {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string] $ObjectId,
		[Parameter(Mandatory = $false)]
		[string] $ObjectType,
		[Parameter(Mandatory = $false)]
		[string] $DisplayName
	)

	if ([string]::IsNullOrWhiteSpace($ObjectId)) {
		return $false
	}

	# If DisplayName is "Unknown", the principal is orphaned
	if ($DisplayName -eq "Unknown") {
		Write-Verbose "Principal is orphaned (DisplayName='Unknown'): ObjectId=$ObjectId"
		return $false
	}

	$preferredLookupOrder = @()

	switch ($ObjectType) {
		'User' { $preferredLookupOrder += 'Get-AzADUser' }
		'Group' { $preferredLookupOrder += 'Get-AzADGroup' }
		'ServicePrincipal' { $preferredLookupOrder += 'Get-AzADServicePrincipal' }
		'ManagedIdentity' { $preferredLookupOrder += 'Get-AzADServicePrincipal' }
		'Application' { $preferredLookupOrder += 'Get-AzADApplication' }
	}

	$fallbackLookups = @('Get-AzADUser', 'Get-AzADGroup', 'Get-AzADServicePrincipal', 'Get-AzADApplication')
	foreach ($cmd in $fallbackLookups) {
		if ($preferredLookupOrder -notcontains $cmd) {
			$preferredLookupOrder += $cmd
		}
	}

	$notFoundPatterns = 'not\s+found', 'does\s+not\s+exist', 'cannot\s+find', 'Directory_ObjectNotFound', 'Request_ResourceNotFound'
	$authorizationError = $false
	$foundResult = $null

	foreach ($commandName in $preferredLookupOrder) {
		$commandInfo = Get-Command -Name $commandName -ErrorAction SilentlyContinue
		if (-not $commandInfo) {
			continue
		}

		try {
			$result = & $commandInfo.Name -ObjectId $ObjectId -ErrorAction Stop
			if ($null -ne $result) {
				return $true
			}
		}
		catch {
			$message = $_.Exception.Message
			if ($notFoundPatterns | Where-Object { $message -match $_ }) {
				return $false
			}

			# Track authorization errors separately from other indeterminate errors
			if ($message -match 'Authorization_RequestDenied|Insufficient\s+privileges') {
				$authorizationError = $true
				Write-Debug "Authorization denied when attempting to resolve object '$ObjectId' via '$($commandInfo.Name)': $message"
			}
			else {
				Write-Verbose "Unable to resolve object '$ObjectId' via '$($commandInfo.Name)': $message"
			}
		}
	}

	# If we encountered authorization errors, assume the object exists (don't remove it)
	# This is safer than treating authorization errors as indeterminate
	if ($authorizationError) {
		return $true
	}

	return $false
}

function Set-OrphanedRoleAssignmentsCleanup {
	[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
	param(
		[Parameter(Mandatory = $false)]
		[string] $SubscriptionId,
		[Parameter(Mandatory = $false)]
		[string] $Scope,
		[Parameter(Mandatory = $false)]
		[string] $ScriptsDirectory,
		[Parameter(Mandatory = $false)]
		[switch] $SkipPrincipalVerification,
		[Parameter(Mandatory = $false)]
		[switch] $Interactive
	)

	Write-Verbose "Set-OrphanedRoleAssignmentsCleanup starting..."
	Write-Verbose "SkipPrincipalVerification: $SkipPrincipalVerification"

	# Interactive mode for manual execution
	if ($Interactive -or [string]::IsNullOrWhiteSpace($SubscriptionId)) {
		Write-Host ""
		Write-Host "╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
		Write-Host "║          Orphaned Role Assignments Cleanup Tool                ║" -ForegroundColor Cyan
		Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
		Write-Host ""
		Write-Host "This tool removes role assignments for principals that no longer exist in Azure AD."
		Write-Host "You must have 'Owner' or 'User Access Administrator' role on the subscription."
		Write-Host ""

		# Ensure user is logged in
		$currentContext = Get-AzContext
		if (-not $currentContext) {
			Write-Host "Connecting to Azure..." -ForegroundColor Yellow
			Connect-AzAccount | Out-Null
			$currentContext = Get-AzContext
		}

		Write-Host "Current Azure Account: $($currentContext.Account.Id)" -ForegroundColor Green
		Write-Host ""

		# Get subscription
		if ([string]::IsNullOrWhiteSpace($SubscriptionId)) {
			$subscriptions = Get-AzSubscription | Select-Object -Property Name, Id, TenantId
			if ($subscriptions.Count -gt 1) {
				Write-Host "Available subscriptions:"
				$subscriptions | ForEach-Object { Write-Host "  [$($_.Id)] $($_.Name)" }
				Write-Host ""
				$SubscriptionId = Read-Host "Enter Subscription ID"
			}
			elseif ($subscriptions.Count -eq 1) {
				$SubscriptionId = $subscriptions.Id
				Write-Host "Using subscription: $($subscriptions.Name) [$SubscriptionId]" -ForegroundColor Green
			}
			else {
				throw "No subscriptions found. Check your Azure login."
			}
		}

		Write-Host ""
		$response = Read-Host "Continue with cleanup? (yes/no)"
		if ($response -ne 'yes') {
			Write-Host "Cleanup cancelled." -ForegroundColor Yellow
			return
		}
	}

	if ([string]::IsNullOrWhiteSpace($Scope)) {
		$Scope = "/subscriptions/$SubscriptionId"
	}

	if (-not $ScriptsDirectory) {
		$ScriptsDirectory = Split-Path $PSScriptRoot -Parent
	}

	$currentContext = Get-AzContext
	if (-not $currentContext) {
		throw "Set-OrphanedRoleAssignmentsCleanup requires an active Azure context. Run Connect-AzAccount before invoking this function."
	}

	Write-Verbose "Current Azure Context:"
	Write-Verbose "  Account: $($currentContext.Account.Id)"
	Write-Verbose "  Subscription: $($currentContext.Subscription.Name) ($($currentContext.Subscription.Id))"
	Write-Verbose "  Tenant: $($currentContext.Tenant.Id)"
	Write-Verbose "  Environment: $($currentContext.Environment.Name)"

	if ($currentContext.Subscription.Id -ne $SubscriptionId) {
		Write-Verbose "Setting context to subscription: $SubscriptionId"
		Set-AzContext -SubscriptionId $SubscriptionId | Out-Null
		$currentContext = Get-AzContext
	}

	try {
		Write-Verbose "Attempting to retrieve role assignments from scope: $Scope"
		$assignments = Get-AzRoleAssignment -Scope $Scope -ErrorAction Stop
		Write-Verbose "Successfully retrieved role assignments. Found $($assignments.Count) total assignment(s)."
	}
	catch {
		Write-Warning "Unable to enumerate role assignments for scope '$Scope': $($_.Exception.Message)"
		Write-Host ""
		Write-Host "⚠️  Permission Error" -ForegroundColor Red
		Write-Host "Your account needs 'Owner' or 'User Access Administrator' role on this subscription."
		Write-Host ""
		return
	}

	if (-not $assignments) {
		Write-Host "No role assignments found for scope '$Scope'."
		return
	}

	Write-Host "Found $($assignments.Count) total role assignment(s) in scope '$Scope'." -ForegroundColor Cyan

	$orphanedAssignments = @()

	foreach ($assignment in $assignments) {
		$objectId = $assignment.ObjectId
		if ([string]::IsNullOrWhiteSpace($objectId)) {
			Write-Verbose "Skipping assignment with empty ObjectId: DisplayName=$($assignment.DisplayName), RoleDefinitionName=$($assignment.RoleDefinitionName)"
			continue
		}

		if ($SkipPrincipalVerification) {
			# In skip mode, treat all assignments as potential orphans
			# This avoids authorization errors when service principal lacks Directory.Read.All permission
			Write-Verbose "Adding to orphan list (SkipPrincipalVerification mode): ObjectId=$objectId, DisplayName=$($assignment.DisplayName), Type=$($assignment.ObjectType), Role=$($assignment.RoleDefinitionName)"
			$orphanedAssignments += $assignment
		}
		else {
			$exists = Test-OrphanedRoleAssignmentPrincipalExists -ObjectId $objectId -ObjectType $assignment.ObjectType -DisplayName $assignment.DisplayName

			if ($exists -eq $false) {
				Write-Verbose "Adding to orphan list (principal not found): ObjectId=$objectId, DisplayName=$($assignment.DisplayName), Type=$($assignment.ObjectType), Role=$($assignment.RoleDefinitionName)"
				$orphanedAssignments += $assignment
			}
		}
	}

	if ($orphanedAssignments.Count -eq 0) {
		Write-Host "✓ No orphaned role assignments detected for scope '$Scope'." -ForegroundColor Green
		Write-Verbose "Set-OrphanedRoleAssignmentsCleanup completed."
		return
	}

	Write-Host ""
	Write-Host "Found $($orphanedAssignments.Count) orphaned role assignment(s) to remove:" -ForegroundColor Yellow
	Write-Host ""
	$orphanedAssignments | ForEach-Object {
		$principal = $_.DisplayName
		if (-not $principal) { $principal = $_.ObjectId }
		Write-Host "  • $($_.RoleDefinitionName) for '$principal' (Type: $($_.ObjectType))"
	}
	Write-Host ""

	# Ask for confirmation once, upfront
	$response = Read-Host "Remove these $($orphanedAssignments.Count) orphaned role assignment(s)? (yes/no)"
	if ($response -ne 'yes') {
		Write-Host "Cleanup cancelled." -ForegroundColor Yellow
		return
	}

	Write-Host ""

	$removedAssignments = @()

	foreach ($assignment in $orphanedAssignments) {
		$principalLabel = $assignment.DisplayName
		if (-not $principalLabel) {
			$principalLabel = $assignment.SignInName
		}
		if (-not $principalLabel) {
			$principalLabel = $assignment.ObjectId
		}

		try {
			Remove-AzRoleAssignment -InputObject $assignment -ErrorAction Stop | Out-Null
			Write-Host "✓ Removed orphaned role assignment '$($assignment.RoleDefinitionName)' for '$principalLabel'." -ForegroundColor Green
			$removedAssignments += $assignment
		}
		catch {
			Write-Warning "Failed to remove orphaned role assignment '$($assignment.RoleDefinitionName)' for '$principalLabel': $($_.Exception.Message)"
		}
	}

	Write-Host ""
	if ($removedAssignments.Count -gt 0) {
		Write-Host "✓ Successfully removed $($removedAssignments.Count) orphaned role assignment(s)." -ForegroundColor Green
		Write-Verbose "Removed $($removedAssignments.Count) orphaned role assignment(s) from scope '$Scope'."
	}
	else {
		Write-Host "No role assignments were removed." -ForegroundColor Yellow
	}

	Write-Verbose "Set-OrphanedRoleAssignmentsCleanup completed."
}