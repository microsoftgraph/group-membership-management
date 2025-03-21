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

	$computeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
	$functionApps = Get-AzFunctionApp -ResourceGroupName $computeResourceGroupName | Select-Object -ExpandProperty Name

	foreach ($functionAppName in $functionApps)
	{

		Write-Host "Granting app service access to storage account blobs";

		$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
		if($DataResourceGroupName)
		{
			$resourceGroupName = $DataResourceGroupName
		}

		$appServicePrincipal = Get-AzADServicePrincipal -DisplayName $functionAppName;

		# Grant the app service access to the storage account blobs
		if ($appServicePrincipal)
		{
            $sizeIdentifier = ($functionAppName -split "-")[-1]
            if ($sizeIdentifier -notin @("large", "medium", "small", "onboarding", "s1", "m2", "l1", "o1")) {
                $sizeIdentifier = ""
            }
			$functionAbbreviation = ($functionAppName | Select-String -CaseSensitive -AllMatches -Pattern '[A-Z]').Matches.Value -join ''
            $prefix = $functionAbbreviation + $SolutionAbbreviation + $EnvironmentAbbreviation + "prod" + $sizeIdentifier

            $allFunctionStorageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName |
                Where-Object { $_.StorageAccountName -like "$prefix*" }

            if ($allFunctionStorageAccounts.Count -eq 0) {
                Write-Warning "No storage account found starting with '$prefix'. Skipping..."
                continue
            }

            if ($allFunctionStorageAccounts.Count -gt 1) {
                # Multiple matches found
                if ($sizeIdentifier -eq "") {
                    # No size identifier given, try to filter out any accounts with known size keywords
					$sizeKeywords = @("small","medium","large","onboarding","s1","m2","l1","o1")
					$filteredMatches = $allFunctionStorageAccounts | Where-Object {
						$acctName = $_.StorageAccountName.ToLower()
						$matchesSize = $sizeKeywords | ForEach-Object { $acctName -like "*$_*" }
						-not ($matchesSize -contains $true)
					}

                    if ($filteredMatches.Count -eq 1) {
                        $functionStorageAccount = $filteredMatches[0]
                    } elseif ($filteredMatches.Count -gt 1) {
                        Write-Warning "Multiple non-size storage accounts found. Using $($filteredMatches[0].StorageAccountName)."
                        $functionStorageAccount = $filteredMatches[0]
                    } else {
                        Write-Warning "No non-size storage account found. Using $($allFunctionStorageAccounts[0].StorageAccountName)."
                        $functionStorageAccount = $allFunctionStorageAccounts[0]
                    }
                } else {
                    # If we have a size identifier, just pick the first match
                    Write-Warning "Multiple matches found. Using $($allFunctionStorageAccounts[0].StorageAccountName)."
                    $functionStorageAccount = $allFunctionStorageAccounts[0]
                }
            } else {
                # Exactly one match
                $functionStorageAccount = $allFunctionStorageAccounts[0]
            }

            $functionStorageAccountId = $functionStorageAccount.Id
            $functionStorageAccountRoles = @("Storage Queue Data Contributor","Storage Table Data Contributor","Storage Blob Data Contributor")

            foreach($role in $functionStorageAccountRoles)
            {
                if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $functionStorageAccount.Id -RoleDefinitionName $role)) {
                    $assignment = New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $functionStorageAccount.Id -RoleDefinitionName $role;
                    if ($assignment) {
                        Write-Host "Added role assignment $role to $functionAppName with scope $functionStorageAccountId.";
                    }
                    else {
                        Write-Host "Failed to add role assignment $role to $functionAppName with scope $functionStorageAccountId. Please double check that you have permission to perform this operation";
                    }
                }
                else {
                    Write-Host "$functionAppName already has role $role with scope $functionStorageAccountId.";
                }
            }

			$jobsStorageAccount = Get-AzStorageAccount -ResourceGroupName $resourceGroupName | Where-Object { $_.StorageAccountName -like "jobs$EnvironmentAbbreviation*" }
			$jobsStorageAccountId = $jobsStorageAccount.Id
			$jobsStorageAccountRoles = @("Storage Blob Data Contributor")

			foreach($role in $jobsStorageAccountRoles)
			{
				if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $jobsStorageAccount.Id -RoleDefinitionName $role)) {
					$assignment = New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $jobsStorageAccount.Id -RoleDefinitionName $role;
					if ($assignment) {
						Write-Host "Added role assignment $role to $functionAppName with scope $jobsStorageAccountId.";
					}
					else {
						Write-Host "$functionAppName already has role $role with scope $jobsStorageAccountId.";
					}
				}
				else {
					Write-Host "$functionAppName already has role $role with scope $jobsStorageAccountId.";
				}
			}
		}
		elseif ($null -eq $appServicePrincipal) {
			Write-Host "Function $functionAppName was not found!"

		}
	}

	$webApiRoles = @("Storage Queue Data Contributor","Storage Table Data Contributor")
	$webApi = Get-AzWebApp -ResourceGroupName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation" -Name "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi"
	$webApiSP = $webApi.Identity.PrincipalId
	$dataRG = Get-AzResourceGroup -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$dataRGResourceId = $dataRG.ResourceId

	foreach($role in $webApiRoles)
	{
		if ($null -eq (Get-AzRoleAssignment -ObjectId $webApiSP -Scope $dataRGResourceId -RoleDefinitionName $role)) {
			New-AzRoleAssignment -ObjectId $webApiSP -Scope $dataRGResourceId -RoleDefinitionName $role;
			Write-Host "Added role assignment $role to $($webApi.Name) with scope $dataRGResourceId.";
		}
		else {
			Write-Host "$($webApi.Name) can already $role with scope $dataRGResourceId.";
		}
	}

	$serviceConnectionName = "$SolutionAbbreviation-serviceconnection-$EnvironmentAbbreviation"
	$serviceConnectionPrincipal = Get-AzADServicePrincipal -DisplayName $serviceConnectionName
	if ($serviceConnectionPrincipal) {
		$serviceConnectionRoles = @("Storage Queue Data Contributor","Storage Table Data Contributor","Storage Blob Data Contributor")
		foreach($role in $serviceConnectionRoles)
		{
			if ($null -eq (Get-AzRoleAssignment -ObjectId $serviceConnectionPrincipal.Id -Scope $jobsStorageAccountId -RoleDefinitionName $role)) {
				New-AzRoleAssignment -ObjectId $serviceConnectionPrincipal.Id -Scope $jobsStorageAccountId -RoleDefinitionName $role;
				Write-Host "Added role assignment $role to $($serviceConnectionName) with scope $jobsStorageAccountId.";
			}
			else {
				Write-Host "$($serviceConnectionName) can already $role with scope $jobsStorageAccountId.";
			}
		}
	}
	else {
		Write-Host "Service connection $($serviceConnectionName) was not found!"
	}

	Write-Host "Done attempting to add Storage role assignments.";
}
