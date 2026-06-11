function Add-KeyVaultIpFromError {
    param (
        [string]$VaultName,
        [string]$ResourceGroup,
        [string]$ErrorMessage
    )

    $messageContainsIpAddress = $ErrorMessage -match "\b((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})\b"
    if ($messageContainsIpAddress) {
        $ipToAdd = $matches[1]
        Write-Verbose "Extracted IP: $ipToAdd"

        $kv = Get-AzKeyVault -VaultName $VaultName -ResourceGroupName $ResourceGroup
        $existingIps = $kv.NetworkAcls.IpAddressRanges

        if ($existingIps -notcontains $ipToAdd) {
            $updatedIps = $existingIps + $ipToAdd

            Update-AzKeyVaultNetworkRuleSet -VaultName $VaultName `
                -ResourceGroupName $ResourceGroup `
                -IpAddressRange $updatedIps `
                -DefaultAction Deny

            Start-Sleep -Seconds 10

            Write-Verbose "✅ IP $ipToAdd added to Key Vault firewall rules."
        }
        else {
            Write-Verbose "ℹ️ IP $ipToAdd is already in the allowed list."
        }
    }
    else {
        Write-Verbose "⚠️ No IP address found in the error message."
    }
}