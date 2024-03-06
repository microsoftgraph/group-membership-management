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
		[System.Collections.ArrayList] $UserPrincipalNames
	)

	$functionApps = @("SqlMembershipObtainer")
    $appServices = @("webapi")
    $azureDataFactoryName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
    $servicePrincipals = New-Object System.Collections.ArrayList
    $azureDataFactoryObject = Get-AzResource -Name $azureDataFactoryName

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
			$servicePrincipals.Add($userPrincipal)
		}
		elseif ($null -eq $userPrincipal) {
			Write-Host "User $name was not found!"
		}
    }

    foreach ($functionApp in $functionApps)
	{   
    	$ProductionFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp"
		$StagingFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp/slots/staging"
		$functionAppBasedOnSlots = @($ProductionFunctionAppName,$StagingFunctionAppName)

        foreach ($fa in $functionAppBasedOnSlots)
		{
			$servicePrincipal = Get-AzADServicePrincipal -DisplayName $fa;

            if ($servicePrincipal)
			{
                $servicePrincipals.Add($servicePrincipal)
            }
            elseif ($null -eq $servicePrincipal) {
                Write-Host "Function $fa was not found!"
            }
        }
    }

    foreach ($appService in $appServices)
	{   
		$servicePrincipal = Get-AzADServicePrincipal -DisplayName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$appService"
        
        if ($servicePrincipal)
        {
            $servicePrincipals.Add($servicePrincipal)
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
	
	Write-Host "Done attempting to add Data Factory Contributor role assignments.";
}