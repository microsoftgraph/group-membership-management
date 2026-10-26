function Resolve-AksServiceChart {
    <#
    .SYNOPSIS
      Reads one service chart's scaling model from its Chart.yaml.
    #>
    param (
        [Parameter(Mandatory = $true)]
        [string]$DeploymentPackagePath,
        [Parameter(Mandatory = $true)]
        [string]$ChartName
    )

    $chartFile = Join-Path $DeploymentPackagePath "AKS/charts/$ChartName/Chart.yaml"
    if (-not (Microsoft.PowerShell.Management\Test-Path $chartFile)) {
        throw "Chart '$ChartName' was not found at '$chartFile'."
    }
    $chart = Get-Content -Path $chartFile -Raw
    $scaling = [regex]::Match($chart, '(?m)^\s+gmm\.microsoft\.com/scaling:\s*"?([a-z]+)"?\s*$').Groups[1].Value
    if ($scaling -notin @('keda', 'fixed')) { throw "Chart '$ChartName' must set gmm.microsoft.com/scaling to 'keda' or 'fixed'." }
    return [pscustomobject]@{ Autoscaled = $scaling -eq 'keda' }
}

function Resolve-AksHostedServices {
    <#
    .SYNOPSIS
      Resolves the aksServices deployment parameter into the services that run on
      AKS, with JobTrigger last.

    .DESCRIPTION
      The aksServices parameter (GMM service names, e.g. JobTrigger) decides which
      services run on AKS; every deployment step (identities, SQL users, App
      Service skipping and fencing, install, start) uses the result. Each name must
      have a chart in the deployment package, which supplies its scaling model.

      JobTrigger dispatches work to every other service, so it is always started
      last.

    .PARAMETER Names
      The aksServices parameter value.

    .PARAMETER DeploymentPackagePath
      Directory containing AKS/charts.
    #>
    param (
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Names,
        [Parameter(Mandatory = $true)]
        [string]$DeploymentPackagePath
    )

    if ($Names.Count -eq 0) { throw 'The aksServices parameter lists no services; it is required when deployAks is true.' }

    $services = @(foreach ($name in $Names) {
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw 'The aksServices parameter contains a null, empty, or whitespace-only service name.'
        }
        if ($name -cne $name.Trim()) {
            throw "aksServices entry '$name' contains leading or trailing whitespace."
        }
        $chart = Resolve-AksServiceChart -DeploymentPackagePath $DeploymentPackagePath -ChartName $name.ToLowerInvariant()
        [pscustomobject]@{ Name = $name; Autoscaled = $chart.Autoscaled }
    })
    return @($services | Sort-Object @{ Expression = { $_.Name.Equals('JobTrigger', [StringComparison]::OrdinalIgnoreCase) } }, Name)
}
