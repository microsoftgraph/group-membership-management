$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create a sync job

.DESCRIPTION
This script facilitates the creation of a GMM sync job

.PARAMETER SubscriptionName
The name of the subscription into which GMM is installed.

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.PARAMETER Owner
The requestor of the sync job.

.PARAMETER TargetOfficeGroupId
The destination M365 Group into which source users will be synced.

.PARAMETER StartDate
The date that the sync job should start.

.PARAMETER Enabled
Sets the sync job to enabled if $True and disabled if $False

.PARAMETER Query
This value depends on the type of sync job.  See example below for details.

.EXAMPLE
Add-AzAccount
New-GmmSecurityGroupSyncJob	-SubscriptionName "<subscription name>" `
							-EnvironmentAbbreviation "<env>" `
							-Owner "<requestor email address>" `
							-TargetOfficeGroupId "<destination group object id>" `
							-Query "<source group object id(s) (separated by ';')>" `
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
		[string] $Owner,
		[Parameter(Mandatory=$True)]
		[Guid] $TargetOfficeGroupId,
		[Parameter(Mandatory=$True)]
		[string] $Query,
		[Parameter(Mandatory=$False)]
		[DateTime] $StartDate,
		[Parameter(Mandatory=$True)]
		[boolean] $Enabled,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	"New-GmmSecurityGroupSyncJob starting..."

	Set-AzContext -SubscriptionName $SubscriptionName
	Install-Module AzTable -Scope CurrentUser

	$resourceGroupName = "gmm-data-$EnvironmentAbbreviation"
	$storageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName

	$storageAccountNamePrefix = "jobs$EnvironmentAbbreviation"
	$jobStorageAccount = $storageAccounts | ? { $_.StorageAccountName -like "$storageAccountNamePrefix*" }

	$tableName = "syncJobs"
	$cloudTable = (Get-AzStorageTable –Name $tableName –Context $jobStorageAccount.Context).CloudTable

	$now = Get-Date
	$partitionKey = "$($now.Year)-$($now.Month)-$($now.Day)"
	$rowKey = (New-Guid).Guid

	if ($Null -eq $StartDate)
	{
		$StartDate = ([System.DateTime]::UtcNow)
	}

	$lastRunTime = Get-Date -Year 1601 -Month 1 -Day 1 -Hour 0 -Minute 0 -Second 0 -Millisecond 0
	$lastRunTime = $lastRunTime.AddHours(-8)

	$period = 6

	$property  = @{
			"Owner"=$Owner;
			"Type"="SecurityGroup";
			"TargetOfficeGroupId"=$TargetOfficeGroupId;
			"Status"="Idle";
			"LastRunTime"=$lastRunTime
			"Period"=$period;  # in hours, integers only
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