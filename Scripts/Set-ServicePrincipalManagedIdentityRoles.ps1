$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
This script creates a service principal

.PARAMETER ServicePrincipalName
Name for the new service principal

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (or application)

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.EXAMPLE
Set-ServicePrincipalManagedIdentityRoles -SolutionAbbreviation "<solutionAbbreviation>" `
                                        -EnvironmentAbbreviation "<environmentAbbreviation>"
#>

function Set-Role {
    Param(
        [Parameter (Mandatory=$true)]
        [string]
        $RoleDefinitionName,
        [Parameter (Mandatory=$true)]
        [string]
        $ResourceGroupName,
        [Parameter (Mandatory=$true)]
        [string]
        $ObjectId
    )
    if($objectId -ne $null)
    {
        $roleAssignment = (Get-AzRoleAssignment `
            -ObjectId $ObjectId `
            -ResourceGroupName $ResourceGroupName `
            -RoleDefinitionName $RoleDefinitionName)

        if($roleAssignment)
        {
            Write-Host "$ResourceGroupName's $RoleDefinitionName Role Assignment already exists."
        }
        else
        {
            Write-Host "Assigning $RoleDefinitionName access to ($ResourceGroupName)..."
            New-AzRoleAssignment `
                -ObjectId $ObjectId `
                -ResourceGroupName $ResourceGroupName `
                -RoleDefinitionName $RoleDefinitionName
            Write-Host "$RoleDefinitionName access assigned to ($ResourceGroupName)."
        }
    }
}

function Set-ServicePrincipalManagedIdentityRoles {
    [CmdletBinding()]
	param(
        [Parameter(Mandatory=$True)]
        [string] $solutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $environmentAbbreviation
    )
    #region Get service principal
    $servicePrincipalName = "$solutionAbbreviation-serviceconnection-$environmentAbbreviation";
    $servicePrincipal = Get-AzADServicePrincipal -DisplayName $servicePrincipalName

    if($null -eq $servicePrincipal) {
        Write-Host "The service principal is null. Please have the owner create a service principal with name $($servicePrincipalName) then try again."
        return
    }
    #endregion

    Write-Host "Assigning service principal to resource groups..."
    $resourceGroupTypes = "compute", "data", "prereqs"
    foreach ($resourceGroupType in $resourceGroupTypes)
    {
        $resourceGroupName = "$solutionAbbreviation-$resourceGroupType-$environmentAbbreviation"
        Set-Role -RoleDefinitionName "Contributor" -ResourceGroupName $resourceGroupName -ObjectId $servicePrincipal.Id
    }
}