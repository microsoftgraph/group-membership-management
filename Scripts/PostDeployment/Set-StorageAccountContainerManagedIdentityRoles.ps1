<#
.SYNOPSIS
Adds the app service's managed service identity as a storage blob data contributor on the specified storage account.

.DESCRIPTION
Adds the app service's managed service identity as a storage blob data contributor on the specified storage account so we don't need connection strings as much.
This should be run by an owner on the subscription after the storage account and app service have been set up.
This should only have to be run once per function app.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-StorageAccountContainerManagedIdentityRoles	-SolutionAbbreviation "gmm" `
												-EnvironmentAbbreviation "<env>" `
												-Verbose
#>

function Set-StorageAccountContainerManagedIdentityRoles
{
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $False)]
		[string] $DataResourceGroupName = $null,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	$functionApps = @("GroupMembershipObtainer","SqlMembershipObtainer","PlaceMembershipObtainer","MembershipAggregator","GraphUpdater","TeamsChannelMembershipObtainer","GroupOwnershipObtainer","TeamsChannelUpdater", "DestinationAttributesUpdater")

	foreach ($functionApp in $functionApps)
	{

		Write-Host "Granting app service access to storage account blobs";


		$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
		if($DataResourceGroupName)
		{
			$resourceGroupName = $DataResourceGroupName
		}

		$ProductionFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp"
		$StagingFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp/slots/staging"

		$functionAppBasedOnSlots = @($ProductionFunctionAppName,$StagingFunctionAppName)

		foreach ($fa in $functionAppBasedOnSlots)
		{
			$appServicePrincipal = Get-AzADServicePrincipal -DisplayName $fa;

			# Grant the app service access to the storage account blobs
			if ($appServicePrincipal)
			{
				$resources = Get-AzResource -ResourceGroupName $resourceGroupName

				$filteredStorageAccountsList = $resources | Where-Object {
					$_.ResourceType -eq "Microsoft.Storage/storageAccounts" -and $_.Name -like "jobs$EnvironmentAbbreviation*"
				}

				$storageAccountObject = $filteredStorageAccountsList[0]
				$storageAccountName = $storageAccountObject.Name

				if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $storageAccountObject.Id))
				{
					$assignment = New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $storageAccountObject.Id -RoleDefinitionName "Storage Blob Data Contributor";
					if ($assignment) {
						Write-Host "Added role assignment to allow $fa to access on the $storageAccountName blobs.";
					}
					else {
						Write-Host "Failed to add role assignment to allow $fa to access on the $storageAccountName blobs. Please double check that you have permission to perform this operation";
					}
				}
				else
				{
					Write-Host "$fa already has access to $storageAccountName blobs.";
				}
			}
			elseif ($null -eq $appServicePrincipal) {
				Write-Host "Function $fa was not found!"
			}
		}
	}

	Write-Host "Done attempting to add Storage Blob Data Contributor role assignments.";
}