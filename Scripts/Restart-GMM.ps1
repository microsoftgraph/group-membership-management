$ErrorActionPreference = "Stop"
<#
.EXAMPLE
Restart-GMM -SubscriptionName "<SubscriptionName>" `
            -SolutionAbbreviation "<SolutionAbbreviation>" `
            -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
            -JobSchedulerFunctionKey "<JobSchedulerFunctionKey>" `
            -GraphUpdaterStorageAccountName "<GraphUpdaterStorageAccountName>" `
			-Verbose
#>

function Restart-GMM {
	[CmdletBinding()]
	param(
        [Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $True)]
		[string] $JobSchedulerFunctionKey,
        [Parameter(Mandatory = $True)]
		[string] $GraphUpdaterStorageAccountName
	)

    Set-AzContext -Subscription $SubscriptionName
    $resourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation";
    $functionNames = "JobTrigger", "GraphUpdater", "GroupMembershipObtainer", "SqlMembershipObtainer", "GroupOwnershipObtainer", "PlaceMembershipObtainer", "TeamsChannelMembershipObtainer"

    foreach ($functionName in $functionNames)
    {
        $functionApp = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionName";
        Write-Host "Stopping $functionApp"
        Stop-AzFunctionApp -Name $functionApp -ResourceGroupName $resourceGroup -Force
        Write-Host "Stopped $functionApp"
    }

    $jobSchedulerFunctionBaseUrl = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-jobscheduler.azurewebsites.net"
    $sqlDatabaseConnectionString = "Server=tcp:$SolutionAbbreviation-data-$EnvironmentAbbreviation.database.windows.net,1433;Initial Catalog=$SolutionAbbreviation-data-$EnvironmentAbbreviation-jobs;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90;"

    Write-Host "Deleting instances, history tables from $GraphUpdaterStorageAccountName"
    $storageAccount = Get-AzStorageAccount -ResourceGroupName "$SolutionAbbreviation-data-$EnvironmentAbbreviation" -Name "$GraphUpdaterStorageAccountName"
    $ctx = $storageAccount.Context
    $listOfTablesToDelete = (Get-AzStorageTable -Context $ctx).name
    $countOfTablesToDelete = $listOfTablesToDelete.Count
    Write-Host "Number of tables to be deleted from $GraphUpdaterStorageAccountName is $countOfTablesToDelete"
    ForEach ($table in $listOfTablesToDelete) {
        Write-Host $table
        Remove-AzStorageTable –Name $table –Context $ctx -Force
    }

    Write-Host "Updating LastRunTime and Status of jobs"

    function Set-UpdateJob {

        # Connect to the SQL Server instance
        $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
        $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate($context.Account, $context.Environment, $context.Tenant.Id.ToString(), $null, [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never, $null, "https://database.windows.net").AccessToken
        $connection = New-Object System.Data.SqlClient.SqlConnection
        $connection.ConnectionString = $sqlDatabaseConnectionString
        $connection.AccessToken = $sqlToken
        $connection.Open()

        $updateQuery = "UPDATE SyncJobs SET LastRunTime = GetDate() - 2, Status = 'Idle' WHERE Status IN ('InProgress', 'StuckInProgress', 'ErroredDueToStuckInProgress')"
        $updateCommand = $connection.CreateCommand()
        $updateCommand.CommandText = $updateQuery
        $updateCommand.ExecuteNonQuery()

        $connection.Close()
    }

    try {
        Set-UpdateJob
    }
    catch {
        Write-Host "Error: $_"
        throw
    }

    Write-Host "Running JobScheduler"
    $functionUrl = "$jobSchedulerFunctionBaseUrl/api/PipelineInvocationStarterFunction?code=$JobSchedulerFunctionKey"
    $jobSchedulerResponse = Invoke-RestMethod -Uri $functionUrl -Method Post -Body "{'DelayForDeploymentInMinutes': 1}" -ContentType 'application/json' -StatusCodeVariable statusCode
    $statusCode
    $jobSchedulerSuccess = $?

    if ($jobSchedulerSuccess -eq $false) {
        Write-Host "Skipping remaining code because JobScheduler was not successful."
        return
    }

    $functionNames = "GraphUpdater", "GroupMembershipObtainer", "SqlMembershipObtainer", "GroupOwnershipObtainer", "PlaceMembershipObtainer", "TeamsChannelMembershipObtainer"
    foreach ($functionName in $functionNames)
    {
        $functionApp = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionName";
        Write-Host "Starting $functionApp"
        Start-AzFunctionApp -Name $functionApp -ResourceGroupName $resourceGroup
        Start-Sleep -Seconds 30
        Write-Host "Started $functionApp"
    }

    $functionUrl = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-graphupdater.azurewebsites.net/"
    $graphUpdaterResponse = Invoke-RestMethod -Method 'Get' -Uri $functionUrl -StatusCodeVariable statusCode
    $statusCode

    $storageAccount = Get-AzStorageAccount -ResourceGroupName "$SolutionAbbreviation-data-$EnvironmentAbbreviation" -Name "$GraphUpdaterStorageAccountName"
    $ctx = $storageAccount.Context
    $tableCount = 0

    while ($tableCount -ne 2) {
        $graphUpdaterTables = Get-AzStorageTable -Context $ctx | select Name
        $tableCount = $graphUpdaterTables.Count

        if ($tableCount -eq 2) {
            Write-Host "Starting JobTrigger"
            Start-AzFunctionApp -Name "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-JobTrigger" -ResourceGroupName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
            Write-Host "Started JobTrigger"
        } else {
            Write-Host "Waiting for tables to be created. Currently there are $tableCount tables."
            Start-Sleep -Seconds 10
        }
    }
}