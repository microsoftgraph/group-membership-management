<#
.SYNOPSIS
Adds the Key Vault Secrets User role to all functions on data and prereqs keyvaults.

.DESCRIPTION
Adds the Key Vault Secrets User role to all functions on data and prereqs keyvaults.
This should be run by an owner on the subscription after the Log Analytics resource and app service have been set up.
This should only have to be run once per function app.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-KeyVaultSecretsUserRoles	-SolutionAbbreviation "gmm" `
								-EnvironmentAbbreviation "<env>" `
								-Verbose
#>

function Set-KeyVaultSecretsUserRoles
{
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	$functionApps = @("GraphUpdater","MembershipAggregator","SecurityGroup","AzureMaintenance","AzureUserReader","JobScheduler","JobTrigger","NonProdService")
	$resourceGroups = @("$SolutionAbbreviation-data-$EnvironmentAbbreviation", "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation")

	foreach ($functionApp in $functionApps)
	{
		Write-Host "Granting app service access to data and prereqs key vaults";

		$ProductionFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp"
		$StagingFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp/slots/staging"

		$functionAppBasedOnSlots = @($ProductionFunctionAppName,$StagingFunctionAppName)

		foreach ($fa in $functionAppBasedOnSlots)
		{

			Write-Host "FunctionAppName: $fa"

			$appServicePrincipal = Get-AzADServicePrincipal -DisplayName $fa;

			# Grant the app service access to the data and prereqs key vaults
			if ($appServicePrincipal)
			{
				foreach($resourceGroupName in $resourceGroups) {
					$keyVaultName = $resourceGroupName
					$keyVault = Get-AzKeyVault -ResourceGroupName $resourceGroupName -VaultName $keyVaultName

					if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $keyVault.ResourceId))
					{
						$assignment = New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $keyVault.ResourceId -RoleDefinitionName "Key Vault Secrets User";
						if ($assignment) {
							Write-Host "Added role assignment to allow $fa to read secrets in the $keyVaultName key vault.";
						}
						else {
							Write-Host "Failed to add role assignment to allow $fa to read secrets in the $keyVaultName key vault. Please double check that you have permission to perform this operation";
						}
					}
					else
					{
						Write-Host "$fa can already read secrets in the $keyVaultName key vault.";
					}

				}
			} elseif ($null -eq $appServicePrincipal) {
				Write-Host "Function $fa was not found!"
			}
		}
	}

	Write-Host "Done attempting to add Key Vault Secrets User role assignments.";
}