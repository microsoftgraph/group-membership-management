<#
.SYNOPSIS
Grants the Web API access to the necessary resources.

.DESCRIPTION
This should be run by an owner on the subscription after the storage account and app service have been set up.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-WebAPIAccessRoles	-SolutionAbbreviation "<solution>" `
						-EnvironmentAbbreviation "<env>" `
						-Verbose
#>

function Set-WebAPIAccessRoles {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $False)]
		[string] $DataResourceGroupName = $null,
		[Parameter(Mandatory = $False)]
		[string] $ComputeResourceGroupName = $null,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	Write-Host "Granting RBACs to WebAPI";

	if ([string]::IsNullOrEmpty($ComputeResourceGroupName)) {
		$ComputeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation";
	}

	if ([string]::IsNullOrEmpty($DataResourceGroupName)) {
		$DataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	}

	$currentSubscription = (Get-AzContext).Subscription
	$webApi = Invoke-WithRetry `
		-Operation { Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName -Name "$ComputeResourceGroupName-webapi" } `
		-OperationName "Get WebAPI app" `
		-MaxAttempts 3 -BaseDelaySeconds 2

	if ($webApi) {
		$webApiServicePrincipal = Invoke-WithRetry `
			-Operation { Get-AzADServicePrincipal -DisplayName $webApi.Name } `
			-OperationName "Get WebAPI service principal" `
			-MaxAttempts 3 -BaseDelaySeconds 2

		if ($webApiServicePrincipal) {

			Set-RoleAssignment `
				-ObjectId $webApiServicePrincipal.Id `
				-DisplayName $webApi.Name `
				-Scope "/subscriptions/$($currentSubscription.Id)/resourceGroups/$ComputeResourceGroupName" `
				-RoleDefinitionName "Website Contributor"

			Set-RoleAssignment `
				-ObjectId $webApiServicePrincipal.Id `
				-DisplayName $webApi.Name `
				-Scope "/subscriptions/$($currentSubscription.Id)/resourceGroups/$DataResourceGroupName" `
				-RoleDefinitionName "Reader"

			Set-RoleAssignment `
				-ObjectId $webApiServicePrincipal.Id `
				-DisplayName $webApi.Name `
				-Scope "/subscriptions/$($currentSubscription.Id)/resourceGroups/$ComputeResourceGroupName" `
				-RoleDefinitionName "Reader"

			$signalRResource = Invoke-WithRetry `
			-Operation { Get-AzResource -ResourceGroupName $ComputeResourceGroupName -ResourceType "Microsoft.SignalRService/SignalR" -Name "$ComputeResourceGroupName-signalr" } `
			-OperationName "Get SignalR resource" `
			-MaxAttempts 3 -BaseDelaySeconds 2
			Set-RoleAssignment `
				-ObjectId $webApiServicePrincipal.Id `
				-DisplayName $webApi.Name `
				-Scope $signalRResource.Id `
				-RoleDefinitionName "SignalR App Server"

			$openAIResource = Invoke-WithRetry `
			-Operation { Get-AzResource -ResourceGroupName $DataResourceGroupName -ResourceType "Microsoft.CognitiveServices/accounts" -Name "$DataResourceGroupName-openai" -ErrorAction SilentlyContinue } `
			-OperationName "Get OpenAI resource" `
			-MaxAttempts 3 -BaseDelaySeconds 2
			if ($null -ne $openAIResource) {
				Set-RoleAssignment `
				-ObjectId $webApiServicePrincipal.Id `
				-DisplayName $webApi.Name `
				-Scope $openAIResource.Id `
				-RoleDefinitionName "Cognitive Services OpenAI User"
			}
		}
		elseif ($null -eq $webApiServicePrincipal) {
			Write-Host "Web API $($webApi.Name) was not found!"
		}
	}

	Write-Host "Done.";
}

function Set-RoleAssignment {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $ObjectId,
		[Parameter(Mandatory = $True)]
		[string] $DisplayName,
		[Parameter(Mandatory = $True)]
		[string] $Scope,
		[Parameter(Mandatory = $True)]
		[string] $RoleDefinitionName
	)

	if ($null -eq (Get-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName)) {
		Invoke-WithRetry `
			-Operation { New-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName } `
			-OperationName "Assign $RoleDefinitionName to $DisplayName" `
			-MaxAttempts 3 -BaseDelaySeconds 2
		Write-Host "Added role $RoleDefinitionName to $DisplayName to $Scope.";
	}
	else {
		Write-Host "$DisplayName already has  $RoleDefinitionName role for $Scope.";
	}
}