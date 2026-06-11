$ErrorActionPreference = "Stop"
<#
.EXAMPLE
Reset-Function -SubscriptionId "<SubscriptionId>" `
                            -SolutionAbbreviation "<SolutionAbbreviation>" `
                            -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                            -FunctionName "<FunctionName>" `
                			-Verbose
#>

function Reset-Function {
	[CmdletBinding()]
	param(
        [Parameter(Mandatory=$True)]
		[string] $SubscriptionId,
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $FunctionName,
        [Parameter(Mandatory = $False)]
		[string] $InstanceIdentifier,
		[Parameter(Mandatory = $False)]
		[switch] $StartJobTrigger,
        [Parameter(Mandatory = $False)]
        [bool] $StopFunction = $True,
        [Parameter(Mandatory = $False)]
        [bool] $StartFunction = $True
	)

    Set-AzContext -Subscription $SubscriptionId

    # Stop Function App
    $functionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$FunctionName"
    
    if (-not $StopFunction) {
        Write-Output "Skipping stopping $functionAppName as per parameter."
    }
    else {
        Write-Output "Stopping $functionAppName..."

        Stop-AzFunctionApp -ResourceGroupName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation" `
                            -Name $functionAppName -Force

        # Verify if the Function App is stopped
        $functionApp = Get-AzFunctionApp -ResourceGroupName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation" `
                                        -Name $functionAppName

        while ($functionApp.State -ne "Stopped") {
            Write-Output "$functionAppName is not stopped yet. Current state: $($functionApp.State)"
            Start-Sleep -Seconds 10
            $functionApp = Get-AzFunctionApp -ResourceGroupName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation" `
                                            -Name $functionAppName
        }

        Write-Output "Stopped $functionAppName."
    }

    # Reset Function Storage Tables
    $resourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";

    $functionAbbreviation = ($FunctionName | Select-String -CaseSensitive -AllMatches -Pattern '[A-Z]').Matches.Value -join ''
    $prefix = $functionAbbreviation + $SolutionAbbreviation + $EnvironmentAbbreviation + "prod" + $InstanceIdentifier
    $allFunctionStorageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroup | Where-Object { $_.StorageAccountName -like "$prefix*" }

    if ($allFunctionStorageAccounts.Count -eq 0) {
        Write-Warning "No storage account found starting with '$prefix'. Skipping..."
    }

    if ($allFunctionStorageAccounts.Count -gt 1) {
        # Multiple matches found
        if ($InstanceIdentifier -eq "") {
            # No size identifier given, try to filter out any accounts with known size keywords
            $sizeKeywords = @("small","medium","large","onboarding")
            $filteredMatches = $allFunctionStorageAccounts | Where-Object {
                $acctName = $_.StorageAccountName.ToLower()
                $matchesSize = $sizeKeywords | ForEach-Object { $acctName -like "*$_*" }
                -not ($matchesSize -contains $true)
            }

            if ($filteredMatches.Count -eq 1) {
                $functionStorageAccount = $filteredMatches[0]
            } elseif ($filteredMatches.Count -gt 1) {
                Write-Warning "Multiple non-size storage accounts found. Using $($filteredMatches[0].StorageAccountName)."
                $functionStorageAccount = $filteredMatches[0]
            } else {
                Write-Warning "No non-size storage account found. Using $($allFunctionStorageAccounts[0].StorageAccountName)."
                $functionStorageAccount = $allFunctionStorageAccounts[0]
            }
        } else {
            # If we have a size identifier, just pick the first match
            Write-Warning "Multiple matches found. Using $($allFunctionStorageAccounts[0].StorageAccountName)."
            $functionStorageAccount = $allFunctionStorageAccounts[0]
        }
    } else {
        # Exactly one match
        $functionStorageAccount = $allFunctionStorageAccounts[0]
    }

    if ($null -eq $functionStorageAccount) {
        Write-Host "No storage account found for $FunctionName, with expected prefix $prefix"
    }
    else {
        $functionStorageAccountName = $functionStorageAccount.StorageAccountName
        $ctx = New-AzStorageContext -StorageAccountName $functionStorageAccountName -UseConnectedAccount

        Write-Host "Deleting queues from $functionStorageAccountName storage account, for function $FunctionName"
        $listOfQueuesToClear = (Get-AzStorageQueue -Context $ctx).name | Where-Object { $_ -like "*-control-*" -or $_ -like "*-workitems*" }

        $countOfQueuesToClear = $listOfQueuesToClear.Count
        if ($countOfQueuesToClear -eq 0) {
            Write-Host "No queues found to clear from $functionStorageAccountName storage account, for function $FunctionName"
        }
        else {
            Write-Host "Number of queues to be cleared from $functionStorageAccountName is $countOfQueuesToClear"
            ForEach ($queue in $listOfQueuesToClear) {
                Write-Host "Deleting queue: $queue"
                Remove-AzStorageQueue –Name $queue –Context $ctx -Force
                Write-Host "Deleted queue: $queue"
            }
            Write-Host "Deleted $countOfQueuesToClear queues from $functionStorageAccountName"
        }

        Write-Host "Deleting instances, history tables from $functionStorageAccountName storage account, for function $FunctionName"
        $listOfTablesToDelete = (Get-AzStorageTable -Context $ctx).name | Where-Object { $_ -like "*Instances*" -or $_ -like "*History*" }
        $countOfTablesToDelete = $listOfTablesToDelete.Count
        if($countOfTablesToDelete -eq 0) {
            Write-Host "No tables found to delete from $functionStorageAccountName storage account, for function $FunctionName"
        }
        else {
            Write-Host "Number of tables to be deleted from $functionStorageAccountName is $countOfTablesToDelete"
            ForEach ($table in $listOfTablesToDelete) {
                Write-Host "Deleting table: $table"
                Remove-AzStorageTable –Name $table –Context $ctx -Force
                Write-Host "Deleted table: $table"
            }
            Write-Host "Deleted $countOfTablesToDelete tables from $functionStorageAccountName"
        }
    }

    if (-not $StartFunction) {
        Write-Output "Skipping starting $functionAppName as per parameter."
    }
    else {
        # Deleting a table takes at least 40 seconds, so we need to wait a bit before restarting the functions
        # reference: https://learn.microsoft.com/en-us/rest/api/storageservices/delete-table#remarks

        Start-Sleep -Seconds 60

        if (($FunctionName -ne 'JobTrigger') -or $StartJobTrigger.IsPresent) {
            # Start Function App
            Write-Output "Starting $functionAppName..."

            Start-AzFunctionApp -ResourceGroupName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation" `
                                -Name $functionAppName

            # Verify if the Function App is started
            $functionApp = Get-AzFunctionApp -ResourceGroupName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation" `
                                            -Name $functionAppName

            while ($functionApp.State -ne "Running") {
            Write-Output "$functionAppName is not started yet. Current state: $($functionApp.State)"
            Start-Sleep -Seconds 10
            $functionApp = Get-AzFunctionApp -ResourceGroupName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation" `
                    -Name $functionAppName
            }

            Write-Output "Started $functionAppName."
        }
        else {
            Write-Output "Did not start $functionAppName because JobTrigger will stay off without override."
        }
    }
}