<#
    Helper functions for resetting GMM.
#>

function Get-KeyVaultSecretWithFirewallRetry {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$VaultName,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ResourceGroup,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SecretName,

        [ValidateRange(1, 10)]
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
            Write-Warning "⚠️ No IP address found in the error message."
        }
    }

    $retryCount = 0
    $secretValue = $null

    while ($retryCount -lt $MaxRetries -and -not $secretValue) {
        try {
            $secretValue = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -AsPlainText -ErrorAction Stop

            if(-not $secretValue) {
                $retryCount++
            } else {
                Write-Host "✅ Secret '$SecretName' retrieved successfully."
            }
        }
        catch {
            $errorMessage = $_.Exception.Message
            Write-Warning "❌ Error retrieving secret: $errorMessage"

            if ($retryCount -eq 0) {
                Add-KeyVaultIpFromError -VaultName $VaultName -ResourceGroup $ResourceGroup -ErrorMessage $errorMessage
                Write-Host "🔁 Retrying after updating firewall..."
            }
            else {
                Write-Error "❌ Retry failed. Exiting."
            }

            $retryCount++
        }
    }

    if(-not $secretValue) {
        Write-Error "❌ Error retrieving secret: $SecretName"
    }

    return $secretValue
}

function Get-ComputeWebApp {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
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

function Reset-GMMWithServicePrincipal {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation
    )

    $app = Get-ComputeWebApp -EnvironmentAbbreviation $EnvironmentAbbreviation -SolutionAbbreviation $SolutionAbbreviation
    if (-not $app) {
        throw "❌ Unable to retrieve the web app for GMM reset."
    }

    $prereqsKeyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $client_id = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiClientId"
    if (-not $client_id) {
        throw "❌ Failed to retrieve webApiClientId from Key Vault."
    }
    $resource = "api://$client_id"
    $token = (Get-AzAccessToken -ResourceUrl $resource).Token
    if ($token -is [System.Security.SecureString]) {
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($token)
        try {
            $plainToken = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
    else {
        $plainToken = $token
    }

    Reset-GMM `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AccessToken $plainToken

    # Clear sensitive token from memory
    $plainToken = $null
    $client_id = $null
}

function Reset-GMMWithCredentials {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation
    )

    $app = Get-ComputeWebApp -EnvironmentAbbreviation $EnvironmentAbbreviation -SolutionAbbreviation $SolutionAbbreviation
    if (-not $app) {
        throw "❌ Unable to retrieve the web app for GMM reset."
    }

    $prereqsKeyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $client_id = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiClientId"
    $tenant_id = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiTenantId"
    $client_secret = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiClientSecret"
    if (-not $client_id -or -not $tenant_id -or -not $client_secret) {
        throw "❌ Missing credentials for GMM reset. Please ensure the secrets are set in the Key Vault."
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

    try {
        Reset-GMM `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -AccessToken $access_token

        Write-Host "Call reset endpoint is complete"
    }
    finally {
        # Clear sensitive data from memory
        $client_secret = $null
        $access_token = $null
        $body = $null
    }
}

function Reset-GMM {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
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
    $baseDelay = 2

    while (-not $success -and $attempt -le $maxRetries) {
        try {
            $response = Invoke-RestMethod -Uri $api_url -Method POST -Headers $headers -ErrorAction Stop
            $success = $true
            Start-Sleep -Seconds 15
        }
        catch {
            $attempt++
            if ($attempt -gt $maxRetries) {
                Write-Error "❌ HTTP call failed after $attempt attempts. Error: $($_.Exception.Message)"
                throw
            }
            else {
                $delay = $baseDelay * [Math]::Pow(2, $attempt - 1)
                Write-Host "HTTP call attempt $attempt failed. Retrying in $delay seconds..."
                Start-Sleep -Seconds $delay
            }
        }
    }

    $api_url = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/api/v1/operations/servicestatus"
    $response = Invoke-RestMethod -Uri $api_url -Headers $headers -Method GET
    [int]$statusCode = [int]$response.status
    $startTime = Get-Date

    while ($response.status -ne 0) {
        $statusCode = [int]$response.status
        Write-Output "Current service status: $($serviceStatuses[$statusCode]), checking again in 60 seconds..."
        Start-Sleep -Seconds 60
        $api_url = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/api/v1/operations/servicestatus"
        $response = Invoke-RestMethod -Uri $api_url -Headers $headers -Method GET

        if ((Get-Date) - $startTime -gt (New-TimeSpan -Minutes 10)) {
            Write-Host "Wait for 10 minutes to reset GMM, proceeding to next step."
            break
        }
    }

    $statusCode = [int]$response.status

    if ($response.status -eq 0) {
        Write-Output "Current service status: $($serviceStatuses[$statusCode])"
    }

    return $serviceStatuses[$statusCode];
}

function Start-JobScheduler {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation
    )

    $dataKeyVaultName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $functionKey = Get-KeyVaultSecretWithFirewallRetry -VaultName $dataKeyVaultName -ResourceGroup $dataKeyVaultName -SecretName "jobSchedulerFunctionKey"
    $baseUrl = Get-KeyVaultSecretWithFirewallRetry -VaultName $dataKeyVaultName -ResourceGroup $dataKeyVaultName -SecretName "jobSchedulerFunctionBaseUrl"
    if (-not $functionKey -or -not $baseUrl) {
        throw "❌ Missing JobScheduler settings. Please ensure the secrets are set in the Key Vault."
    }

    $fullUrl = "$baseUrl/api/pipelineinvocationstarterfunction?code=$functionKey"
    $body = @{
        DelayForDeploymentInMinutes = 5
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod -Uri $fullUrl -Method Post -ContentType "application/json" -Body $body
        if ($response -and $response.statusQueryGetUri) {
            Write-Host "✅ Job Scheduler invoked successfully."

            $statusResponse = $null
            $startTime = Get-Date
            $pollInterval = 5
            $maxPollInterval = 30
            while ((-not $statusResponse) -or ($statusResponse.runtimeStatus -ne "Completed" -and $statusResponse.runtimeStatus -ne "Failed")) {
                Start-Sleep -Seconds $pollInterval
                try {
                    $statusResponse = Invoke-RestMethod -Uri $response.statusQueryGetUri -Method GET
                    # Gradually increase poll interval to reduce API calls
                    if ($pollInterval -lt $maxPollInterval) {
                        $pollInterval = [Math]::Min($pollInterval + 2, $maxPollInterval)
                    }
                }
                catch {
                    Write-Warning "⚠️ Failed to get status response: $($_.Exception.Message)"
                    break
                }

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
    catch {
        Write-Error "❌ Failed to invoke Job Scheduler: $($_.Exception.Message)"
    }
}

function Set-AppRoleToServicePrincipal {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PrincipalId,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ResourceId,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AppRoleId
    )

    $scriptsDirectory = $PSScriptRoot

    Write-Host "Setting app role to service principal with ID: $PrincipalId"

    . (Join-Path $scriptsDirectory 'Install-MSGraphIfNeeded.ps1')
	Install-MSGraphIfNeeded

    # Connect to Microsoft Graph
    Connect-MgGraph -Scopes "AppRoleAssignment.ReadWrite.All"

    try {
        # Retrieve existing app role assignments for the service principal
        $existingAssignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $PrincipalId

        # Check if the desired assignment already exists
        $assignmentExists = $existingAssignments | Where-Object {
            $_.AppRoleId -eq $AppRoleId -and $_.ResourceId -eq $ResourceId
        }

        if (-not $assignmentExists) {
            # Assignment doesn't exist, so create it
            New-MgServicePrincipalAppRoleAssignment `
                -ServicePrincipalId $PrincipalId `
                -BodyParameter @{
                    principalId = $PrincipalId
                    resourceId  = $ResourceId
                    appRoleId   = $AppRoleId
                }

            Write-Host "✅ App role assignment created successfully."
        } else {
            Write-Host "App role assignment already exists. Skipping creation."
        }
    }
    finally {
        # Disconnect from Microsoft Graph
        Disconnect-MgGraph -ErrorAction SilentlyContinue
    }
}

function Set-WebAPIAsResetAdministrator {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation
    )

    $scriptsDirectory = $PSScriptRoot

    Write-Host "Setting WebAPI as Reset Administrator"

    . (Join-Path $scriptsDirectory 'Install-MSGraphIfNeeded.ps1')
	Install-MSGraphIfNeeded

    # Connect to Microsoft Graph
    Connect-MgGraph -Scopes "AppRoleAssignment.ReadWrite.All"

    try {
        # Define the target role value
        $targetRoleValue = "Operations.Reset"

        # Get the application object
        $app = Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation'" -ErrorAction SilentlyContinue
        if (-not $app) {
            throw "❌ Application '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation' not found."
        }

        # Search for the app role by value
        $role = $app.AppRoles | Where-Object { $_.Value -eq $targetRoleValue }

        if ($role) {
            Write-Host "Role Found:"
            Write-Host "Display Name: $($role.DisplayName)"
            Write-Host "Value: $($role.Value)"
            Write-Host "ID: $($role.Id)"

            $sp = Get-MgServicePrincipal -Filter "appId eq '$($app.AppId)'" -ErrorAction SilentlyContinue
            if (-not $sp) {
                throw "❌ Service Principal for application '$($app.AppId)' not found."
            }

            Write-Host "Service Principal Found:"
            Write-Host "Display Name: $($sp.DisplayName)"
            Write-Host "ID: $($sp.Id)"

            Set-AppRoleToServicePrincipal `
                -PrincipalId $sp.Id `
                -ResourceId $sp.Id `
                -AppRoleId $role.Id
        } else {
            Write-Host "No app role found with value '$targetRoleValue'."
        }
    }
    finally {
        # Disconnect from Microsoft Graph
        Disconnect-MgGraph -ErrorAction SilentlyContinue
    }
}