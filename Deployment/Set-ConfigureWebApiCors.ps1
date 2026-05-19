$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Configure CORS allowed origins for the WebAPI App Service and the SignalR
service from the deployed UI Static Web App's hostnames.

.DESCRIPTION
Queries the UI Static Web App for its default hostname and (if present) its
custom domain, then writes those HTTPS origins to:

  - The WebAPI App Service's `siteConfig.cors.allowedOrigins`
    (preserving any pre-existing values).
  - The SignalR service's `AllowedOrigin`.

This is the canonical mechanism by which non-development environments
populate the App Service "CORS" blade. The WebAPI's `Program.cs` deliberately
does NOT register `app.UseCors(...)` outside Development — Microsoft's
guidance is that App Service CORS and ASP.NET Core CORS must not be combined.

Designed to be invokable from an ADO pipeline that does NOT have Microsoft
Graph permissions: the function only touches Azure resources (App Service,
SignalR), never directory objects.

.PARAMETER SolutionAbbreviation
Solution abbreviation (typically `gmm`). Combined with the environment
abbreviation to derive resource group and resource names.

.PARAMETER EnvironmentAbbreviation
Environment abbreviation (e.g. `int`, `ua`, `prodv2`).

.PARAMETER StaticWebAppName
Optional. Name of the UI Static Web App. Defaults to
`<SolutionAbbreviation>-ui`. Override when the UI was deployed under a
non-default name in this environment.

.OUTPUTS
System.String[] — the list of HTTPS origins that were applied.

