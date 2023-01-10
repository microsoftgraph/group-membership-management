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

    if(!$jobStorageAccount){
        Write-Host "Storage account $storageAccountNamePrefix* does not exist."
        return
    }

	$tableName = "syncJobs"
    $storageTable = Get-AzStorageTable -Name $tableName -Context $jobStorageAccount.Context

    if(!$storageTable){
        Write-Host "syncJobs table does not exist."
        return
    }

	$cloudTable = $storageTable.CloudTable

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

	. ($scriptsDirectory + '\Scripts\Install-AzTableModuleIfNeeded.ps1')
	Install-AzTableModuleIfNeeded | Out-Null

    # see https://docs.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities#filtering-on-guid-properties
	$sgJobs = Get-AzTableRow -Table $cloudTable

    foreach($job in $sgJobs)
    {
        if(-not ($job.Query.Contains("SecurityGroup"))) {
            continue
        }

        $newQueryParts = @()
        $currentQuery = ConvertFrom-Json -InputObject $job.Query

        foreach($part in $currentQuery) {

            if($part.type -ne "SecurityGroup") {
                $newQueryParts +=  ConvertTo-Json -InputObject $part -Compress -Depth 100
                continue
            }

            if($part.sources) {
                foreach($id in $part.sources) {
                    $newQueryPart = '{"type":"SecurityGroup","source":"' + $id + '"}'
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

    Write-Host "Finish Set-UpdateSecurityGroupQuery"
}