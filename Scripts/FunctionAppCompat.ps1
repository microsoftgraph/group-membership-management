<#
.SYNOPSIS
Drop-in REST-based replacements for Az.Functions cmdlets used by the GMM
deployment scripts.

.DESCRIPTION
Az.Functions >= 4.1.1 has a bug (Azure/azure-powershell#29630) where every
cmdlet in the module throws:
    Exception calling "ContainsKey" with "1" argument(s):
    "Value cannot be null. (Parameter 'key')"
from RegisterFunctionsTabCompleters -> GetRuntimeName because the Functions
Stacks API now returns runtime entries (e.g. go 1.0 Linux, dotnet 4.7 Windows,
node 6 LTS Windows) whose appSettingsDictionary lacks FUNCTIONS_WORKER_RUNTIME.
The bug fires from the Process block of any Az.Functions cmdlet, so partial
replacement (e.g. swapping only Get-AzFunctionApp) doesn't help — every
cmdlet on the deployment hot path must be replaced.

Fix upstream is merged but unreleased: https://github.com/Azure/azure-powershell/pull/29691
Tracking issue:                        https://github.com/Azure/azure-powershell/issues/29630

This helper exposes the following compat functions that the deployment
scripts dot-source and call in place of the Az.Functions cmdlets:

    Get-FunctionAppCompat            -> Get-AzFunctionApp
    Get-FunctionAppSettingCompat     -> Get-AzFunctionAppSetting
    Update-FunctionAppSettingCompat  -> Update-AzFunctionAppSetting
    Stop-FunctionAppCompat           -> Stop-AzFunctionApp -Force
    Start-FunctionAppCompat          -> Start-AzFunctionApp
    Restart-FunctionAppCompat        -> Restart-AzFunctionApp -Force
    Remove-FunctionAppCompat         -> Remove-AzFunctionApp -Force

Implementations use Az.Accounts (Get-AzContext, Invoke-AzRestMethod) and
Az.Websites (Get/Stop/Start/Restart-AzWebApp), both already loaded by the
deployment. No new module imports.

#####################################################################
# ROLLBACK INSTRUCTIONS (when Az.Functions fix from PR Azure/azure-powershell#29691 ships):
#
# Pre-flight check:
# 1. Confirm the Az.Functions release containing PR #29691 is GA on PowerShell Gallery.
# 2. Confirm Install-AzModuleIfNeeded.ps1 installs an Az meta-module version that
#    bundles the fixed Az.Functions.
# 3. Smoke-test Get-AzFunctionApp against a Flex Consumption app in a non-prod
#    environment with that Az.Functions version loaded.
#
# Mechanical rollback (in this order):
# 1. In each of the following files, replace the compat function names with the
#    original Az.Functions cmdlet names:
#      - Public-GMM\Deployment\Deploy-Resources.ps1
#          Get-FunctionAppCompat            -> Get-AzFunctionApp
#          Get-FunctionAppSettingCompat     -> Get-AzFunctionAppSetting
#          Update-FunctionAppSettingCompat  -> Update-AzFunctionAppSetting
#          Stop-FunctionAppCompat           -> Stop-AzFunctionApp (add -Force on line 1597)
#          Start-FunctionAppCompat          -> Start-AzFunctionApp
#      - Public-GMM\Scripts\PreDeploymentMigrations\Start-FlexConsumptionMigration.ps1
#          Get-FunctionAppCompat            -> Get-AzFunctionApp
#      - Public-GMM\Scripts\PreDeploymentMigrations\Remove-MultiLaneResources.ps1
#          Get-FunctionAppCompat            -> Get-AzFunctionApp
#          Get-FunctionAppSettingCompat     -> Get-AzFunctionAppSetting
# 2. Remove the dot-source line for FunctionAppCompat.ps1 from each of the three
#    files above.
# 3. Delete Public-GMM\Scripts\FunctionAppCompat.ps1.
# 4. Verify with: grep -rn 'FunctionAppCompat' Public-GMM   (should return zero matches)
# 5. Verify with: grep -rn 'Get-AzFunctionApp\|Stop-AzFunctionApp\|Start-AzFunctionApp\|Get-AzFunctionAppSetting\|Update-AzFunctionAppSetting' Public-GMM\Deployment Public-GMM\Scripts\PreDeploymentMigrations  (should match the original call sites)
#
# Tracking:
#   Original bug:        https://github.com/Azure/azure-powershell/issues/29630
#   Fix PR:              https://github.com/Azure/azure-powershell/pull/29691
#   Workaround added:    2026-05-27
#   Workaround removed:  <fill in when rolled back>
#####################################################################
#>

$script:WebSitesApiVersion = '2023-12-01'

function Get-FunctionAppCompatSubscriptionId {
    $ctx = Get-AzContext
    if (-not $ctx -or -not $ctx.Subscription -or -not $ctx.Subscription.Id) {
        throw "Get-FunctionAppCompat*: No active AzContext. Run Connect-AzAccount first."
    }
    return $ctx.Subscription.Id
}

function Invoke-FunctionAppCompatRest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [Parameter(Mandatory = $true)] [ValidateSet('GET','POST','PUT')] [string]$Method,
        [Parameter(Mandatory = $false)] [string]$Payload
    )
    $splat = @{ Path = $Path; Method = $Method }
    if ($PSBoundParameters.ContainsKey('Payload') -and $Payload) { $splat['Payload'] = $Payload }
    $resp = Invoke-AzRestMethod @splat
    if ($null -eq $resp) {
        throw "Invoke-AzRestMethod returned null for $Method $Path"
    }
    if ($resp.StatusCode -lt 200 -or $resp.StatusCode -ge 300) {
        throw "REST $Method $Path failed with status $($resp.StatusCode): $($resp.Content)"
    }
    return $resp
}

