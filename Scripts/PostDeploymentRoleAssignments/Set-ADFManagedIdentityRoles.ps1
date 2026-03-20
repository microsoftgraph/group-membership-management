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
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

	$functionApps = @("SqlMembershipObtainer")
    $appServices = @("webapi")
    $azureDataFactoryName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
    $servicePrincipals = @()
    $azureDataFactoryObject = Invoke-WithRetry `
        -Operation { Get-AzResource -Name $azureDataFactoryName -ResourceType "Microsoft.DataFactory/factories" } `
        -OperationName "Get ADF resource '$azureDataFactoryName'" `
        -MaxAttempts 3 -BaseDelaySeconds 2

    if ($null -eq $azureDataFactoryObject)
    {
        Write-Host "The $azureDataFactoryName ADF resource was not found!";
        return;
    }

    foreach ($name in $UserPrincipalNames)
    {
        $userPrincipal = Invoke-WithRetry `
            -Operation { Get-AzADUser -UserPrincipalName $name } `
            -OperationName "Get AD user '$name'" `
            -MaxAttempts 3 -BaseDelaySeconds 2

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

        $servicePrincipal = Invoke-WithRetry `
            -Operation { Get-AzADServicePrincipal -DisplayName $functionAppName } `
            -OperationName "Get service principal '$functionAppName'" `
            -MaxAttempts 3 -BaseDelaySeconds 2

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
		$servicePrincipal = Invoke-WithRetry `
		    -Operation { Get-AzADServicePrincipal -DisplayName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$appService" } `
		    -OperationName "Get service principal '$appService'" `
		    -MaxAttempts 3 -BaseDelaySeconds 2

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

        Invoke-WithCreateRetry `
            -GetExistingOperation { Get-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $azureDataFactoryObject.Id } `
            -CreateOperation {
                New-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $azureDataFactoryObject.Id -RoleDefinitionName "Data Factory Contributor"
                Write-Host "Added role assignment to allow $servicePrincipalName to access the $azureDataFactoryName ADF resource."
            } `
            -OperationName "Assign Data Factory Contributor to $servicePrincipalName" `
            -MaxAttempts 3 -BaseDelaySeconds 2 `
            -ExistsMessage "Data Factory Contributor role is already assigned to '$servicePrincipalName'. Skipping."
    }

    Write-Host "Grant ADF identity access to the storage account";
    # Define the Key Vault name and the secret name
    $azureUserReaderPrincipal = Invoke-WithRetry `
        -Operation { Get-AzADServicePrincipal -DisplayName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-AzureUserReader" } `
        -OperationName "Get AzureUserReader principal" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $dataFactoryPrincipal = Invoke-WithRetry `
        -Operation { Get-AzADServicePrincipal -DisplayName $azureDataFactoryName } `
        -OperationName "Get ADF principal" `
        -MaxAttempts 3 -BaseDelaySeconds 2
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

        $adfStorageAccount = Invoke-WithRetry `
            -Operation { Get-AzStorageAccount -ResourceGroupName $dataRGName -Name $storageAccountName } `
            -OperationName "Get storage account '$storageAccountName'" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        $storageAccountRoles = @("Storage Queue Data Contributor","Storage Table Data Contributor","Storage Blob Data Contributor")

        foreach ($servicePrincipal in $servicePrincipalsToBeGrantedStorageRoles)
        {
            $servicePrincipalName = $servicePrincipal.DisplayName

            foreach($role in $storageAccountRoles)
            {
                Invoke-WithCreateRetry `
                    -GetExistingOperation { Get-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $adfStorageAccount.Id -RoleDefinitionName $role } `
                    -CreateOperation {
                        New-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $adfStorageAccount.Id -RoleDefinitionName $role
                        Write-Host "Added role assignment $role to $servicePrincipalName with scope $storageAccountName."
                    } `
                    -OperationName "Assign $role to $servicePrincipalName" `
                    -MaxAttempts 3 -BaseDelaySeconds 2 `
                    -ExistsMessage "Role '$role' is already assigned to '$servicePrincipalName' on storage account '$storageAccountName'. Skipping."
            }
        }
    }



	Write-Host "Done attempting to add Data Factory Contributor role assignments.";
}