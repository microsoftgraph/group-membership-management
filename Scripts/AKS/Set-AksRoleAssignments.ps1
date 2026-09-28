function Set-AksRoleAssignment
{
	[CmdletBinding(SupportsShouldProcess = $true)]
	param(
		[Parameter(Mandatory = $True)]
		[string] $ObjectId,
		[Parameter(Mandatory = $True)]
		[string] $RoleDefinitionName,
		[Parameter(Mandatory = $True)]
		[string] $Scope,
		[Parameter(Mandatory = $True)]
		[string] $TargetName
	)

	Invoke-WithCreateRetry `
		-GetExistingOperation {
			Get-AzRoleAssignment -ObjectId $ObjectId -RoleDefinitionName $RoleDefinitionName -Scope $Scope -ErrorAction SilentlyContinue |
				Where-Object { $_.Scope -eq $Scope }
		} `
		-CreateOperation {
			if ($WhatIfPreference)
			{
				Write-Host "Would add role assignment $RoleDefinitionName to $TargetName with scope $Scope."
				return
			}

			New-AzRoleAssignment -ObjectId $ObjectId -RoleDefinitionName $RoleDefinitionName -Scope $Scope
			Write-Host "Added role assignment $RoleDefinitionName to $TargetName with scope $Scope."
		} `
		-OperationName "Assign $RoleDefinitionName to $TargetName" `
		-MaxAttempts 3 -BaseDelaySeconds 2 `
		-ExistsMessage "Role '$RoleDefinitionName' is already assigned to '$TargetName' with scope $Scope. Skipping." | Out-Null
}

function Set-AksRoleAssignments
{
	[CmdletBinding(SupportsShouldProcess = $true)]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $False)]
		[string] $SubscriptionId,
		[Parameter(Mandatory = $False)]
		[string] $DeploymentPrincipalObjectId
	)

	$ErrorActionPreference = 'Stop'

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')
	. ($scriptsDirectory + '/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')

	$computeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
	$dataResourceGroupName    = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$prereqsResourceGroupName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"

	$context = Get-AzContext
	if (-not $context)
	{
		throw 'Not signed in. Activate the elevated role via PIM, then run Connect-AzAccount.'
	}
	if (-not $SubscriptionId)
	{
		$SubscriptionId = $context.Subscription.Id
	}
	if ($context.Subscription.Id -ne $SubscriptionId)
	{
		Set-AzContext -Subscription $SubscriptionId | Out-Null
	}

	Write-Host "Assigning AKS roles for $SolutionAbbreviation-$EnvironmentAbbreviation as $($context.Account.Id) in $($context.Subscription.Name)."

	$languageMode = $ExecutionContext.SessionState.LanguageMode
	if ($languageMode -ne 'FullLanguage')
	{
		Write-Warning "PowerShell is running in $languageMode. This script avoids method invocation on non-core types; if you see 'Method invocation is supported only on core types in this language mode', a new method call has been introduced - fix that rather than re-running."
	}

	$canAssign = $false
	try
	{
		$probe = Get-AzRoleAssignment -Scope "/subscriptions/$SubscriptionId/resourceGroups/$computeResourceGroupName" -ErrorAction Stop
		$canAssign = $null -ne $probe
	}
	catch
	{
		$canAssign = $false
	}
	if (-not $canAssign)
	{
		throw "Cannot read role assignments in '$computeResourceGroupName'. PIM is almost certainly not active in THIS token: activate Owner or User Access Administrator, run Connect-AzAccount again, then re-run."
	}

	$aksName          = "$computeResourceGroupName-aks"
	$controlPlaneName = "$aksName-identity"
	$kubeletName      = "$aksName-kubelet-identity"
	$workloadPrefix   = "$SolutionAbbreviation-identity-$EnvironmentAbbreviation-aks-"
	$acrName          = "$SolutionAbbreviation`computeacr$EnvironmentAbbreviation"
	$appConfigName    = "$SolutionAbbreviation-appConfig-$EnvironmentAbbreviation"

	$workloadIdentities = Invoke-WithRetry `
		-Operation {
			Get-AzUserAssignedIdentity -ResourceGroupName $computeResourceGroupName |
				Where-Object { $_.Name -like "$workloadPrefix*" } |
				Sort-Object Name
		} `
		-OperationName "Get AKS workload identities in $computeResourceGroupName" `
		-MaxAttempts 3 -BaseDelaySeconds 2

	if (-not $workloadIdentities)
	{
		throw "No workload identities named '$workloadPrefix*' found in '$computeResourceGroupName'. The prerequisite deployment (aksPrereqs.bicep) has not run for this environment - deploy it before running this script."
	}

	$controlPlaneIdentity = Get-AzUserAssignedIdentity -ResourceGroupName $computeResourceGroupName -Name $controlPlaneName
	$kubeletIdentity      = Get-AzUserAssignedIdentity -ResourceGroupName $computeResourceGroupName -Name $kubeletName

	if (-not $DeploymentPrincipalObjectId)
	{
		$serviceConnectionName = "$SolutionAbbreviation-serviceconnection-$EnvironmentAbbreviation"
		$serviceConnectionPrincipal = Invoke-WithRetry `
			-Operation { Get-AzADServicePrincipal -DisplayName $serviceConnectionName -ErrorAction SilentlyContinue } `
			-OperationName "Get service connection principal '$serviceConnectionName'" `
			-MaxAttempts 3 -BaseDelaySeconds 2

		if ($serviceConnectionPrincipal)
		{
			$DeploymentPrincipalObjectId = $serviceConnectionPrincipal.Id
		}
	}

	$storageAccountName = Get-KeyVaultSecretWithFirewallRetry `
		-VaultName $dataResourceGroupName `
		-ResourceGroup $dataResourceGroupName `
		-SecretName 'functionsStorageAccountName' `
		-AsPlainText

	if (-not $storageAccountName)
	{
		throw "Secret 'functionsStorageAccountName' in key vault '$dataResourceGroupName' is missing, empty, or not readable. This account needs Key Vault Secrets User on that vault, and permission to update the vault's network rules if its firewall blocks this machine."
	}

	$storageAccount = Get-AzResource -Name $storageAccountName -ResourceType 'Microsoft.Storage/storageAccounts' -ErrorAction SilentlyContinue
	if (-not $storageAccount)
	{
		throw "Storage account '$storageAccountName' not found in subscription '$SubscriptionId'."
	}

	$subscriptionScope = "/subscriptions/$SubscriptionId"
	$subnetScope       = "$subscriptionScope/resourceGroups/$computeResourceGroupName/providers/Microsoft.Network/virtualNetworks/$aksName-vnet/subnets/aks-nodes"
	$publicIpScope     = "$subscriptionScope/resourceGroups/$computeResourceGroupName/providers/Microsoft.Network/publicIPAddresses/$aksName-outbound-pip"
	$acrScope          = "$subscriptionScope/resourceGroups/$computeResourceGroupName/providers/Microsoft.ContainerRegistry/registries/$acrName"
	$serviceBusScope   = "$subscriptionScope/resourceGroups/$dataResourceGroupName/providers/Microsoft.ServiceBus/namespaces/$dataResourceGroupName"
	$dataKeyVaultScope = "$subscriptionScope/resourceGroups/$dataResourceGroupName/providers/Microsoft.KeyVault/vaults/$dataResourceGroupName"
	$prereqsKvScope    = "$subscriptionScope/resourceGroups/$prereqsResourceGroupName/providers/Microsoft.KeyVault/vaults/$prereqsResourceGroupName"
	$appConfigScope    = "$subscriptionScope/resourceGroups/$dataResourceGroupName/providers/Microsoft.AppConfiguration/configurationStores/$appConfigName"
	$computeRgScope    = "$subscriptionScope/resourceGroups/$computeResourceGroupName"
	$storageScope      = $storageAccount.ResourceId
	$kubeletScope      = $kubeletIdentity.Id

	Write-Host "Granting the control-plane identity its networking and kubelet roles..."
	Set-AksRoleAssignment -ObjectId $controlPlaneIdentity.PrincipalId -RoleDefinitionName 'Network Contributor' -Scope $subnetScope -TargetName $controlPlaneName
	Set-AksRoleAssignment -ObjectId $controlPlaneIdentity.PrincipalId -RoleDefinitionName 'Network Contributor' -Scope $publicIpScope -TargetName $controlPlaneName
	Set-AksRoleAssignment -ObjectId $controlPlaneIdentity.PrincipalId -RoleDefinitionName 'Managed Identity Operator' -Scope $kubeletScope -TargetName $controlPlaneName

	Write-Host "Granting the kubelet identity AcrPull..."
	Set-AksRoleAssignment -ObjectId $kubeletIdentity.PrincipalId -RoleDefinitionName 'AcrPull' -Scope $acrScope -TargetName $kubeletName

	$workloadKeyVaultRoles   = @('Key Vault Secrets User')
	$workloadServiceBusRoles = @('Azure Service Bus Data Sender', 'Azure Service Bus Data Receiver')
	$workloadStorageRoles    = @('Storage Blob Data Contributor', 'Storage Queue Data Contributor', 'Storage Table Data Contributor')

	foreach ($workloadIdentity in $workloadIdentities)
	{
		$workloadName = $workloadIdentity.Name
		Write-Host "Granting workload identity access to its data-plane resources $workloadName..."

		Set-AksRoleAssignment -ObjectId $workloadIdentity.PrincipalId -RoleDefinitionName 'App Configuration Data Reader' -Scope $appConfigScope -TargetName $workloadName

		foreach ($role in $workloadKeyVaultRoles)
		{
			Set-AksRoleAssignment -ObjectId $workloadIdentity.PrincipalId -RoleDefinitionName $role -Scope $prereqsKvScope -TargetName $workloadName
			Set-AksRoleAssignment -ObjectId $workloadIdentity.PrincipalId -RoleDefinitionName $role -Scope $dataKeyVaultScope -TargetName $workloadName
		}

		foreach ($role in $workloadServiceBusRoles)
		{
			Set-AksRoleAssignment -ObjectId $workloadIdentity.PrincipalId -RoleDefinitionName $role -Scope $serviceBusScope -TargetName $workloadName
		}

		foreach ($role in $workloadStorageRoles)
		{
			Set-AksRoleAssignment -ObjectId $workloadIdentity.PrincipalId -RoleDefinitionName $role -Scope $storageScope -TargetName $workloadName
		}
	}

	if ($DeploymentPrincipalObjectId)
	{
		Write-Host "Granting the deployment service principal Kubernetes data-plane access..."
		Set-AksRoleAssignment -ObjectId $DeploymentPrincipalObjectId -RoleDefinitionName 'Azure Kubernetes Service RBAC Cluster Admin' -Scope $computeRgScope -TargetName "$SolutionAbbreviation-serviceconnection-$EnvironmentAbbreviation"
	}
	else
	{
		Write-Warning "Deployment service principal was not found, so its Kubernetes data-plane access was SKIPPED. The pipeline will fail at helm upgrade with 'secrets is forbidden'. Find the object id with Get-AzADServicePrincipal -DisplayName '$SolutionAbbreviation-serviceconnection-$EnvironmentAbbreviation' and re-run with -DeploymentPrincipalObjectId."
	}

	Write-Host "Done attempting to add AKS role assignments."

	Write-Warning "STILL OUTSTANDING - SQL. Each pod authenticates to SyncJobs as its own workload identity via Active Directory Default, so every one of them needs a CONTAINED DATABASE USER created against $dataResourceGroupName as a member of the GMM SQL admin group, by dot-sourcing the deployment script and calling Set-SqlContainedUser:"
	foreach ($workloadIdentity in $workloadIdentities)
	{
		Write-Warning "    $($workloadIdentity.Name)"
	}
}
