function Set-UpdateDestination {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
        [Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation
    )

    Write-Host "Start Set-UpdateDestination"

    Set-AzContext -SubscriptionName $SubscriptionName

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$storageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName

	$storageAccountNamePrefix = "jobs$EnvironmentAbbreviation"
	$jobStorageAccount = $storageAccounts | Where-Object { $_.StorageAccountName -like "$storageAccountNamePrefix*" }

    if(!$jobStorageAccount){
        Write-Host "Storage account $storageAccountNamePrefix* does not exist."
        return
    }

    $tableName = "syncJobs"
    $storageTable = Get-AzStorageTable -Name $tableName -Context $jobStorageAccount.Context -ErrorAction SilentlyContinue

    if(!$storageTable){
        Write-Host "syncJobs table does not exist."
        return
    }

    $cloudTable = $storageTable.CloudTable

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    $type = @("OneCatalog", "Securitygroup")

	. ($scriptsDirectory + '\Scripts\Install-AzTableModuleIfNeeded.ps1')
	Install-AzTableModuleIfNeeded | Out-Null

    # see https://docs.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities#filtering-on-guid-properties
	$jobs = Get-AzTableRow -Table $cloudTable

    foreach($job in $jobs)
    {
        if(-not [string]::IsNullOrWhiteSpace($job.Destination)) {
            continue
        }

        $targetOfficeId = $job.TargetOfficeGroupId;

        $job.Destination = (@{type="AzureADGroup";value=$targetOfficeId.ToString()}) | ConvertTo-Json
        $job | Update-AzTableRow -table $cloudTable
    }

    Write-Host "Finish Set-UpdateDestination"
}