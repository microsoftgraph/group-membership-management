$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal that can read and update Teams Channel using Graph.
Be aware that running this in VS Code doesn't work for some reason, it works better if you run it in a regular Powershell session.
You may have to open the created Azure AD app in your demo tenant and consent to the permissions!

Basically, this script is designed to create an Azure AD app with the appropriate permissions in a given tenant
(delegated permissions ChannelMember.ReadWrite.All) and write its credentials to a key vault in another tenant.
This should be able to work when the AD app and the target key vault are in the same tenant. Just pass the same tenant ID to both
parameters.

To find the tenant ID for a tenant, you can run Connect-AzAccount in Powershell, or open the Azure portal, click on "Azure Active Directory",
and it should be there.

You'll be promped to sign in twice. First as someone who can create the Azure AD app in the given tenant and assign it permissions,
then as someone who can write to the prereqs key vault in the other. Make sure you set SubscriptionName to the name of the Azure subscription
that contains the key vault.

.PARAMETER SubscriptionName
Subscription Name

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.PARAMETER TenantIdToCreateAppIn
Azure tenant id where the application is going to be created.

.PARAMETER TenantIdWithKeyVault
Azure tenant id where the prereqs keyvault was created.

.PARAMETER CertificateName
Certificate name
Optional

.PARAMETER SaveToKeyVault
When set to true, the application-related secrets will be saved to the key vault.
Optional

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.PARAMETER CreateNewSecret
When set to true, a new application secret will be created and stored. When false, secret creation is skipped.
Optional

