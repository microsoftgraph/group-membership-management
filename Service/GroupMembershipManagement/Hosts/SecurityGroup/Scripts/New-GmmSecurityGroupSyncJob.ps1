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

.PARAMETER ThresholdPercentageForAdditions
Threshold percentage for users being added. Default value is 100 unless specified in the request.

.PARAMETER ThresholdPercentageForRemovals
Threshold percentage for users being removed. Default value is 20 unless specified in the request.

.EXAMPLE
Add-AzAccount

New-GmmSecurityGroupSyncJob	-SubscriptionName "<subscription name>" `
                            -SolutionAbbreviation "<solution abbreviation>" `
							-EnvironmentAbbreviation "<env>" `
							-Requestor "<requestor email address>" `
							-TargetOfficeGroupId "<destination group object id>" `
							-Query "<source group object id(s) (separated by ';')>" `
							-Period <in hours, integer only> `
							-ThresholdPercentageForAdditions <integer only> `
							-ThresholdPercentageForRemovals <integer only> `
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
		[int] $ThresholdPercentageForAdditions,
		[Parameter(Mandatory=$True)]
		[int] $ThresholdPercentageForRemovals,
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
			"Enabled"=$Enabled;
			"ThresholdPercentageForAdditions"=$ThresholdPercentageForAdditions;
			"ThresholdPercentageForRemovals"=$ThresholdPercentageForRemovals
		}

	Add-AzTableRow `
		-table $cloudTable `
		-partitionKey $partitionKey `
		-rowKey ($rowKey) -property $property

	"New-GmmSecurityGroupSyncJob completed."
}