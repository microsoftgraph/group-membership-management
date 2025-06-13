<#
    Helper functions for resetting GMM.
#>

function Get-KeyVaultSecretWithFirewallRetry {
    param (
        [Parameter(Mandatory = $true)]
        [string]$VaultName,

        [Parameter(Mandatory = $true)]
        [string]$ResourceGroup,

        [Parameter(Mandatory = $true)]
        [string]$SecretName,

        [int]$MaxRetries = 2
    )

    function Add-KeyVaultIpFromError {
        param (
            [string]$VaultName,
            [string]$ResourceGroup,
            [string]$ErrorMessage
        )

        if ($ErrorMessage -match "Client address:\s*([\d\.]+)") {
            $ipToAdd = $matches[1]
            Write-Host "Extracted IP: $ipToAdd"

            $kv = Get-AzKeyVault -VaultName $VaultName -ResourceGroupName $ResourceGroup
            $existingIps = $kv.NetworkAcls.IpAddressRanges

            if ($existingIps -notcontains $ipToAdd) {
                $updatedIps = $existingIps + $ipToAdd

                Update-AzKeyVaultNetworkRuleSet -VaultName $VaultName `
                    -ResourceGroupName $ResourceGroup `
                    -IpAddressRange $updatedIps `
                    -DefaultAction Deny

                Start-Sleep -Seconds 10

                Write-Host "✅ IP $ipToAdd added to Key Vault firewall rules."
            }
            else {
                Write-Host "ℹ️ IP $ipToAdd is already in the allowed list."
            }
        }
        else {
            Write-Host "⚠️ No IP address found in the error message."
        }
    }

    $retryCount = 0
    $secretValue = $null

    while ($retryCount -lt $MaxRetries -and -not $secretValue) {
        try {
            $secretValue = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -AsPlainText -ErrorAction Stop
            Write-Host "✅ Secret '$SecretName' retrieved successfully."
        }
        catch {
            $errorMessage = $_.Exception.Message
            Write-Host "❌ Error retrieving secret: $errorMessage"

            if ($retryCount -eq 0) {
                Add-KeyVaultIpFromError -VaultName $VaultName -ResourceGroup $ResourceGroup -ErrorMessage $errorMessage
                Write-Host "🔁 Retrying after updating firewall..."
            }
            else {
                Write-Host "❌ Retry failed. Exiting."
            }

            $retryCount++
        }
    }

    return $secretValue
}

function Get-ComputeWebApp {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation
    )

    $resourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $appName = "$resourceGroupName-webapi"
    $app = Get-AzWebApp -ResourceGroupName $resourceGroupName -Name $appName -ErrorAction SilentlyContinue
    if (-not $app) {
        Write-Error "❌ Web App '$appName' not found in resource group '$resourceGroupName'."
        return $null
    }
    return $app
}

function Reset-GMM-WithServicePrincipal {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )

    $app = Get-ComputeWebApp -EnvironmentAbbreviation $EnvironmentAbbreviation -SolutionAbbreviation $SolutionAbbreviation
    if (-not $app) {
        Write-Error "❌ Unable to retrieve the web app for GMM reset."
        return
    }

    $prereqsKeyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $client_id = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiClientId"
    $resource = "api://$client_id"
    $token = (Get-AzAccessToken -ResourceUrl $resource).Token
    $plainToken = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($token)
    )

    Reset-GMM `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AccessToken $plainToken
}

function Reset-GMM-WithCredentials {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )

    $app = Get-ComputeWebApp -EnvironmentAbbreviation $EnvironmentAbbreviation -SolutionAbbreviation $SolutionAbbreviation
    if (-not $app) {
        Write-Error "❌ Unable to retrieve the web app for GMM reset."
        return
    }

    $prereqsKeyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $client_id = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiClientId"
    $tenant_id = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiTenantId"
    $client_secret = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiClientSecret"
    if (-not $client_id -or -not $tenant_id -or -not $client_secret) {
        Write-Error "❌ Missing credentials for GMM reset. Please ensure the secrets are set in the Key Vault."
        return
    }

    $scope = "api://$client_id/.default"
    $token_url = "https://login.microsoftonline.com/$tenant_id/oauth2/v2.0/token"
    $body = @{
        client_id     = $client_id
        client_secret = $client_secret
        scope         = $scope
        grant_type    = "client_credentials"
    }
    $response = Invoke-RestMethod -Uri $token_url -Method Post -ContentType "application/x-www-form-urlencoded" -Body $body
    $access_token = $response.access_token

    Reset-GMM `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AccessToken $access_token

    Write-Host "Call reset endpoint is complete"
}

function Reset-GMM {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$AccessToken
    )

    $serviceStatuses = @{
        0 = 'Running'
        1 = 'Stopped'
        2 = 'Resetting'
        3 = 'Stopping'
        4 = 'Starting'
        5 = 'Error'
    }

    $api_url = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/api/v1/operations/Reset"
    $headers = @{
        "Authorization" = "Bearer $AccessToken"
        "Content-Type"  = "application/json"
    }

    $maxRetries = 3
    $attempt = 0
    $success = $false
    $response = $null

    while (-not $success -and $attempt -le $maxRetries) {
        try {
            # Replace this with your actual HTTP call
            # Example:
            $response = Invoke-RestMethod -Uri $api_url -Method POST -Headers $headers -ErrorAction Stop
            $success = $true
            Start-Sleep -Seconds 15
        }
        catch {
            $attempt++
            if ($attempt -gt $maxRetries) {
                Write-Host "HTTP call failed after $attempt attempts. Error: $($_.Exception.Message)"
                throw
            }
            else {
                Write-Host "HTTP call attempt $attempt failed. Retrying in 2 seconds..."
                Start-Sleep -Seconds 2
            }
        }
    }

    $api_url = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/api/v1/operations/servicestatus"
    $response = Invoke-RestMethod -Uri $api_url -Headers $headers -Method GET
    [int]$statusCode = [int]$response.status
    $startTime = Get-Date

    while ($response.status -ne 0) {
        Write-Output "Current service status: $($serviceStatuses[$statusCode])"
        Start-Sleep -Seconds 60
        $api_url = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/api/v1/operations/servicestatus"
        $response = Invoke-RestMethod -Uri $api_url -Headers $headers -Method GET

        if ((Get-Date) - $startTime -gt (New-TimeSpan -Minutes 10)) {
            Write-Host "Wait for 10 minutes to reset GMM, proceeding to next step."
            break
        }
    }

    if ($response.status -eq 0) {
        Write-Output "Current service status: $($serviceStatuses[$statusCode])"
    }

    return $serviceStatuses[$statusCode];
}

function Run-JobScheduler {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )


    $dataKeyVaultName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $functionKey = Get-KeyVaultSecretWithFirewallRetry -VaultName $dataKeyVaultName -ResourceGroup $dataKeyVaultName -SecretName "jobSchedulerFunctionKey"
    $baseUrl = Get-KeyVaultSecretWithFirewallRetry -VaultName $dataKeyVaultName -ResourceGroup $dataKeyVaultName -SecretName "jobSchedulerFunctionBaseUrl"
    if (-not $functionKey -or -not $baseUrl) {
        Write-Error "❌ Missing JobScheduler settings. Please ensure the secrets are set in the Key Vault."
        return
    }

    $fullUrl = "$baseUrl/api/pipelineinvocationstarterfunction?code=$functionKey"
    $body = @{
        DelayForDeploymentInMinutes = 5
    } | ConvertTo-Json

    $response = Invoke-RestMethod -Uri $fullUrl -Method Post -ContentType "application/json" -Body $body
    if ($response -and $response.statusQueryGetUri) {
        Write-Host "✅ Job Scheduler invoked successfully."

        $statusResponse = $null
        $startTime = Get-Date
        while ($statusResponse.runtimeStatus -ne "Completed" -and $statusResponse.runtimeStatus -ne "Failed") {
            Start-Sleep -Seconds 5
            $statusResponse = Invoke-RestMethod -Uri $response.statusQueryGetUri -Method GET

            if ((Get-Date) - $startTime -gt (New-TimeSpan -Minutes 5)) {
                Write-Host "Waited Job Scheduler to complete for 5 minutes, proceeding to next step."
                 break
            }
        }

        Write-Host "Job Scheduler status: $($statusResponse.runtimeStatus)"
    }
    else {
        Write-Error "❌ Failed to invoke Job Scheduler. Response: $($response | ConvertTo-Json)"
    }
}