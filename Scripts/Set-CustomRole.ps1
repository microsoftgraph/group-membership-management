<#
.SYNOPSIS
Script to create a custom role and assign it to a service principal in a resource group.
The role has permissions to manage locks on the resource group.

.PARAMETER SolutionAbbreviation
The Solution Abbreviation of the environment.

.PARAMETER EnvironmentAbbreviation
The Environment Abbreviation of the environment.

.EXAMPLE
Set-CustomRole -SolutionAbbreviation "<SolutionAbbreviation>"  `
               -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
               -Verbose
#>


function Assign-CustomRole {
    param (
        [Parameter(Mandatory=$true)]
        [string]$ResourceGroupName,
        [Parameter(Mandatory=$true)]
        [string]$ServicePrincipalName,
        [Parameter(Mandatory=$true)]
        [string]$RoleName
    )

    Write-Host "Assigning role '$RoleName' to service principal '$ServicePrincipalName' in resource group '$ResourceGroupName'."

    # Get the resource group
    $resourceGroup = Get-AzResourceGroup -Name $ResourceGroupName

    # Get the service principal
    $servicePrincipal = Get-AzADServicePrincipal -DisplayName $ServicePrincipalName

    # Get the custom role
    $role = Get-AzRoleDefinition -Name $RoleName

    # Check if the role assignment already exists
    $existingAssignment = Get-AzRoleAssignment `
        -ObjectId $servicePrincipal.Id `
        -RoleDefinitionName $role.Name `
        -Scope $resourceGroup.ResourceId `
        -ErrorAction SilentlyContinue

    if ($existingAssignment) {
        Write-Output "The role assignment already exists."
    } else {
        # Assign the role to the service principal
        New-AzRoleAssignment -ObjectId $servicePrincipal.Id -RoleDefinitionName $role.Name -Scope $resourceGroup.ResourceId
        Write-Output "The role assignment has been created."
    }
}


function Set-CustomRole {
    param (
        [Parameter(Mandatory=$true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory=$true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory=$false)]
        [string]$ResourceGroupClassification = "data"
    )

    # Get the resource group
    $resourceGroupName = "$SolutionAbbreviation-$ResourceGroupClassification-$EnvironmentAbbreviation"
    $resourceGroup = Get-AzResourceGroup -Name $resourceGroupName

    # Define the role
    $roleName = "GMM Custom Role"
    $roleDefinition = @{
        Name = $roleName
        Description = "Custom role with Microsoft.Authorization/locks permissions"
        Actions = @("Microsoft.Authorization/locks/*")
        NotActions = @()
        AssignableScopes = @($resourceGroup.ResourceId)
    }

    # Get the custom role
    $role = Get-AzRoleDefinition -Name $roleName -ErrorAction SilentlyContinue

    if ($role) {
        Write-Output "The role '$roleName' already exists."
    } else {
        # Create the role
        Write-Output "Creating role '$roleName'."
        New-AzRoleDefinition -Role $roleDefinition
        Start-Sleep -Seconds 10
    }

    # Assign the role to the service principal
    Assign-CustomRole `
        -ResourceGroupName $resourceGroupName `
        -ServicePrincipalName "$SolutionAbbreviation-serviceconnection-$EnvironmentAbbreviation" `
        -RoleName $roleName
}
