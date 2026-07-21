$ErrorActionPreference = "Stop"

$ScriptsDirectory = Split-Path $PSScriptRoot -Parent
. ($ScriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

function Set-PostDataDeploymentMigrations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )

    $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $computeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $webApiName = "$computeResourceGroupName-webapi"
    $topicName = "notifications"
    $subscriptionName = "notifier"
    $ruleName = "allNotifications"

    Invoke-WithRetry `
        -Operation {
            Get-AzServiceBusTopic `
                -ResourceGroupName $dataResourceGroupName `
                -NamespaceName $dataResourceGroupName `
                -Name $topicName `
                -ErrorAction Stop | Out-Null

            Get-AzServiceBusSubscription `
                -ResourceGroupName $dataResourceGroupName `
                -NamespaceName $dataResourceGroupName `
                -TopicName $topicName `
                -SubscriptionName $subscriptionName `
                -ErrorAction Stop | Out-Null

            $rule = Get-AzServiceBusRule `
                -ResourceGroupName $dataResourceGroupName `
                -NamespaceName $dataResourceGroupName `
                -TopicName $topicName `
                -SubscriptionName $subscriptionName `
                -Name $ruleName `
                -ErrorAction Stop

            if ($rule.SqlExpression -notmatch '(?i)^\s*EXISTS\s*\(\s*MessageType\s*\)\s*$') {
                throw "Service Bus rule '$ruleName' does not have the expected EXISTS(MessageType) filter."
            }
        } `
        -OperationName "Validate notifications topic, subscription, and rule" `
        -MaxAttempts 5 `
        -BaseDelaySeconds 2

    $webApi = Get-AzWebApp `
        -ResourceGroupName $computeResourceGroupName `
        -Name $webApiName `
        -ErrorAction Stop

    if ($webApi.State -eq "Running") {
        Write-Host "WebAPI '$webApiName' is already running." -ForegroundColor DarkGray
        return
    }

    Write-Host "Starting WebAPI '$webApiName' after the notifications topic migration..." -ForegroundColor Yellow
    Invoke-WithRetry `
        -Operation {
            Start-AzWebApp `
                -ResourceGroupName $computeResourceGroupName `
                -Name $webApiName `
                -ErrorAction Stop | Out-Null

            $currentWebApi = Get-AzWebApp `
                -ResourceGroupName $computeResourceGroupName `
                -Name $webApiName `
                -ErrorAction Stop

            if ($currentWebApi.State -ne "Running") {
                throw "WebAPI '$webApiName' has not reached the Running state."
            }
        } `
        -OperationName "Start WebAPI '$webApiName'" `
        -MaxAttempts 5 `
        -BaseDelaySeconds 2

    Write-Host "WebAPI '$webApiName' started successfully." -ForegroundColor Green
}
