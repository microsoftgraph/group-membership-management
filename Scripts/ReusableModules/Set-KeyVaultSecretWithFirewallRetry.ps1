function Set-KeyVaultSecretWithFirewallRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VaultName,

        [Parameter(Mandatory = $true)]
        [string]$ResourceGroup,

        [Parameter(Mandatory = $true)]
        [string]$SecretName,

        [Parameter(Mandatory = $true)]
        [object]$SecretValue,

        [ValidateRange(1, 10)]
        [int]$MaxRetries = 2
    )

    $directory = $PSScriptRoot
    . ($directory + '\Invoke-WithFirewallRetry.ps1')
    . ($directory + '\Add-KeyVaultIpFromError.ps1')

    Invoke-WithFirewallRetry -ResourceGroup $ResourceGroup -MaxRetries $MaxRetries `
        -Operation {
            # Normalize to SecureString
            if ($SecretValue -is [System.Security.SecureString]) {
                $secureSecret = $SecretValue
            }
            elseif ($SecretValue -is [string]) {
                $secureSecret = New-Object System.Security.SecureString
                $SecretValue.ToString().ToCharArray() | ForEach-Object { $secureSecret.AppendChar($_) }
            }
            else {
                throw "SecretValue must be a [string] or [SecureString]. Got: $($SecretValue.GetType().FullName)"
            }

            Set-AzKeyVaultSecret `
                -VaultName $VaultName `
                -Name $SecretName `
                -SecretValue $secureSecret `
                -ErrorAction Stop

            Write-Host "✅ Secret '$SecretName' set successfully."
            $true   # ensures loop exits
        } `
        -OnFirewallError {
            param($err)
            Add-KeyVaultIpFromError -VaultName $VaultName -ResourceGroup $ResourceGroup -ErrorMessage $err
        }
}