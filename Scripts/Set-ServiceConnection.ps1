$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create Azure DevOps Service Connection

.DESCRIPTION
Long description

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (or application)

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.PARAMETER OrganizationName
Azure DevOps organization name

.PARAMETER ProjectName
Azure DevOps project name

.EXAMPLE
Set-ServiceConnection	-SolutionAbbreviation "<solution>"  `
						-EnvironmentAbbreviation "<environment>" `
						-OrganizationName "<organizationName>" `
						-ProjectName "<projectName>" `
						-Clean $true `
						-Verbose
#>
function Set-ServiceConnection {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
        [string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $OrganizationName,
		[Parameter(Mandatory=$True)]
		[string] $ProjectName,
		[Parameter(Mandatory=$True)]
        [boolean] $Clean,
		[Parameter(Mandatory=$False)]
		[securestring] $SecurePersonalAccessToken,		
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	"Set-ServiceConnection starting..."

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
	Add-AzAccountIfNeeded | Out-Null

	. ($scriptsDirectory + '\Scripts\Install-VSTeamModuleIfNeeded.ps1')
	Install-VSTeamModuleIfNeeded | Out-Null
		    	
	$servicePrincipalName = "$SolutionAbbreviation-serviceconnection-$EnvironmentAbbreviation"

	Write-Host "`nCurrent subscription:`n"
	$currentSubscription = (Get-AzContext).Subscription	
	Write-Host "$($currentSubscription.Name) -  $($currentSubscription.Id)"

	Write-Host "`nAvailable subscriptions:"
	Write-Host (Get-AzSubscription | Select-Object -Property Name, Id)
	Write-Host "`n"
	$SubscriptionId = Read-Host -Prompt "If you would like to use other subscription than '$($currentSubscription.Name)' `nprovide the subscription id, otherwise press enter to continue."

    if ($SubscriptionId)
    {        
        Set-AzContext -SubscriptionId $SubscriptionId
        $currentSubscription = (Get-AzContext).Subscription
        Write-Host "Selected subscription: $($currentSubscription.Name) -  $($currentSubscription.Id)"
    }
	
	if($null -eq $SecurePersonalAccessToken)
	{

		Write-Host "`n`Azure DevOps' Create a personal access token' page should open a new Edge browser window:"
		Write-Host "Click on 'New Token' and fill in the form."
		Write-Host "Name: $servicePrincipalName"
		Write-Host "Organizations: $OrganizationName"
		Write-Host "Expiration: 90 days (or set a custom value)"
		Write-Host "Authorized Scopes: Custom Defined"		
		Write-Host "Click on 'Show all scopes' link at the bottom of the form"
		Write-Host "Locate and select 'Service Connections (read, query and manage)' scope"
		Write-Host "Create and copy your personal access token"

		Start-Process "msedge.exe" "https://dev.azure.com/$OrganizationName/_usersSettings/tokens"
		
		$SecurePersonalAccessToken = Read-Host "`n`nWhat is your Azure DevOps Personal Access Token?" -AsSecureString
	}

	Set-VSTeamAccount	-Account $OrganizationName `
						-SecurePersonalAccessToken $SecurePersonalAccessToken `
						| Out-Null
	
	$vsTeamServiceEndpoints = Get-VSTeamServiceEndpoint -ProjectName $ProjectName
	$vsTeamServiceEndpoint = $vsTeamServiceEndpoints | Where-Object { $_.Name -eq "$($servicePrincipalName)"}
	$vsTeamServiceEndpointExists = ($vsTeamServiceEndpoint | Measure-Object | Select-Object -Expand Count) -gt 0

	if($Clean -and $vsTeamServiceEndpointExists)
	{
		Write-Host  "`nRemove old Azure DevOps service endpoints"
		$vsTeamServiceEndpoint | ForEach-Object {
			Remove-VSTeamServiceEndpoint 	-ProjectName $ProjectName `
											-id $_.Id `
											-Force `
											| Out-Null
		 }
	}
		
	$subscriptionTenantId = $currentSubscription.TenantId
	$SubscriptionId = $currentSubscription.Id

	#region Create service connection
	$serviceConnectionServicePrincipal = Get-AzADServicePrincipal -DisplayName $servicePrincipalName
    $serviceConnectionCredential = Get-AzADApplication -ApplicationId $serviceConnectionServicePrincipal.AppId | New-AzADAppCredential -StartDate (Get-Date).AddHours(-1) -EndDate (Get-Date).AddYears(1)
    Write-Host "`nThe Azure DevOps RM App ServicePrincipal ObjectID is: $($serviceConnectionServicePrincipal.Id)"

	Add-VSTeamAzureRMServiceEndpoint    -DisplayName $servicePrincipalName `
										-ProjectName $ProjectName `
										-SubscriptionId $SubscriptionId `
										-subscriptionTenantId $subscriptionTenantId `
										-servicePrincipalId $serviceConnectionServicePrincipal.AppId `
										-servicePrincipalKey $($serviceConnectionCredential.SecretText) `
										-ErrorAction SilentlyContinue `
										| Out-Null

	Write-Host "`nService connection ($servicePrincipalName) created."

	Write-Host "`nSet-ServiceConnection completed."
}