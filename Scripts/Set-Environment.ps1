$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Script to set up the general environment (will be run on SAW)

.PARAMETER solutionAbbreviation
Abbreviation used to denote the overall solution (or application). Length 1-3.

.PARAMETER environmentAbbreviation
Abbreviation for the environment. Length 1-3.

.PARAMETER objectId
Azure Object Id of the user, group or service principal to which access to the prereqs keyvault is going to be granted.

.PARAMETER resourceGroupLocation
Azure location where the resource groups and its resources are going to be created.

.PARAMETER overwrite
If set to $true, it will delete the resource groups if they already exist.

.EXAMPLE
Set-Environment -solutionAbbreviation "<solutionAbbreviation>" `                
                -environmentAbbreviation "<environmentAbbreviation>" `
                -objectId "<objectId>" `
                -resourceGroupLocation "<resourceGroupLocation>" `
                -overwrite $true
   
#>
 
function Set-Subscription {
    
    if (-not $SubscriptionId) {
		Write-Host "`nCurrent subscription:`n"
		$currentSubscription = (Get-AzContext).Subscription	
		Write-Host "$($currentSubscription.Name) -  $($currentSubscription.Id)"

		Write-Host "`nAvailable subscriptions:"
		Write-Host (Get-AzSubscription | Select-Object -Property Name, Id)
		Write-Host "`n"
		$SubscriptionId = Read-Host -Prompt "If you would like to use other subscription than '$($currentSubscription.Name)' `nprovide the subscription id, otherwise press enter to continue."
	}

    if ($SubscriptionId)
    {        
        Set-AzContext -SubscriptionId $SubscriptionId
        $currentSubscription = (Get-AzContext).Subscription
        Write-Host "Selected subscription: $($currentSubscription.Name) -  $($currentSubscription.Id)"
    }

    return $SubscriptionId;
}

function Set-Environment {
    Param
    (
        [Parameter (
            Mandatory=$true,
            HelpMessage="The abbreviation for your solution."
        )]
        [string]
        $solutionAbbreviation,
        [Parameter (
            Mandatory=$true,
            HelpMessage="The abbreviation for your environment."
        )]
        [string]
        $environmentAbbreviation,
        [Parameter (
            Mandatory=$true,
            HelpMessage="The Azure object id to which access to the prereqs keyvault will be granted."
        )]
        [string]
        $objectId,
        [Parameter (
            Mandatory=$false,
            HelpMessage="The Azure location where the resource groups are going to be created."
        )]
        [string]
        $resourceGroupLocation,
        [Parameter (
            Mandatory=$true,
            HelpMessage="By default, this script will not overwrite an existing environment.  If you really want to overwrite your environment, set this value to True."
        )]
        [bool] $overwrite = $false
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
    Add-AzAccountIfNeeded | Out-Null
    
    Set-Subscription

    foreach ($namespace in @("Microsoft.ServiceBus", "Microsoft.Insights", "Microsoft.OperationalInsights", "Microsoft.AlertsManagement", "Microsoft.Storage"))
    {
        Write-Host "Checking if the resource provider $namespace is registered..."
        $provider = Get-AzResourceProvider -ProviderNamespace $namespace

        if ($provider.Where({$_.RegistrationState -ne "Registered"}).Count -gt 0)
        {
            Write-Host "$namespace is not registered. Registering..."
            Register-AzResourceProvider -ProviderNamespace $namespace
        }

        Write-Host "$namespace is registered."
    }
    
    #region Generate Resource Group Names
    $resourceGroupTypes = "compute", "data", "prereqs"
    $resourceGroupNames = @()
    foreach ($resourceGroupType in $resourceGroupTypes)
    {
        $resourceGroupNames += "$solutionAbbreviation-$resourceGroupType-$environmentAbbreviation"
    }
    #endregion

    #region Create resource groups
    foreach ($resourceGroupName in $resourceGroupNames)
    {
        #region Create Resource Group if not exists
        $resourceGroup = Get-AzResourceGroup `
            -Name $resourceGroupName `
            -ErrorAction SilentlyContinue `
            -ErrorVariable ev 
        if(-not $ev -and $resourceGroup)
        {
            if($overwrite -eq $false)
            {
                throw "This environment already exists.  If you want to overwrite it, set the `$overwrite parameter to true."
            }
            if($overwrite -eq $true)
            {
                Write-Host "Removing the existing resource group $resourceGroupName..."
                Remove-AzResourceGroup `
                    -Name $resourceGroup.ResourceGroupName `
                    -Force
                Write-Host "Removed the existing resource group $resourceGroupName."
            }
        }

        Write-Host "Creating resource group $resourceGroupName..."
        $resourceGroup = New-AzResourceGroup -Name $resourceGroupName `
            -Location $resourceGroupLocation `
            -ErrorAction Stop
        Write-Host "Created resource group $resourceGroupName."
        #endregion
    }
    #endregion


    #region Create PreReqs KeyVault
    $resourceGroupName = "$solutionAbbreviation-prereqs-$environmentAbbreviation"
    Write-Host "Creating prereqs keyvault for $resourceGroupName..."
    $prereqsKeyVault = New-AzKeyVault   -ResourceGroupName $resourceGroupName `
                                        -Location $resourceGroupLocation `
                                        -EnabledForDeployment `
                                        -EnabledForTemplateDeployment `
                                        -Name $resourceGroupName
                                        
    Write-Host "Prereqs keyvault $($prereqsKeyVault.VaultName) created."
    #endregion


    #region Set PreReqs KeyVault Access Policies
    Write-Host "Setting keyvault $($prereqsKeyVault.VaultName) access policies..."
    
    #Start-Sleep -Seconds 5
    Set-AzKeyVaultAccessPolicy  -VaultName $prereqsKeyVault.VaultName `
                                -ObjectId $objectId `
                                -PermissionsToSecrets list,get,set,delete `
                                -PermissionsToCertificates  list,get,create,update,delete
    
    Write-Host "Prereqs keyvault $($prereqsKeyVault.VaultName) access policies set."
    #endregion
    
    Write-Host "Set-Environment complete."
}