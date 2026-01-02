function Get-KeyVaultSecretWithFirewallRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VaultName,

        [Parameter(Mandatory = $true)]
        [string]$ResourceGroup,

        [Parameter(Mandatory = $true)]
        [string]$SecretName,

        [ValidateRange(1, 10)]
        [int]$MaxRetries = 10,

        [switch]$AsPlainText,

        [switch]$ErrorOnNotFound
    )

    $directory = $PSScriptRoot
    . ($directory + '/Invoke-WithFirewallRetry.ps1')
    . ($directory + '/Add-KeyVaultIpFromError.ps1')

    Write-Host "`nRetrieving Key Vault secret '$SecretName' from vault '$VaultName'..."

    $result = Invoke-WithFirewallRetry -ResourceGroup $ResourceGroup -MaxRetries $MaxRetries `
        -Operation {
            try {
                if ($AsPlainText) {
                    $secret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -AsPlainText -ErrorAction Stop
                }
                else {
                    $secret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -ErrorAction Stop
                }
                return $secret
            }
            catch {
                if ($_.Exception.Message -match "was not found") {
                    if ($ErrorOnNotFound) {
                        Write-Host "Secret '$SecretName' does not exist in vault '$VaultName'." -ForegroundColor Red
                        return $null   # break retry loop immediately
                    }
                    else {
                        Write-Host "ℹ️ Secret '$SecretName' not found in vault '$VaultName'. This is expected."
                        return $null   # break retry loop immediately
                    }
                }
                throw  # let the retry wrapper handle firewall errors
            }
        } `
        -OnFirewallError {
            param($err) 
            Add-KeyVaultIpFromError -VaultName $VaultName -ResourceGroup $ResourceGroup -ErrorMessage $err
        }

    if ($null -eq $result -and $ErrorOnNotFound) {
        throw "Secret '$SecretName' does not exist in vault '$VaultName'."
    }   

    if ($null -ne $result) {
        Write-Host "✅ Secret '$SecretName' retrieved successfully."
    }

    return $result
}
