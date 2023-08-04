function Set-UpdateGroupMembershipQuery {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
        [Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation
    )

    Write-Host "Start Set-UpdateGroupMembershipQuery"

    Set-AzContext -SubscriptionName $SubscriptionName

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$storageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName

	$storageAccountNamePrefix = "jobs$EnvironmentAbbreviation"
	$jobStorageAccount = $storageAccounts | Where-Object { $_.StorageAccountName -like "$storageAccountNamePrefix*" }

	$tableName = "syncJobs"
	$cloudTable = (Get-AzStorageTable -Name $tableName -Context $jobStorageAccount.Context).CloudTable

    # see https://docs.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities#filtering-on-guid-properties
	$sgJobs = Get-AzTableRow -Table $cloudTable

    foreach($job in $sgJobs)
    {
        if(-not ($job.Query.Contains("GroupMembership"))) {
            continue
        }

        $newQueryParts = @()
        $currentQuery = ConvertFrom-Json -InputObject $job.Query

        foreach($part in $currentQuery) {

            if($part.type -ne "GroupMembership") {
                $newQueryParts +=  ConvertTo-Json -InputObject $part -Compress -Depth 100
                continue
            }

            if($part.sources) {
                foreach($id in $part.sources) {
                    $newQueryPart = '{"type":"GroupMembership","source":"' + $id + '"}'
                    $newQueryParts += $newQueryPart
                }
            }
            else {
                $newQueryParts +=  ConvertTo-Json -InputObject $part -Compress -Depth 100
            }

        }

        $newQuery = "[" + ($newQueryParts -join ",") + "]"
        $job.Query = $newQuery
        $job | Update-AzTableRow -table $cloudTable
    }

    Write-Host "Finish Set-UpdateGroupMembershipQuery"
}