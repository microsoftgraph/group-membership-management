$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create a sync job

.DESCRIPTION
This script facilitates the creation of a GMM sync job

.PARAMETER SubscriptionName
The name of the subscription into which GMM is installed.

.PARAMETER SolutionAbbreviation
Abbreviation for the solution.

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.PARAMETER Requestor
The requestor of the sync job.

.PARAMETER TargetOfficeGroupId
The destination M365 Group into which source users will be synced.

.PARAMETER StartDate
The date that the sync job should start.

.PARAMETER Period
Sets the frequency for the job execution. In hours. Integers only. Default is 6 hours.

.PARAMETER Enabled
Sets the sync job to enabled if $True and disabled if $False

.PARAMETER Query
This value depends on the type of sync job.  See example below for details.

.EXAMPLE
Add-AzAccount

New-GmmSecurityGroupSyncJob	-SubscriptionName "<subscription name>" `
                            -SolutionAbbreviation "<solution abbreviation>" `
							-EnvironmentAbbreviation "<env>" `
							-Requestor "<requestor email address>" `
							-TargetOfficeGroupId "<destination group object id>" `
							-Query "<source group object id(s) (separated by ';')>" `
							-Period <in hours, integer only> `
							-Enabled $False `
							-Verbose
#>
function New-GmmSecurityGroupSyncJob {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $Requestor,
		[Parameter(Mandatory=$True)]
		[Guid] $TargetOfficeGroupId,
		[Parameter(Mandatory=$True)]
		[string] $Query,
		[Parameter(Mandatory=$False)]
		[DateTime] $StartDate,
		[Parameter(Mandatory=$False)]
		[int] $Period = 6,
		[Parameter(Mandatory=$True)]
		[boolean] $Enabled,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	"New-GmmSecurityGroupSyncJob starting..."
	
	Set-AzContext -SubscriptionName $SubscriptionName

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$storageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName

	$storageAccountNamePrefix = "jobs$EnvironmentAbbreviation"
	$jobStorageAccount = $storageAccounts | Where-Object { $_.StorageAccountName -like "$storageAccountNamePrefix*" }

	$tableName = "syncJobs"
	$cloudTable = (Get-AzStorageTable -Name $tableName -Context $jobStorageAccount.Context).CloudTable

	$now = Get-Date
	$partitionKey = "$($now.Year)-$($now.Month)-$($now.Day)"
	$rowKey = (New-Guid).Guid

	if ($Null -eq $StartDate)
	{
		$StartDate = ([System.DateTime]::UtcNow)
	}

	$lastRunTime = Get-Date -Date "1601-01-01T00:00:00.0000000Z"
	
	$property  = @{
			"Requestor"=$Requestor;
			"Type"="SecurityGroup";
			"TargetOfficeGroupId"=$TargetOfficeGroupId;
			"Status"="Idle";
			"LastRunTime"=$lastRunTime;
			"Period"=$Period;  # in hours, integers only
			"Query"=$Query;
			"StartDate"=$StartDate;
			"Enabled"=$Enabled
		}

	Add-AzTableRow `
		-table $cloudTable `
		-partitionKey $partitionKey `
		-rowKey ($rowKey) -property $property

	"New-GmmSecurityGroupSyncJob completed."
}

Add-AzAccount

New-GmmSecurityGroupSyncJob	-SubscriptionName "MSFT-STSolution-02" `
							-SolutionAbbreviation "gmm" `
							-EnvironmentAbbreviation "gl" `
							-Requestor "glovelace@microsoft.com" `
							-TargetOfficeGroupId "00000000-f75f-45b7-9e39-50554aba1c7c" `
							-Query "e90eabe5-6532-474b-b428-2f18a8d0a7f2;ada21a30-95d6-4b2c-bc5e-722753192d78" `
							-Period 6 `
							-Enabled $True `
							-Verbose