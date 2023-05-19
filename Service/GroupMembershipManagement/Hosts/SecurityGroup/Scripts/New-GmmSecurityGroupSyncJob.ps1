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

.PARAMETER Query
This value depends on the type of sync job.  See example below for details.
Single Source
[{"type":"SecurityGroup","source":"<group-object-id>"}]

Multiple Sources
[{"type":"SecurityGroup","source":"<group-object-id-1>"},{"type":"SecurityGroup","source":"<group-object-id-2>"}]

.PARAMETER ThresholdPercentageForAdditions
This value determines threshold percentage for users being added.  Default value is 100 unless specified in the sync request. See example below for details.

.PARAMETER ThresholdPercentageForRemovals
This value determines threshold percentage for users being removed.  Default value is 10 unless specified in the sync request. See example below for details.

.PARAMETER GroupTenantId
Optional
If your group resides in a different tenant than your storage account, provide the tenant id where the group was created.

.EXAMPLE
Add-AzAccount

New-GmmSecurityGroupSyncJob	-SubscriptionName "<subscription name>" `
                            -SolutionAbbreviation "<solution abbreviation>" `
							-EnvironmentAbbreviation "<env>" `
							-Requestor "<requestor email address>" `
							-TargetOfficeGroupId "<destination group object id>" `
							-Query "<query>" `                       # See .PARAMETER Query above for more information
							-Period <in hours, integer only> `
							-ThresholdPercentageForAdditions <100> ` # integer only
							-ThresholdPercentageForRemovals <10> `   # integer only
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
		[Parameter(Mandatory=$False)]
		[int] $ThresholdPercentageForAdditions = 100,
		[Parameter(Mandatory=$False)]
		[int] $ThresholdPercentageForRemovals = 10,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop,
		[Parameter(Mandatory=$False)]
		[Guid] $GroupTenantId
	)
	"New-GmmSecurityGroupSyncJob starting..."

	$jobTableContext = Set-AzContext -SubscriptionName $SubscriptionName

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$storageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName

	$storageAccountNamePrefix = "jobs$EnvironmentAbbreviation"
	$jobStorageAccount = $storageAccounts | Where-Object { $_.StorageAccountName -like "$storageAccountNamePrefix*" }

	$tableName = "syncJobs"
	$cloudTable = (Get-AzStorageTable -Name $tableName -Context $jobStorageAccount.Context).CloudTable

	# see https://docs.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities#filtering-on-guid-properties
	$rowExists = Get-AzTableRow -Table $cloudTable  -CustomFilter "(TargetOfficeGroupId eq guid'$TargetOfficeGroupId')"

	if ($null -ne $rowExists)
	{
		Write-Host "A group with TargetOfficeGroup Id $TargetOfficeGroupId already exists in the table. This job will not be onboarded." -ForegroundColor Red 
		return;
	}

	if ($null -ne $GroupTenantId -and [Guid]::Parse($jobTableContext.Tenant.Id) -ne $GroupTenantId)
	{
		Write-Host "Please sign into an account that can read the display names of groups in the $GroupTenantId tenant."
		Add-AzAccount -Tenant $GroupTenantId
	}

	$targetGroup = Get-AzADGroup -ObjectId $TargetOfficeGroupId
	$sourceGroups = $Query.Split(";") | ForEach-Object { Get-AzADGroup -ObjectId $_ } | ForEach-Object { "$($_.DisplayName) ($($_.Id))" }
	$sourceGroupJson = $Query.Split(";") | ForEach-Object { @{"type"="SecurityGroup"; "source"=$_} } | ConvertTo-Json -Compress
	
	# Some versions of Powershell don't have the AsArray parameter on ConvertTo-Json, which means we have to add the array brackets ourselves
	# especially if there's just one group
	if (-not $sourceGroupJson.StartsWith('['))
	{
		$sourceGroupJson = "[$sourceGroupJson]";
	}

	# set the context back even if they cancel out of this
	Set-AzContext -Context $jobTableContext | Out-Null

	if ($null -eq $targetGroup)
	{
		Write-Host -Object "Could not read group names. Ensure GroupTenantId is set to the tenant the groups exist in." -ForegroundColor Yellow
		Write-Host "Syncing the group(s) $Query into the destination: $TargetOfficeGroupId."
	}
	else
	{
		Write-Host "Syncing the group(s) $($sourceGroups -join ", ") ($sourceGroupJson) into the destination: $($targetGroup.DisplayName) ($($targetGroup.Id))."
	}

	$response = Read-Host -Prompt "Are these groups correct? [y/n]"

	if ( -not $response.ToLower().StartsWith("y"))
	{
		Write-Host "Not onboarding the sync."
		return;
	}


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
			"TargetOfficeGroupId"=$TargetOfficeGroupId;
			"Status"="Idle";
			"LastRunTime"=$lastRunTime;
			"Period"=$Period;  # in hours, integers only
			"Query"=$sourceGroupJson;
			"StartDate"=$StartDate;
			"ThresholdPercentageForAdditions"=$ThresholdPercentageForAdditions; # integers only
			"ThresholdPercentageForRemovals"=$ThresholdPercentageForRemovals;   # integers only
			"IsDryRunEnabled"=$False;
			"DryRunTimeStamp"=$lastRunTime;
		}

	Add-AzTableRow `
		-table $cloudTable `
		-partitionKey $partitionKey `
		-rowKey ($rowKey) -property $property

	"New-GmmSecurityGroupSyncJob completed."
}