.EXAMPLE
. ./Set-ConfigureWebApiCors.ps1
Set-ConfigureWebApiCors `
    -SolutionAbbreviation "gmm" `
    -EnvironmentAbbreviation "int" `
    -Verbose

.NOTES
Requires the `Az.Websites`, `Az.SignalR`, and `Az.Resources` modules. The
caller is expected to be already authenticated via `Connect-AzAccount` (or
running inside an `AzurePowerShell@5` task with the right service
connection).
#>

# Bring in the shared transient-retry helper used elsewhere in the repo.
. (Join-Path $PSScriptRoot '..\Scripts\ReusableModules\Invoke-WithRetry.ps1')

function Set-ConfigureWebApiCors {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,

        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$StaticWebAppName
    )

    Write-Host "`nConfiguring WebAPI and SignalR CORS..." -ForegroundColor Cyan

    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $webApiName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi"
    $uiWebAppName = if ([string]::IsNullOrWhiteSpace($StaticWebAppName)) {
        "$SolutionAbbreviation-ui"
    } else {
        $StaticWebAppName
    }

    # Build the set of allowed origins from the UI Static Web App's hostnames.
    $allowedOrigins = @()

    try {
        $customDomains = Invoke-WithRetry `
            -Operation { Get-AzStaticWebAppCustomDomain -Name $uiWebAppName -ResourceGroupName $computeResourceGroup } `
            -OperationName "Get static web app custom domains" `
            -MaxAttempts 3 -BaseDelaySeconds 2

        foreach ($customDomain in @($customDomains)) {
            if ($null -ne $customDomain -and -not [string]::IsNullOrWhiteSpace($customDomain.DomainName)) {
                $allowedOrigins += "https://$($customDomain.DomainName)"
            }
        }
    }
    catch {
        Write-Output "No custom domain associated with this web app."
    }

    try {
        $staticWebApp = Invoke-WithRetry `
            -Operation { Get-AzStaticWebApp -Name $uiWebAppName -ResourceGroupName $computeResourceGroup } `
            -OperationName "Get static web app" `
            -MaxAttempts 3 -BaseDelaySeconds 2
    }
    catch {
        Write-Warning "Failed to fetch UI Static Web App '$uiWebAppName' in resource group '$computeResourceGroup': $($_.Exception.Message). Skipping CORS configuration; deployment will continue."
        return ,$allowedOrigins
    }

    if ($null -eq $staticWebApp -or [string]::IsNullOrWhiteSpace($staticWebApp.DefaultHostname)) {
        Write-Warning "Static Web App '$uiWebAppName' was not found or has no DefaultHostname in resource group '$computeResourceGroup'. Skipping CORS configuration for this run; deployment will continue."
        return ,$allowedOrigins
    }

    $allowedOrigins += "https://$($staticWebApp.DefaultHostname)"

    # Set CORS for the SignalR service.
    try {
        $null = Update-AzSignalR `
            -ResourceGroupName $computeResourceGroup `
            -Name "$computeResourceGroup-signalr" `
            -AllowedOrigin $allowedOrigins

        Write-Host "SignalR service CORS settings updated successfully" -ForegroundColor Green
    }
    catch {
        Write-Output "Unable to update SignalR service CORS settings."
    }

    # Set CORS for the WebAPI App Service.
    #
    # Strategy: snapshot the existing cors object, compute the desired union of
    # origins, and only issue a Set-AzResource if the set actually changed. This
    # keeps the operation idempotent (no spurious writes on every deploy) and
    # preserves any pre-existing fields we don't explicitly manage
    # (supportCredentials, future Azure CORS settings, etc.) by modifying the
    # existing cors object in place instead of replacing it wholesale.
    #
    # The entire block is wrapped in try/catch so a missing WebAPI, an Azure
    # API blip, or an RBAC failure never fails the deployment — CORS misconfig
    # is immediately visible to anyone hitting the UI and the operator can
    # repair via the App Service portal blade.
    try {
        $webApi = Invoke-WithRetry `
            -Operation { Get-AzWebApp -ResourceGroupName $computeResourceGroup -Name $webApiName } `
            -OperationName "Get WebAPI for CORS" `
            -MaxAttempts 3 -BaseDelaySeconds 2

        if ($null -eq $webApi) {
            Write-Warning "WebAPI App Service '$webApiName' was not found in resource group '$computeResourceGroup'. Skipping CORS configuration; deployment will continue."
            return ,$allowedOrigins
        }

        $currentCORs = @()
        if ($null -ne $webApi.SiteConfig.Cors -and $null -ne $webApi.SiteConfig.Cors.AllowedOrigins) {
            $currentCORs = @($webApi.SiteConfig.Cors.AllowedOrigins)
        }

        # Build the desired final list: new origins first, then any pre-existing
        # entries not already in the list. De-dupes both directions.
        $desiredCORs = @()
        foreach ($origin in $allowedOrigins) {
            if ($desiredCORs -notcontains $origin) {
                $desiredCORs += $origin
            }
        }
        foreach ($origin in $currentCORs) {
            if ($desiredCORs -notcontains $origin) {
                $desiredCORs += $origin
            }
        }

        # Determine if the resulting set differs from what's already on the WebAPI.
        $needsUpdate = $false
        if ($desiredCORs.Count -ne $currentCORs.Count) {
            $needsUpdate = $true
        }
        else {
            foreach ($origin in $desiredCORs) {
                if ($currentCORs -notcontains $origin) {
                    $needsUpdate = $true
                    break
                }
            }
        }

        if (-not $needsUpdate) {
            Write-Host "WebAPI CORS already up to date ($($currentCORs.Count) origin(s)); skipping write." -ForegroundColor Gray
        }
        else {
            $apiResourceParams = @{
                ResourceName      = $webApiName
                ResourceType      = "Microsoft.Web/sites"
                ResourceGroupName = $computeResourceGroup
            }

            $webApiResource = Invoke-WithRetry `
                -Operation { Get-AzResource @apiResourceParams } `
                -OperationName "Get WebAPI resource for CORS" `
                -MaxAttempts 3 -BaseDelaySeconds 2

            # Mutate the existing cors object in place so supportCredentials and
            # any other fields survive. If no cors block exists yet, create one
            # with supportCredentials defaulted to $true to match appService.bicep.
            if ($null -eq $webApiResource.Properties.siteConfig.cors) {
                $webApiResource.Properties.siteConfig.cors = @{
                    allowedOrigins     = $desiredCORs
                    supportCredentials = $true
                }
            }
            else {
                $webApiResource.Properties.siteConfig.cors.allowedOrigins = $desiredCORs
            }

            $null = Invoke-WithRetry `
                -Operation { $webApiResource | Set-AzResource -Force } `
                -OperationName "Update WebAPI CORS settings" `
                -MaxAttempts 3 -BaseDelaySeconds 2

            Write-Host "WebAPI CORS settings updated successfully ($($desiredCORs.Count) origin(s))." -ForegroundColor Green
        }
    }
    catch {
        Write-Warning "Failed to configure WebAPI CORS: $($_.Exception.Message). Deployment will continue; the operator can repair CORS via the App Service portal blade if needed."
    }

    # Unary comma prevents PowerShell from unrolling a single-element array
    # when the caller assigns the return value.
    return ,$allowedOrigins
}