function ConvertTo-FunctionAppCompatObject {
    param(
        [Parameter(Mandatory = $true)] $WebApp,
        [Parameter(Mandatory = $false)] [pscustomobject]$SiteRest
    )
    $osType = if ($WebApp.Reserved) { 'Linux' } else { 'Windows' }
    $runtimeName = $null
    $runtimeVersion = $null
    if ($SiteRest -and $SiteRest.properties -and $SiteRest.properties.functionAppConfig -and $SiteRest.properties.functionAppConfig.runtime) {
        $runtimeName = $SiteRest.properties.functionAppConfig.runtime.name
        $runtimeVersion = $SiteRest.properties.functionAppConfig.runtime.version
    }
    return [pscustomobject]@{
        Name           = $WebApp.Name
        State          = $WebApp.State
        Kind           = $WebApp.Kind
        ServerFarmId   = $WebApp.ServerFarmId
        Location       = $WebApp.Location
        OSType         = $osType
        Runtime        = $runtimeName
        RuntimeVersion = $runtimeVersion
    }
}

function Get-FunctionAppCompat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$ResourceGroupName,
        [Parameter(Mandatory = $false)] [string]$Name
    )
    $subId = Get-FunctionAppCompatSubscriptionId

    if ($PSBoundParameters.ContainsKey('Name') -and $Name) {
        $webApp = Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $Name -ErrorAction SilentlyContinue
        if (-not $webApp) { return $null }
        if ($webApp.Kind -notlike '*functionapp*') { return $null }
        $path = "/subscriptions/$subId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$Name`?api-version=$script:WebSitesApiVersion"
        $rest = $null
        try {
            $resp = Invoke-FunctionAppCompatRest -Path $path -Method GET
            $rest = $resp.Content | ConvertFrom-Json
        } catch {
            Write-Verbose "Get-FunctionAppCompat: REST site lookup failed for $Name ($_). Continuing without runtime metadata."
        }
        return ConvertTo-FunctionAppCompatObject -WebApp $webApp -SiteRest $rest
    }

    $allApps = Get-AzWebApp -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue
    if (-not $allApps) { return @() }
    $functionApps = $allApps | Where-Object { $_.Kind -like '*functionapp*' }
    $results = @()
    foreach ($app in $functionApps) {
        $path = "/subscriptions/$subId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$($app.Name)`?api-version=$script:WebSitesApiVersion"
        $rest = $null
        try {
            $resp = Invoke-FunctionAppCompatRest -Path $path -Method GET
            $rest = $resp.Content | ConvertFrom-Json
        } catch {
            Write-Verbose "Get-FunctionAppCompat: REST site lookup failed for $($app.Name) ($_). Continuing without runtime metadata."
        }
        $results += ConvertTo-FunctionAppCompatObject -WebApp $app -SiteRest $rest
    }
    return $results
}

function Get-FunctionAppSettingCompat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$ResourceGroupName,
        [Parameter(Mandatory = $true)] [string]$Name
    )
    $subId = Get-FunctionAppCompatSubscriptionId
    $path = "/subscriptions/$subId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$Name/config/appsettings/list`?api-version=$script:WebSitesApiVersion"
    $resp = Invoke-FunctionAppCompatRest -Path $path -Method POST
    $body = $resp.Content | ConvertFrom-Json
    $result = @{}
    if ($body -and $body.properties) {
        foreach ($prop in $body.properties.PSObject.Properties) {
            $result[$prop.Name] = $prop.Value
        }
    }
    return $result
}

function Update-FunctionAppSettingCompat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$ResourceGroupName,
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [hashtable]$AppSetting
    )
    # CRITICAL: Az.Functions.Update-AzFunctionAppSetting adds/updates a key
    # without wiping the rest. The REST PUT /config/appsettings replaces the
    # entire dictionary, so we MUST read-modify-write here. Getting this wrong
    # wipes app settings in production.
    $subId = Get-FunctionAppCompatSubscriptionId
    $current = Get-FunctionAppSettingCompat -ResourceGroupName $ResourceGroupName -Name $Name
    foreach ($key in $AppSetting.Keys) {
        $current[$key] = $AppSetting[$key]
    }
    $putPath = "/subscriptions/$subId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$Name/config/appsettings`?api-version=$script:WebSitesApiVersion"
    $payload = @{ properties = $current } | ConvertTo-Json -Depth 10 -Compress
    Invoke-FunctionAppCompatRest -Path $putPath -Method PUT -Payload $payload | Out-Null
    return $current
}

function Stop-FunctionAppCompat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$ResourceGroupName,
        [Parameter(Mandatory = $true)] [string]$Name
    )
    Stop-AzWebApp -ResourceGroupName $ResourceGroupName -Name $Name | Out-Null
}

function Start-FunctionAppCompat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$ResourceGroupName,
        [Parameter(Mandatory = $true)] [string]$Name
    )
    Start-AzWebApp -ResourceGroupName $ResourceGroupName -Name $Name | Out-Null
}

function Restart-FunctionAppCompat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$ResourceGroupName,
        [Parameter(Mandatory = $true)] [string]$Name
    )
    Restart-AzWebApp -ResourceGroupName $ResourceGroupName -Name $Name | Out-Null
}

function Remove-FunctionAppCompat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$ResourceGroupName,
        [Parameter(Mandatory = $true)] [string]$Name
    )
    Remove-AzWebApp -ResourceGroupName $ResourceGroupName -Name $Name -Force | Out-Null
}
