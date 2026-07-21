$ErrorActionPreference = "Stop"

$ScriptsDirectory = Split-Path $PSScriptRoot -Parent
. ($ScriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

function Get-LegacyNotificationsQueue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName,

        [Parameter(Mandatory = $true)]
        [string]$NamespaceName,

        [Parameter(Mandatory = $true)]
        [string]$QueueName
    )

    $queues = @(Get-AzServiceBusQueue `
        -ResourceGroupName $ResourceGroupName `
        -NamespaceName $NamespaceName `
        -ErrorAction Stop)

    return $queues |
        Where-Object { $_.Name -eq $QueueName } |
        Select-Object -First 1
}

function Get-ServiceBusQueueMessageCounts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Queue
    )

    $active = [long]$Queue.CountDetailActiveMessageCount
    $deadLetter = [long]$Queue.CountDetailDeadLetterMessageCount
    $scheduled = [long]$Queue.CountDetailScheduledMessageCount
    $transfer = [long]$Queue.CountDetailTransferMessageCount
    $transferDeadLetter = [long]$Queue.CountDetailTransferDeadLetterMessageCount
    $detailedTotal = $active + $deadLetter + $scheduled + $transfer + $transferDeadLetter
    $reportedTotal = [long]$Queue.MessageCount

    return [pscustomobject]@{
        Active             = $active
        DeadLetter         = $deadLetter
        Scheduled          = $scheduled
        Transfer           = $transfer
        TransferDeadLetter = $transferDeadLetter
        Total              = [Math]::Max($detailedTotal, $reportedTotal)
    }
}

function Assert-LegacyNotificationsQueueIsEmpty {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Queue,

        [Parameter(Mandatory = $true)]
        [string]$NotifierFunctionName
    )

    $counts = Get-ServiceBusQueueMessageCounts -Queue $Queue
    if ($counts.Total -eq 0) {
        return
    }

    throw @"
The legacy Service Bus queue '$($Queue.Name)' contains $($counts.Total) message(s):
  Active: $($counts.Active)
  Dead-letter: $($counts.DeadLetter)
  Scheduled: $($counts.Scheduled)
  Transfer: $($counts.Transfer)
  Transfer dead-letter: $($counts.TransferDeadLetter)

The queue was not deleted and the deployment has been stopped.
Start the '$NotifierFunctionName' function app to drain active messages, wait until every count above is zero, and retry the deployment.
Retrying while any messages remain will fail again. Dead-letter, scheduled, or transfer messages may require manual handling.
"@
}

function Remove-LegacyNotificationsQueue {
    <#
    .SYNOPSIS
    Removes the legacy notifications queue before it is replaced by a topic.

    .DESCRIPTION
    The migration is idempotent. If the queue does not exist, it continues.
    If any messages remain, it stops the deployment without deleting the queue.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )

    $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $namespaceName = $dataResourceGroupName
    $queueName = "notifications"
    $notifierFunctionName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-Notifier"
    $computeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $webApiName = "$computeResourceGroupName-webapi"

    Write-Host "Checking for legacy Service Bus queue '$queueName'..."

    $queue = Get-LegacyNotificationsQueue `
        -ResourceGroupName $dataResourceGroupName `
        -NamespaceName $namespaceName `
        -QueueName $queueName

    if ($null -eq $queue) {
        Write-Host "Legacy notifications queue was not found. No migration is required." -ForegroundColor DarkGray
        return
    }

    Assert-LegacyNotificationsQueueIsEmpty `
        -Queue $queue `
        -NotifierFunctionName $notifierFunctionName

    $webApi = Get-AzWebApp `
        -ResourceGroupName $computeResourceGroupName `
        -Name $webApiName `
        -ErrorAction Stop
    $webApiStoppedByMigration = $webApi.State -ne "Stopped"

    try {
        if ($webApiStoppedByMigration) {
            Write-Host "Stopping WebAPI '$webApiName' before deleting the legacy notifications queue..." -ForegroundColor Yellow
            Invoke-WithRetry `
                -Operation {
                    Stop-AzWebApp `
                        -ResourceGroupName $computeResourceGroupName `
                        -Name $webApiName `
                        -ErrorAction Stop | Out-Null

                    $currentWebApi = Get-AzWebApp `
                        -ResourceGroupName $computeResourceGroupName `
                        -Name $webApiName `
                        -ErrorAction Stop

                    if ($currentWebApi.State -ne "Stopped") {
                        throw "WebAPI '$webApiName' has not reached the Stopped state."
                    }
                } `
                -OperationName "Stop WebAPI '$webApiName'" `
                -MaxAttempts 5 `
                -BaseDelaySeconds 2
        }

        # Re-read after all notification producers are stopped.
        $queue = Get-LegacyNotificationsQueue `
            -ResourceGroupName $dataResourceGroupName `
            -NamespaceName $namespaceName `
            -QueueName $queueName

        if ($null -eq $queue) {
            Write-Host "Legacy notifications queue was already removed." -ForegroundColor DarkGray
            return
        }

        Assert-LegacyNotificationsQueueIsEmpty `
            -Queue $queue `
            -NotifierFunctionName $notifierFunctionName

        Write-Host "Removing empty legacy notifications queue '$queueName'..." -ForegroundColor Yellow

        try {
            Remove-AzServiceBusQueue `
                -ResourceGroupName $dataResourceGroupName `
                -NamespaceName $namespaceName `
                -Name $queueName `
                -Confirm:$false `
                -ErrorAction Stop
        }
        catch {
            $queueAfterFailure = Get-LegacyNotificationsQueue `
                -ResourceGroupName $dataResourceGroupName `
                -NamespaceName $namespaceName `
                -QueueName $queueName

            if ($null -ne $queueAfterFailure) {
                throw
            }
        }

        $maxAttempts = 12
        for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
            $remainingQueue = Get-LegacyNotificationsQueue `
                -ResourceGroupName $dataResourceGroupName `
                -NamespaceName $namespaceName `
                -QueueName $queueName

            if ($null -eq $remainingQueue) {
                Write-Host "Legacy notifications queue removed successfully. WebAPI will remain stopped until the topic, subscription, and rule are deployed." -ForegroundColor Green
                return
            }

            if ($attempt -lt $maxAttempts) {
                Start-Sleep -Seconds 5
            }
        }

        throw "The legacy notifications queue '$queueName' still exists after deletion. Retry the deployment after Azure finishes deleting the queue."
    }
    catch {
        $remainingQueue = Get-LegacyNotificationsQueue `
            -ResourceGroupName $dataResourceGroupName `
            -NamespaceName $namespaceName `
            -QueueName $queueName

        if ($webApiStoppedByMigration -and $null -ne $remainingQueue) {
            Write-Host "The legacy queue still exists. Restarting WebAPI '$webApiName' before failing the deployment..." -ForegroundColor Yellow
            Invoke-WithRetry `
                -Operation {
                    Start-AzWebApp `
                        -ResourceGroupName $computeResourceGroupName `
                        -Name $webApiName `
                        -ErrorAction Stop | Out-Null
                } `
                -OperationName "Restore WebAPI '$webApiName'" `
                -MaxAttempts 5 `
                -BaseDelaySeconds 2
        }

        throw
    }
}
