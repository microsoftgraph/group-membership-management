function Set-UpdateSecurityGroupQuery {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
        [Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation
    )

    Write-Host "Start Set-UpdateSecurityGroupQuery"

    Set-AzContext -SubscriptionName $SubscriptionName

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$storageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName

	$storageAccountNamePrefix = "jobs$EnvironmentAbbreviation"
	$jobStorageAccount = $storageAccounts | Where-Object { $_.StorageAccountName -like "$storageAccountNamePrefix*" }

	$tableName = "syncJobs"
	$cloudTable = (Get-AzStorageTable -Name $tableName -Context $jobStorageAccount.Context).CloudTable

    # see https://docs.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities#filtering-on-guid-properties
	$sgJobs = Get-AzTableRow -Table $cloudTable  -CustomFilter "(Type eq 'SecurityGroup')"

    foreach($job in $sgJobs)
    {
        $currentQuery = $job.Query
        if ($currentQuery.StartsWith('['))
        {
            continue;
        }

        $newQuery = '[{ "type": "SecurityGroup", "sources": ['

        if (![string]::IsNullOrEmpty($currentQuery))
        {
            $groupIds = $currentQuery.Split(";")
            for ($i=0; $i -lt $groupIds.Length; $i++)
            {
                $groupIds[$i] = """$($groupIds[$i])"""
            }

            $newQuery += [string]::Join(",", $groupIds)
        }

        $newQuery += ']}]'

        $job.Query = $newQuery
        $job | Update-AzTableRow -table $cloudTable
    }

    Write-Host "Finish Set-UpdateSecurityGroupQuery"
}