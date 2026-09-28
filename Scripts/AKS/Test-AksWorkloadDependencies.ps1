function Test-AksWorkloadDependencies {

    Write-DeployPhase -Name 'Verifying AKS Workload Dependencies' -Event Begin

    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw @"
The Azure CLI ('az') is required to deploy the AKS workload and was not found on PATH.

It is needed for `az acr build`, which builds the container image inside Azure
Container Registry. That is a server-side registry task, so it needs no local
container engine - and it has no Azure PowerShell equivalent. The workload
deployment also obtains its cluster token through the CLI, so kubectl and helm
depend on it too.

Install it from https://learn.microsoft.com/cli/azure/install-azure-cli and sign in
with `az login`, then re-run. To deploy without AKS instead, set deployAks to false.
"@
    }
    Write-DeployLog -Level Success -Message "Azure CLI found: $((Get-Command az).Source)"

    if (-not (Get-Command helm -ErrorAction SilentlyContinue)) {
        throw @"
'helm' is required to deploy the AKS workload and was not found on PATH.

Helm is not a PowerShell module and cannot be installed by this script. Install it
from https://helm.sh/docs/intro/install/ and re-run. To deploy without AKS instead,
set deployAks to false.
"@
    }
    Write-DeployLog -Level Success -Message "helm found: $((Get-Command helm).Source)"

    $minimumHelmMajorVersion = 4
    $helmVersion = (helm version --template '{{.Version}}' 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($helmVersion)) {
        throw "Could not determine the installed helm version: 'helm version --template ''{{.Version}}''' exited with code $LASTEXITCODE and returned '$helmVersion'. The AKS deployment requires helm $minimumHelmMajorVersion or later, and continuing without confirming the version risks failing partway through the deployment. Verify the helm installation at $((Get-Command helm).Source) and re-run."
    }
    if ($helmVersion -notmatch '^v?(\d+)\.(\d+)') {
        throw "helm reported an unrecognised version '$helmVersion'. The AKS deployment requires helm $minimumHelmMajorVersion or later and cannot confirm that from this value. Verify the helm installation at $((Get-Command helm).Source) and re-run."
    }

    $helmMajorVersion = [int]$Matches[1]
    if ($helmMajorVersion -lt $minimumHelmMajorVersion) {
        throw "helm $helmVersion is installed, but helm $minimumHelmMajorVersion or later is required. The AKS deployment passes --force-conflicts to 'helm upgrade', which helm $helmMajorVersion does not support, so the deployment would fail partway through with 'unknown flag: --force-conflicts'. Install helm $minimumHelmMajorVersion or later from https://helm.sh/docs/intro/install/ and re-run."
    }
    Write-DeployLog -Level Success -Message "helm version $helmVersion satisfies the minimum major version of $minimumHelmMajorVersion."

    $ToolDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'gmm-aks-tools'
    New-Item -ItemType Directory -Force -Path $ToolDirectory | Out-Null

    $exeSuffix = if ($IsWindows) { '.exe' } else { '' }
    $kubectlPath = Join-Path $ToolDirectory "kubectl$exeSuffix"
    $kubeloginPath = Join-Path $ToolDirectory "kubelogin$exeSuffix"

    if (-not (Get-Command kubectl -ErrorAction SilentlyContinue) -or -not (Get-Command kubelogin -ErrorAction SilentlyContinue)) {
        Write-DeployLog -Level Info -Message "Installing kubectl and kubelogin to $ToolDirectory"
        az aks install-cli --install-location $kubectlPath --kubelogin-install-location $kubeloginPath --only-show-errors
        if ($LASTEXITCODE -ne 0) { throw "az aks install-cli failed." }
        $env:PATH = "$ToolDirectory$([System.IO.Path]::PathSeparator)$env:PATH"
    }

    foreach ($tool in @('kubectl', 'kubelogin', 'helm', 'az')) {
        $resolved = Get-Command $tool -ErrorAction SilentlyContinue
        if (-not $resolved) {
            throw "'$tool' is not on PATH after dependency setup. kubectl and kubelogin are installed to $ToolDirectory by 'az aks install-cli'; helm and az must already be installed."
        }
        Write-DeployLog -Level Success -Message "$tool : $($resolved.Source)"
    }

    Write-DeployPhase -Name 'Verifying AKS Workload Dependencies' -Event End
}
