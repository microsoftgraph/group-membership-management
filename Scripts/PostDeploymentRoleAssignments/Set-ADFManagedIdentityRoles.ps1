<#
.SYNOPSIS
Adds the app service's managed service identity as a data factory contributor on the specified Azure Data Factory.

.DESCRIPTION
Adds the app service's managed service identity as a data factory contributor on the specified Azure Data Factory.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER EnableMultiRegion
Include the secondary regional ADF and its storage account when enabled.

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
		[array] $UserPrincipalNames,
        [Parameter(Mandatory = $False)]
        [bool] $EnableMultiRegion = $false
	)

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

	$functionApps = @("SqlMembershipObtainer", "SqlDataChecker", "AutoApprover")
    $appServices = @("webapi")
    $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $azureDataFactories = @(
        @{
            Name = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
            ResourceGroup = $dataResourceGroupName
        }
    )
    if ($EnableMultiRegion) {
        $azureDataFactories += @{
            Name = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf-sec"
            ResourceGroup = "$dataResourceGroupName-sec"
        }
    }
    $servicePrincipals = @()
    $azureDataFactoryObjects = @()
    foreach ($factory in $azureDataFactories) {
        $factoryObject = Invoke-WithRetry `
            -Operation { Get-AzResource -ResourceGroupName $factory.ResourceGroup -Name $factory.Name -ResourceType "Microsoft.DataFactory/factories" -ErrorAction SilentlyContinue } `
            -OperationName "Get ADF resource '$($factory.Name)'" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        if ($null -ne $factoryObject) {
            $azureDataFactoryObjects += $factoryObject
        }
    }

    if ($azureDataFactoryObjects.Count -eq 0) {
        Write-Host "No regional ADF resources were found."
        return
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

    foreach ($azureDataFactoryObject in $azureDataFactoryObjects) {
        $factoryServicePrincipals = $servicePrincipals
        if ($azureDataFactoryObject.Name -eq "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf-sec") {
            # The public HR pipeline no longer calls SqlDataChecker; do not extend its legacy factory role to the standby.
            $factoryServicePrincipals = @($servicePrincipals | Where-Object {
                $_.DisplayName -ne "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-SqlDataChecker"
            })
        }

        foreach ($servicePrincipal in $factoryServicePrincipals)
        {
            $servicePrincipalName = $servicePrincipal.DisplayName

            Invoke-WithCreateRetry `
                -GetExistingOperation { Get-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $azureDataFactoryObject.Id } `
                -CreateOperation {
                    New-AzRoleAssignment -ObjectId $servicePrincipal.Id -Scope $azureDataFactoryObject.Id -RoleDefinitionName "Data Factory Contributor"
                    Write-Host "Added role assignment to allow $servicePrincipalName to access the $($azureDataFactoryObject.Name) ADF resource."
                } `
                -OperationName "Assign Data Factory Contributor to $servicePrincipalName" `
                -MaxAttempts 3 -BaseDelaySeconds 2 `
                -ExistsMessage "Data Factory Contributor role is already assigned to '$servicePrincipalName'. Skipping."
        }
    }

    Write-Host "Grant ADF identity access to the storage account";
    # Define the Key Vault name and the secret name
    $azureUserReaderPrincipal = Invoke-WithRetry `
        -Operation { Get-AzADServicePrincipal -DisplayName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-AzureUserReader" } `
        -OperationName "Get AzureUserReader principal" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $primaryAdfName = $azureDataFactories[0].Name
    $primaryAdfPrincipal = Invoke-WithRetry `
        -Operation { Get-AzADServicePrincipal -DisplayName $primaryAdfName } `
        -OperationName "Get ADF principal '$primaryAdfName'" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $secondaryAdfPrincipal = $null
    if ($EnableMultiRegion) {
        $secondaryAdfName = $azureDataFactories[1].Name
        $secondaryAdfPrincipal = Invoke-WithRetry `
            -Operation { Get-AzADServicePrincipal -DisplayName $secondaryAdfName } `
            -OperationName "Get ADF principal '$secondaryAdfName'" `
            -MaxAttempts 3 -BaseDelaySeconds 2
    }
    $sharedStoragePrincipals = @($azureUserReaderPrincipal) | Where-Object { $null -ne $_ }
    $dataRGName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $dataKeyVaultName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $secretNames = @("adfStorageAccountName", "sqlMembershipStorageAccountName")
    if ($EnableMultiRegion) {
        $secretNames += "adfStorageAccountNameSec"
    }

    foreach($secret in $secretNames)
    {
        # Each regional factory is granted only its own ADF storage account; shared identities keep access to all.
        $servicePrincipalsToBeGrantedStorageRoles = @(
            switch ($secret) {
                "adfStorageAccountName"    { $primaryAdfPrincipal }
                "adfStorageAccountNameSec" { $secondaryAdfPrincipal }
                default                    { $primaryAdfPrincipal; $secondaryAdfPrincipal }
            }
        ) + $sharedStoragePrincipals | Where-Object { $null -ne $_ }

        $storageAccountName = Get-KeyVaultSecretWithFirewallRetry -VaultName $dataKeyVaultName -ResourceGroup $dataRGName -SecretName $secret -AsPlainText

        if ($null -eq $storageAccountName) {
            continue;
        }

        $storageResourceGroupName = if ($secret -eq "adfStorageAccountNameSec") {
            "$dataRGName-sec"
        }
        else {
            $dataRGName
        }

        $adfStorageAccount = Invoke-WithRetry `
            -Operation { Get-AzStorageAccount -ResourceGroupName $storageResourceGroupName -Name $storageAccountName } `
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