.EXAMPLE
# these are arbitrary guids and subscription names, you'll have to change them.
Set-TeamsChannelAzureADApplication	-SubscriptionName "<subscription-name>" `
                                    -SolutionAbbreviation "<solution-abbreviation>" `
                                    -EnvironmentAbbreviation "<environment-abbreviation>" `
                                    -TenantIdToCreateAppIn "<app-tenant-id>" `
                                    -TenantIdWithKeyVault "<keyvault-tenant-id>" `
                                    -SaveToKeyVault $true `
                                    -Clean $false `
                                    -Verbose
#>

function Set-TeamsChannelAzureADApplication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory=$True)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory=$True)]
        [Guid] $AppTenantId,
        [Parameter(Mandatory=$False)]
        [Guid] $KeyVaultTenantId,
		[Parameter(Mandatory=$False)]
        [string] $SubscriptionName,
        [Parameter(Mandatory=$False)]
        [string] $CertificateName,
        [Parameter(Mandatory = $False)]
        [boolean] $SaveToKeyVault = $True,
        [Parameter(Mandatory=$False)]
        [boolean] $Clean = $False,
        [Parameter(Mandatory = $False)]
        [boolean] $SkipIfApplicationExists = $True,
        [Parameter(Mandatory=$False)]
        [boolean] $CreateNewSecret = $True,
        [Parameter(Mandatory=$False)]
        [string] $ErrorActionPreference = $Stop
    )
    Write-Host "`nSet-TeamsChannelAzureADApplication starting...`n"

    # Validate required parameters when SaveToKeyVault is enabled
    if ($SaveToKeyVault -eq $true) {
		if ([string]::IsNullOrWhiteSpace($SubscriptionName)) {
			throw "SubscriptionName parameter is required when SaveToKeyVault is set to true."
		}
		if ([string]::IsNullOrWhiteSpace($KeyVaultTenantId)) {
			throw "KeyVaultTenantId parameter is required when SaveToKeyVault is set to true."
		}
	}

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    if ($global:SkipModuleInstall -ne $true) {
        . ($scriptsDirectory + '/Install-MSGraphIfNeeded.ps1')
        Install-MSGraphIfNeeded

        if ($SaveToKeyVault -eq $true) {
            . ($scriptsDirectory + '/Install-AzModuleIfNeeded.ps1')
            Install-AzModuleIfNeeded
        }
    }

    if ($global:SkipAzLogin -ne $true -and $SaveToKeyVault -eq $true) {
        Connect-AzAccount -Tenant $KeyVaultTenantId
		Set-AzContext -SubscriptionName $SubscriptionName
    }

    if ($global:SkipMsGraphLogin -ne $true) {
        # Disconnect any existing session
        Disconnect-MgGraph -ErrorAction SilentlyContinue 

        $requiredScopes = @(
            "Application.ReadWrite.All", 
            "AppRoleAssignment.ReadWrite.All"
        )
        
        # Connect to Microsoft Graph with required scopes for the target tenant
        Connect-MgGraph -TenantId $AppTenantId -Scopes $requiredScopes
        
        Write-Host "Successfully connected to Microsoft Graph for tenant $AppTenantId"
    }

    #region Delete Application / Service Principal if they already exist
    $teamsChannelAppDisplayName = "$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation"
    $teamsChannelApps = Get-MgApplication -Filter "displayName eq '$teamsChannelAppDisplayName'"
    
    # Validate that we don't have multiple applications with the same name
    if($null -ne $teamsChannelApps -and $teamsChannelApps.Count -gt 1) {
        Write-Error "Found $($teamsChannelApps.Count) applications with the name '$teamsChannelAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
        throw "Multiple applications found with the same display name: $teamsChannelAppDisplayName"
    }
    
    # Convert to single application object if we have exactly one
    $teamsChannelApp = if($null -ne $teamsChannelApps -and $teamsChannelApps.Count -eq 1) { $teamsChannelApps } else { $null }
    $updatedAPIPermissions = $false

    if($null -ne $teamsChannelApp -and $Clean -eq $false -and $SkipIfApplicationExists -eq $true)
    {
        Write-Host "Application $teamsChannelAppDisplayName already exists. Skipping creation..."
        return @{ ApplicationId = $teamsChannelApp.AppId; TenantId = $AppTenantId; ApplicationName = $teamsChannelAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions; }
    }	

    if($Clean -eq $true -and $null -ne $teamsChannelApp)
    {
        $displayName = $teamsChannelApp.DisplayName;
        $objectId = $teamsChannelApp.Id;
        try {
            Remove-MgApplication -ApplicationId $objectId
            Write-Host "Removed $displayName..." -ForegroundColor Green;
            $teamsChannelApp = $null
        }
        catch {
            Write-Host "Failed to remove $displayName..." -ForegroundColor Red;
            throw
        }
    }
    #endregion

    # These are the function apps that need to interact with the TeamsChannel.
    $replyUrls = @("teamschannelupdater", "teamschannelmembershipobtainer") |
        ForEach-Object { "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$_.azurewebsites.net"};
    $replyUrls += "http://localhost";

    #region Create Application
    if($null -eq $teamsChannelApp)
    {
        Write-Host "Creating Azure AD app $teamsChannelAppDisplayName"

        $appCreationParameters = New-TeamsChannelValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation

        # Create application body for Microsoft Graph
        $teamsChannelApp = New-MgApplication -BodyParameter $appCreationParameters
        $updatedAPIPermissions = $true
        
        New-MgServicePrincipal -AppId $teamsChannelApp.AppId

        # Update with identifier URI
        $updatedAppParameters = New-TeamsChannelValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -AppId $teamsChannelApp.AppId

        Update-MgApplication -ApplicationId $teamsChannelApp.Id -BodyParameter $updatedAppParameters
        Write-Host "Created Azure AD app $teamsChannelAppDisplayName"
    }
    else
    {
        Write-Host "Azure AD app $teamsChannelAppDisplayName already exists."
        Write-Host "Checking if app needs update..."

        $expectedAppConfig = New-TeamsChannelValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -AppId $teamsChannelApp.AppId

        . ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
        $needsUpdate = -not (Test-AppMatchesConfiguration -AppObject $teamsChannelApp -ExpectedConfiguration $expectedAppConfig)
		
        if ($needsUpdate) {
            Write-Host "App $teamsChannelAppDisplayName needs update. Updating..."
            Update-MgApplication -ApplicationId $teamsChannelApp.Id -BodyParameter $expectedAppConfig
            $updatedAPIPermissions = $true
            Write-Host "Finished updating Azure AD app $teamsChannelAppDisplayName"
        }
        else {
            Write-Host "No update needed for app $teamsChannelAppDisplayName."
        }
    }

    if ($updatedAPIPermissions -eq $true) {
        Write-Host "Waiting 15 seconds for Azure AD replication..."
        Start-Sleep -Seconds 15
        Write-Host "Done waiting for Azure AD replication."
    }

    if($SaveToKeyVault -eq $true)
    {
		Set-TeamsChannelAppKeyVaultSecrets `
			-SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-AppTenantId $AppTenantId `
			-ApplicationClientId $teamsChannelApp.AppId `
			-CertificateName $CertificateName `
			-CreateNewSecret $CreateNewSecret
    }

    # Disconnect from Microsoft Graph before returning
    if ($global:SkipMsGraphLogin -ne $true) {
		Disconnect-MgGraph -ErrorAction SilentlyContinue

		Write-Host "Disconnected from Microsoft Graph." -ForegroundColor Green
	}

    Write-Host "`nSet-TeamsChannelAzureADApplication completed.`n"
	return @{ ApplicationId = $teamsChannelApp.AppId; TenantId = $AppTenantId; ApplicationName = $teamsChannelAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions;}
}

function New-TeamsChannelValidationConfiguration {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string] $SolutionAbbreviation,
		
		[Parameter(Mandatory = $true)]
		[string] $EnvironmentAbbreviation,
		
		[AllowNull()]
		[Parameter(Mandatory = $false)]
		[string] $AppId  # If provided, will be used for identifier URI
	)
	
	$teamsChannelAppDisplayName = "$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation"
	
	# These are the function apps that need to interact with the TeamsChannel
	$replyUrls = @("teamschannelupdater", "teamschannelmembershipobtainer") |
		ForEach-Object { "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$_.azurewebsites.net" }
	$replyUrls += "http://localhost"
	
	# Get Microsoft Graph service principal to retrieve current permission IDs
	$mgGraphServicePrincipal = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"
	
	$delegatedPermissions = $mgGraphServicePrincipal.Oauth2PermissionScopes `
		| Where-Object { ($_.Value -eq "ChannelMember.ReadWrite.All") -or ($_.Value -eq "Channel.ReadBasic.All") } `
		| ForEach-Object { @{Id = $_.Id; Type = "Scope" } }

	$requiredResourceAccess = @{
		ResourceAppId = "00000003-0000-0000-c000-000000000000"
		ResourceAccess = $delegatedPermissions
	}
	
	$config = @{
		displayName            = $teamsChannelAppDisplayName
		signInAudience         = "AzureADMyOrg"
		requiredResourceAccess = @($requiredResourceAccess)
		isFallbackPublicClient = $true
		web                    = @{
			redirectUris = $replyUrls
			implicitGrantSettings = @{
				enableAccessTokenIssuance = $true
				enableIdTokenIssuance     = $true
			}
		}
	}
	
	# Add identifier URIs if AppId is provided
	if (-not [string]::IsNullOrWhiteSpace($AppId)) {
		$config.identifierUris = @("api://$AppId")
	} else {
		$config.identifierUris = @()  # Empty array for initial creation
	}
	
	return $config
}


function Set-TeamsChannelAppKeyVaultSecrets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory=$True)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory=$True)]
        [Guid] $AppTenantId,
        [Parameter(Mandatory=$True)]
        [Guid] $ApplicationClientId,
        [Parameter(Mandatory=$False)]
        [string] $CertificateName,
		[AllowNull()]
		[Parameter(Mandatory = $False)]
		[string] $AppSecret = $null,
        [Parameter(Mandatory=$False)]
        [boolean] $CreateNewSecret = $True
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')
    . ($scriptsDirectory + '/ReusableModules/Set-KeyVaultSecretWithFirewallRetry.ps1')

    # These need to go into the key vault
    $teamsChannelAppTenantId = $AppTenantId;
    $teamsChannelAppClientId = $ApplicationClientId;
    $teamsChannelAppDisplayName = "$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation" 

    # Create new secret if requested
    $teamsChannelAppClientSecret = $AppSecret
    if ($CreateNewSecret -eq $true) {
        $endDate = [System.DateTime]::Now.AddYears(1)
        $passwordCredential = @{
            displayName = "GMM Generated Secret"
            startDateTime = [System.DateTime]::Now
            endDateTime = $endDate
        }
        $appObjectId = (Get-MgApplication -Filter "appId eq '$teamsChannelAppClientId'").Id
        $teamsChannelAppClientSecret = (Add-MgApplicationPassword -ApplicationId $appObjectId -PasswordCredential $passwordCredential).SecretText
        Write-Host "Created new application secret for app $teamsChannelAppClientId"
    } else {
        Write-Host "Skipping secret creation as CreateNewSecret is set to false"
    }

    $keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
    {
        throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
    }

    # Store Application (client) ID in KeyVault
    $teamsClientIdKeyVaultSecretName = "teamsChannelAppClientId"

    $teamsClientIdSecret = New-Object System.Security.SecureString
    $teamsChannelAppClientId.ToString().ToCharArray() | ForEach-Object { $teamsClientIdSecret.AppendChar($_) }

    Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
                                        -ResourceGroup $keyVault.ResourceGroupName `
                                        -SecretName $teamsClientIdKeyVaultSecretName `
                                        -SecretValue $teamsClientIdSecret

    Write-Host "$teamsClientIdKeyVaultSecretName added to vault for $teamsChannelAppDisplayName."

    # Store Application secret in KeyVault (only if a new secret was created)
    if (-not [string]::IsNullOrEmpty($teamsChannelAppClientSecret)) {
        $teamsChannelAppClientSecretName = "teamsChannelAppClientSecret"

		Write-Host "Storing TeamsChannel application client secret in KeyVault"
        $teamsClientSecret = New-Object System.Security.SecureString
        $teamsChannelAppClientSecret.ToCharArray() | ForEach-Object { $teamsClientSecret.AppendChar($_) }

        Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
                                        -ResourceGroup $keyVault.ResourceGroupName `
                                        -SecretName $teamsChannelAppClientSecretName `
                                        -SecretValue $teamsClientSecret

        Write-Host "$teamsChannelAppClientSecretName added to vault for $teamsChannelAppDisplayName."
    } else {
        Write-Host "Skipping application secret storage as no new secret was created"
    }

    # Store tenantID in KeyVault
    $teamsTenantSecretName = "teamsChannelAppTenantId"

    $teamsTenantSecret = New-Object System.Security.SecureString
    $teamsChannelAppTenantId.ToString().ToCharArray() | ForEach-Object { $teamsTenantSecret.AppendChar($_) }

    Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
                                        -ResourceGroup $keyVault.ResourceGroupName `
                                        -SecretName $teamsTenantSecretName `
                                        -SecretValue $teamsTenantSecret

    Write-Host "$teamsTenantSecretName added to vault for $teamsChannelAppDisplayName."

    # Store certificate name in KeyVault
    $teamsChannelAppCertificateName = "teamsChannelAppCertificateName"
    $teamsChannelAppCertificate = Get-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName -ResourceGroup $keyVault.ResourceGroupName -SecretName $teamsChannelAppCertificateName -AsPlainText
    $setteamsChannelAppCertificate = $false

    if(!$teamsChannelAppCertificate -and !$CertificateName){
        $CertificateName = "not-set"
        $setteamsChannelAppCertificate = $true
    } elseif ($CertificateName) {
        $setteamsChannelAppCertificate = $true
    }

    if($setteamsChannelAppCertificate){
        Write-Host "Certificate name is $CertificateName"
        $teamsChannelAppCertificateSecret = New-Object System.Security.SecureString
        $CertificateName.ToCharArray() | ForEach-Object { $teamsChannelAppCertificateSecret.AppendChar($_) }

        Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
                                            -ResourceGroup $keyVault.ResourceGroupName `
                                            -SecretName $teamsChannelAppCertificateName `
                                            -SecretValue $teamsChannelAppCertificateSecret
        Write-Host "$teamsChannelAppCertificateName added to vault for $teamsChannelAppDisplayName."
    }

    Write-Host "Set-TeamsChannelAzureADApplication completed."
}

function Test-TeamsChannelApplication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $SolutionAbbreviation,
        
        [Parameter(Mandatory = $true)]
        [string] $EnvironmentAbbreviation
    )
    
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
    
    $teamsChannelAppDisplayName = "$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation"
    
    Write-Host "`n=== Validating Application: $teamsChannelAppDisplayName ===" -ForegroundColor Cyan
    
    # Step 1: Check if application exists
    Write-Host "`n[1/3] Checking if application exists..." -ForegroundColor Yellow
    $teamsChannelApps = Get-MgApplication -Filter "displayName eq '$teamsChannelAppDisplayName'" -All
    
    if ($null -eq $teamsChannelApps -or $teamsChannelApps.Count -eq 0) {
        $errorMessage = "Application '$teamsChannelAppDisplayName' does not exist. Please run the setup script to create it or follow manual setup steps in the documentation."
        Write-Host "❌ $errorMessage" -ForegroundColor Red
        return $false
    }
    
    Write-Host "✅ Application exists." -ForegroundColor Green
    
    # Step 2: Validate uniqueness
    Write-Host "`n[2/3] Validating uniqueness..." -ForegroundColor Yellow
    if ($teamsChannelApps.Count -gt 1) {
        $errorMessage = "Found $($teamsChannelApps.Count) applications with the name '$teamsChannelAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
        Write-Host "❌ $errorMessage" -ForegroundColor Red
        return $false
    }
    
    $teamsChannelApp = $teamsChannelApps
    Write-Host "   Application ID: $($teamsChannelApp.AppId)" -ForegroundColor Gray
    Write-Host "   Object ID: $($teamsChannelApp.Id)" -ForegroundColor Gray
    
    # Step 3: Validate configuration
    Write-Host "`n[3/3] Validating configuration..." -ForegroundColor Yellow
    $expectedAppConfig = New-TeamsChannelValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppId $teamsChannelApp.AppId
    
    $configurationMatches = Test-AppMatchesConfiguration -AppObject $teamsChannelApp `
        -ExpectedConfiguration $expectedAppConfig `
        -ShowDetailedReport
    
    if ($configurationMatches) {
        Write-Host "✅ Configuration matches expected values." -ForegroundColor Green
    } else {
        Write-Host "❌ Configuration does not match expected values. Please review the configuration settings and make updates as needed." -ForegroundColor Red
        return $false
    }

    Write-Host "`n=== Application Validation Completed Successfully ===" -ForegroundColor Cyan
    
    return $true
}