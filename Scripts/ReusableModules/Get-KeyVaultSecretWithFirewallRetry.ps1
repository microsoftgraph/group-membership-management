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

        [switch]$AsPlainText
    )

    $directory = $PSScriptRoot
    . ($directory + '/Invoke-WithFirewallRetry.ps1')
    . ($directory + '/Add-KeyVaultIpFromError.ps1')

    Write-Host "`nRetrieving Key Vault secret '$SecretName' from vault '$VaultName'..."

    Invoke-WithFirewallRetry -ResourceGroup $ResourceGroup -MaxRetries $MaxRetries `
        -Operation {
            try {
                if ($AsPlainText) {
                    $secret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -AsPlainText -ErrorAction Stop
                }
                else {
                    $secret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -ErrorAction Stop
                }

                Write-Host "✅ Secret '$SecretName' retrieved successfully."
                return $secret
            }
            catch {
                if ($_.Exception.Message -match "was not found") {
                    Write-Warning "⚠️ Secret '$SecretName' does not exist in vault '$VaultName'."
                    return $null   # break retry loop immediately
                }
                throw  # let the retry wrapper handle firewall errors
            }
        } `
        -OnFirewallError {
            param($err) 
            Add-KeyVaultIpFromError -VaultName $VaultName -ResourceGroup $ResourceGroup -ErrorMessage $err
        }
}
