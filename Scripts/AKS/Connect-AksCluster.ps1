function Connect-AksCluster {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )

    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $aksName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-aks"

    $result = [pscustomobject]@{
        Available            = $false
        Reason               = $null
        ClusterName          = $aksName
        ComputeResourceGroup = $computeResourceGroup
        TenantId             = $null
    }

    $clusterLookup = Invoke-AzCli -AllowFailure `
        -OperationName "Look up AKS cluster '$aksName'" `
        -Arguments @('aks','show','--resource-group',$computeResourceGroup,'--name',$aksName,'--only-show-errors')

    if ($clusterLookup.ExitCode -ne 0) {
        $lookupError = $clusterLookup.Error
        if ($lookupError -notmatch '\(ResourceNotFound\)') {
            throw "Could not determine whether AKS cluster '$aksName' exists in '$computeResourceGroup'. This is NOT a missing cluster - the lookup itself failed, so the cluster's real state is unknown: $lookupError"
        }
        $result.Reason = "AKS cluster '$aksName' does not exist in '$computeResourceGroup'."
        return $result
    }

    $cluster = $clusterLookup.Output | ConvertFrom-Json

    $powerState = $cluster.PowerState.Code
    if ($powerState -eq 'Stopped') {
        $result.Reason = "AKS cluster '$aksName' is stopped, so no pod can be running on it."
        return $result
    }
    Write-DeployLog -Level Info -Message "AKS cluster '$aksName' power state: $powerState"

    # The cluster sets disableLocalAccounts, so no admin kubeconfig exists;
    # kubelogin converts the kubeconfig to a non-interactive Entra flow.
    Write-DeployLog -Level Info -Message "Getting credentials for cluster '$aksName'"
    Invoke-WithRetry `
        -Operation {
            az aks get-credentials --resource-group $computeResourceGroup --name $aksName --overwrite-existing --only-show-errors
            if ($LASTEXITCODE -ne 0) { throw "az aks get-credentials failed for cluster '$aksName'." }
        } `
        -OperationName "Get AKS credentials" `
        -MaxAttempts 3 -BaseDelaySeconds 2

    kubelogin convert-kubeconfig -l azurecli
    if ($LASTEXITCODE -ne 0) { throw "kubelogin convert-kubeconfig failed." }

    $tenantId = az account show --query tenantId --output tsv --only-show-errors
    if ($LASTEXITCODE -ne 0 -or -not $tenantId) { throw "Could not resolve the tenant id from the current Azure CLI login." }

    $result.TenantId = $tenantId
    $result.Available = $true
    return $result
}
