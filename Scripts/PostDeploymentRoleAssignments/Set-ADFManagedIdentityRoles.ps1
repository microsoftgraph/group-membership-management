<#
.SYNOPSIS
Adds the app service's managed service identity as a data factory contributor on the specified Azure Data Factory.

.DESCRIPTION
Adds the app service's managed service identity as a data factory contributor on the specified Azure Data Factory.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-ADFManagedIdentityRoles	    -SolutionAbbreviation "gmm" `
								-EnvironmentAbbreviation "<env>"
#>

function Set-ADFManagedIdentityRoles
{
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $False)]
		[array] $UserPrincipalNames
	)

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')

	$functionApps = @("SqlMembershipObtainer")
    $appServices = @("webapi")
    $azureDataFactoryName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
    $servicePrincipals = @()
    $azureDataFactoryObject = Get-AzResource -Name $azureDataFactoryName -ResourceType "Microsoft.DataFactory/factories"

    if ($null -eq $azureDataFactoryObject)
    {
        Write-Host "The $azureDataFactoryName ADF resource was not found!";
        return;
    }

    foreach ($name in $UserPrincipalNames)
    {
        $userPrincipal = Get-AzADUser -UserPrincipalName $name

		if ($userPrincipal)
		{
			$servicePrincipals += $userPrincipal
		}
		elseif ($null -eq $userPrincipal) {
			Write-Host "User $name was not found!"
		}
    }

    foreach ($functionApp in $functionApps)
	{
    	$functionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp"

        $servicePrincipal = Get-AzADServicePrincipal -DisplayName $functionAppName;

        if ($servicePrincipal)
        {
            $servicePrincipals += $servicePrincipal
        }
        elseif ($null -eq $servicePrincipal) {
            Write-Host "Function $functionAppName was not found!"
        }

    }

    foreach ($appService in $appServices)
	{
		$servicePrincipal = Get-AzADServicePrincipal -DisplayName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$appService"

        if ($servicePrincipal)
        {
            $servicePrincipals += $servicePrincipal
        }
        elseif ($null -eq $servicePrincipal) {
            Write-Host "App Service $appService was not found!"
        }
    }

    foreach ($servicePrincipal in $servicePrincipals)
    {
        $servicePrincipalName = $servicePrincipal.DisplayName

        if ($null -eq (Get-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $azureDataFactoryObject.Id))
        {
            $assignment = New-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $azureDataFactoryObject.Id -RoleDefinitionName "Data Factory Contributor";
            if ($assignment) {
                Write-Host "Added role assignment to allow $servicePrincipalName to access the $azureDataFactoryName ADF resource.";
            }
            else {
                Write-Host "Failed to add role assignment to allow $servicePrincipalName to access the $azureDataFactoryName ADF resource. Please double check that you have permission to perform this operation";
            }
        }
        else
        {
            Write-Host "$servicePrincipalName already has access to the $azureDataFactoryName ADF resource.";
        }
    }

    Write-Host "Grant ADF identity access to the storage account";
    # Define the Key Vault name and the secret name
    $azureUserReaderPrincipal = Get-AzADServicePrincipal -DisplayName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-AzureUserReader";
    $dataFactoryPrincipal = Get-AzADServicePrincipal -DisplayName $azureDataFactoryName;
    $servicePrincipalsToBeGrantedStorageRoles = @($dataFactoryPrincipal, $azureUserReaderPrincipal)
    $dataRGName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $dataKeyVaultName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $secretNames = @("adfStorageAccountName", "sqlMembershipStorageAccountName")

    foreach($secret in $secretNames)
    {
        $storageAccountName = Get-KeyVaultSecretWithFirewallRetry -VaultName $dataKeyVaultName -ResourceGroup $dataRGName -SecretName $secret -AsPlainText

        if ($null -eq $storageAccountName) {
            continue;
        }

        $adfStorageAccount = Get-AzStorageAccount -ResourceGroupName $dataRGName -Name $storageAccountName
        $storageAccountRoles = @("Storage Queue Data Contributor","Storage Table Data Contributor","Storage Blob Data Contributor")

        foreach ($servicePrincipal in $servicePrincipalsToBeGrantedStorageRoles)
        {
            $servicePrincipalName = $servicePrincipal.DisplayName

            foreach($role in $storageAccountRoles)
            {
                if ($null -eq (Get-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $adfStorageAccount.Id -RoleDefinitionName $role)) {
                    $assignment = New-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $adfStorageAccount.Id -RoleDefinitionName $role;
                    if ($assignment) {
                        Write-Host "Added role assignment $role to $servicePrincipalName with scope $storageAccountName.";
                    }
                    else {
                        Write-Host "Failed to add role assignment $role to $servicePrincipalName with scope $storageAccountName. Please double check that you have permission to perform this operation";
                    }
                }
                else {
                    Write-Host "$servicePrincipalName already has role $role with scope $storageAccountName.";
                }
            }
        }
    }



	Write-Host "Done attempting to add Data Factory Contributor role assignments.";
}