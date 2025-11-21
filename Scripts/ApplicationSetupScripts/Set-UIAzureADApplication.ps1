$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal for UI.
Be aware that running this in VS Code doesn't work for some reason, it works better if you run it in a regular Powershell session.
You may have to open the created Azure AD app in your demo tenant and consent to the permissions!

Basically, this script is designed to create an Azure AD app and write its credentials to a key vault in another tenant.
This should be able to work when the AD app and the target key vault are in the same tenant. Just pass the same tenant ID to both
parameters.

To find the tenant ID for a tenant, you can run Connect-AzAccount in Powershell, or open the Azure portal, click on "Microsoft Entra ID",
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

.PARAMETER TenantId
Azure tenant id where keyvaults exists.
The application is going to be created in this tenant and its settings stored in the data keyvault.

.PARAMETER DevTenantId
If you are testing GMM using a dev tenant, but your Azure Resources exist in a Subscription tied to your organization's tenant, you will need to provide both of these tenant ids.
If you are deploying everything in your organization's tenant, you do not need to provide this value.


.PARAMETER SaveToKeyVault
When set to true, the application-related secrets will be saved to the key vault.
Optional

.PARAMETER SkipIfApplicationExists
When set to true, the script will skip application creation if it already exists.
Optional

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.PARAMETER CreateNewSecret
When set to true, a new application secret will be created and stored. When false, secret creation is skipped.
Optional

.EXAMPLE
# these are arbitrary guids and subscription names, you'll have to change them.
Set-UIAzureADApplication	-SubscriptionName "<subscription-name>" `
                            -SolutionAbbreviation "<solution-abbreviation>" `
                            -EnvironmentAbbreviation "<environment-abbreviation>" `
                            -TenantId "<tenant-id>" `
                            -DevTenantId "<dev-tenant-id>" `
                            -TenantDomain "<tenant-domain>" `
                            -SharepointDomain "<sharepoint-domain>" `
                            -Clean $false `
                            -Verbose
#>

function Set-UIAzureADApplication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $True)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $True)]
        [Guid] $AppTenantId,
        [Parameter(Mandatory = $False)]
        [Guid] $KeyVaultTenantId,
		[Parameter(Mandatory = $False)]
        [string] $SubscriptionName,
		[AllowNull()]
        [Parameter(Mandatory = $False)]
        [string] $TenantDomain,
		[AllowNull()]
        [Parameter(Mandatory = $False)]
        [string] $SharepointDomain,
        [Parameter(Mandatory = $False)]
        [boolean] $Clean = $False,
        [Parameter(Mandatory = $False)]
        [boolean] $SaveToKeyVault = $True,
        [Parameter(Mandatory = $False)]
        [boolean] $SkipIfApplicationExists = $True,
        [Parameter(Mandatory = $False)]
        [boolean] $CreateNewSecret = $True,
        [Parameter(Mandatory = $False)]
        [string] $ErrorActionPreference = $Stop
    )
    Write-Host "Set-UIAzureADApplication starting..."

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
    $uiAppDisplayName = "$SolutionAbbreviation-ui-$EnvironmentAbbreviation"
    $uiApps = Get-MgApplication -Filter "displayName eq '$uiAppDisplayName'"
    
    # Validate that we don't have multiple applications with the same name
    if($null -ne $uiApps -and $uiApps.Count -gt 1) {
        Write-Error "Found $($uiApps.Count) applications with the name '$uiAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
        throw "Multiple applications found with the same display name: $uiAppDisplayName"
    }
    
    # Convert to single application object if we have exactly one
    $uiApp = if($null -ne $uiApps -and $uiApps.Count -eq 1) { $uiApps } else { $null }
    $updatedAPIPermissions = $false

    if ($null -ne $uiApp -and $SkipIfApplicationExists -eq $true -and $Clean -eq $false) {
        Write-Host "Application $uiAppDisplayName already exists. Skipping creation..."
        return @{ ApplicationId = $uiApp.AppId; TenantId = $AppTenantId; ApplicationName = $uiAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions;}
    }

    if ($Clean -eq $true -and $null -ne $uiApp) {
        $displayName = $uiApp.DisplayName;
        $objectId = $uiApp.Id;
        try {
            Remove-MgApplication -ApplicationId $objectId
            Write-Host "Removed $displayName..." -ForegroundColor Green;
            $uiApp = $null
        }
        catch {
            Write-Host "Failed to remove $displayName..." -ForegroundColor Red;
            throw
        }
    }
    #endregion

    #region Create Application
    if ($null -eq $uiApp) {
        Write-Host "Creating Azure AD app $uiAppDisplayName"

        $appCreationParameters = New-UIValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation

        # Create application body for Microsoft Graph
        $uiApp = New-MgApplication -BodyParameter $appCreationParameters
        $updatedAPIPermissions = $true
        
        New-MgServicePrincipal -AppId $uiApp.AppId

        # Update with identifier URI
        $updatedAppParameters = New-UIValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -AppId $uiApp.AppId

        Update-MgApplication -ApplicationId $uiApp.Id -BodyParameter $updatedAppParameters
        Write-Host "Created Azure AD app $uiAppDisplayName"
    }
    else {
        Write-Host "Azure AD app $uiAppDisplayName already exists."
        Write-Host "Checking if app needs update..."

        $expectedAppConfig = New-UIValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -AppId $uiApp.AppId

        . ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
        $needsUpdate = -not (Test-AppMatchesConfiguration -AppObject $uiApp -ExpectedConfiguration $expectedAppConfig)
		return
        if ($needsUpdate) {
            Write-Host "App $uiAppDisplayName needs update. Updating..."
            Update-MgApplication -ApplicationId $uiApp.Id -BodyParameter $expectedAppConfig
            $updatedAPIPermissions = $true
            Write-Host "Finished updating Azure AD app $uiAppDisplayName"
        }
        else {
            Write-Host "No update needed for app $uiAppDisplayName."
        }
    }

    if ($updatedAPIPermissions -eq $true) {
        Write-Host "Waiting 15 seconds for Azure AD replication..."
        Start-Sleep -Seconds 15
        Write-Host "Done waiting for Azure AD replication."
    }

	if ($SaveToKeyVault -eq $true) {
		Set-UIKeyVaultSecrets `
			-SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-AppTenantId $AppTenantId `
			-UIApplicationId $uiApp.AppId `
			-TenantDomain $TenantDomain `
			-SharepointDomain $SharepointDomain `
			-CreateNewSecret $CreateNewSecret
	}

    # Disconnect from Microsoft Graph before returning
    if ($global:SkipMsGraphLogin -ne $true) {
		Disconnect-MgGraph -ErrorAction SilentlyContinue

		Write-Host "Disconnected from Microsoft Graph." -ForegroundColor Green
	}

    return @{ ApplicationId = $uiApp.AppId; TenantId = $AppTenantId; ApplicationName = $uiAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions;}
    Write-Host "Set-UIAzureADApplication completed."
}

function Set-UIKeyVaultSecrets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $True)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $True)]
        [Guid] $AppTenantId,
        [Parameter(Mandatory = $True)]
        [Guid] $UIApplicationId,
		[AllowNull()]
		[Parameter(Mandatory = $False)]
		[string] $TenantDomain = $null,
		[AllowNull()]
		[Parameter(Mandatory = $False)]
		[string] $SharepointDomain = $null,
        [Parameter(Mandatory = $False)]
        [boolean] $CreateNewSecret = $True,
		[AllowNull()]
		[Parameter(Mandatory = $False)]
		[string] $AppSecret = $null,
        [Parameter(Mandatory = $False)]
        [string] $ErrorActionPreference = $Stop
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '/ReusableModules/Set-KeyVaultSecretWithFirewallRetry.ps1')

    # These need to go into the key vault
    $uiAppTenantId = $AppTenantId;
    $uiAppClientId = $UIApplicationId
    $uiAppDisplayName = "$SolutionAbbreviation-ui-$EnvironmentAbbreviation"

    # Create new secret if requested
    $uiAppClientSecret = $AppSecret
    if ($CreateNewSecret -eq $true) {
        $endDate = [System.DateTime]::Now.AddYears(1)
        $passwordCredential = @{
            displayName = "GMM Generated Secret"
            startDateTime = [System.DateTime]::Now
            endDateTime = $endDate
        }
        $appObjectId = (Get-MgApplication -Filter "appId eq '$uiAppClientId'").Id
        $uiAppClientSecret = (Add-MgApplicationPassword -ApplicationId $appObjectId -PasswordCredential $passwordCredential).SecretText
        Write-Host "Created new application secret for app $uiAppClientId"
    } else {
        Write-Host "Skipping secret creation as CreateNewSecret is set to false"
    }

    $keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if ($null -eq $keyVault) {
        throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
    }

    # Store Application (client) ID in KeyVault
    $uiAppIdKeyVaultSecretName = "uiAppId"

    Write-Host "UI application (client) ID is $uiAppClientId"
	$uiAppIdSecret = New-Object System.Security.SecureString
	$uiAppClientId.ToString().ToCharArray() | ForEach-Object { $uiAppIdSecret.AppendChar($_) }

    Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
                        -ResourceGroup $keyVault.ResourceGroupName `
                        -SecretName $uiAppIdKeyVaultSecretName `
                        -SecretValue $uiAppIdSecret

    Write-Host "$uiAppIdKeyVaultSecretName added to vault for $uiAppDisplayName."

    # Store Application secret in KeyVault (only if a new secret was created)
    if (-not [string]::IsNullOrEmpty($uiAppClientSecret)) {
        $uiAppClientSecretName = "uiPasswordCredentialValue"

        Write-Host "Storing UI application client secret in KeyVault"
        $uiPasswordCredentialValue = New-Object System.Security.SecureString
        $uiAppClientSecret.ToCharArray() | ForEach-Object { $uiPasswordCredentialValue.AppendChar($_) }

        Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
                            -ResourceGroup $keyVault.ResourceGroupName `
                            -SecretName $uiAppClientSecretName `
                            -SecretValue $uiPasswordCredentialValue

        Write-Host "$uiAppClientSecretName added to vault for $uiAppDisplayName."
    } else {
        Write-Host "Skipping application secret storage as no new secret was created"
    }

    # Store tenantID in KeyVault
    $uiTenantSecretName = "uiTenantId"

    Write-Host "UI tenant ID is $uiAppTenantId"
    $uiTenantSecret = New-Object System.Security.SecureString
    $uiAppTenantId.ToString().ToCharArray() | ForEach-Object { $uiTenantSecret.AppendChar($_) }

    Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
                        -ResourceGroup $keyVault.ResourceGroupName `
                        -SecretName $uiTenantSecretName `
                        -SecretValue $uiTenantSecret

    Write-Host "$uiTenantSecretName added to vault for $uiAppDisplayName."

    # Store tenantDomain in KeyVault
    if($null -eq $TenantDomain) {
        $TenantDomain = "not-set"
    }
    $tenantDomainSecretName = "tenantDomain"

    Write-Host "Tenant Domain is $TenantDomain"
    $tenantDomainSecret = New-Object System.Security.SecureString
    $TenantDomain.ToString().ToCharArray() | ForEach-Object { $tenantDomainSecret.AppendChar($_) }

    Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
                        -ResourceGroup $keyVault.ResourceGroupName `
                        -SecretName $tenantDomainSecretName `
                        -SecretValue $tenantDomainSecret

    Write-Host "$tenantDomainSecretName added to vault for UI Group Links."

    # Store sharepointDomain in KeyVault
    if($null -eq $SharepointDomain) {
        $SharepointDomain = "not-set"
    }
    $sharepointDomainSecretName = "sharepointDomain"

    Write-Host "SharePoint Domain is $SharepointDomain"
    $sharepointDomainSecret = New-Object System.Security.SecureString
    $SharepointDomain.ToString().ToCharArray() | ForEach-Object { $sharepointDomainSecret.AppendChar($_) }

    Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
                        -ResourceGroup $keyVault.ResourceGroupName `
                        -SecretName $sharepointDomainSecretName `
                        -SecretValue $sharepointDomainSecret

    Write-Host "$sharepointDomainSecretName added to vault for UI Group Links."

    Write-Host "Set-UIAzureADApplication completed."
}

function New-UIValidationConfiguration {
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
    
    $uiAppDisplayName = "$SolutionAbbreviation-ui-$EnvironmentAbbreviation"
    
    $replyUrls = @("http://localhost:3000")
    
    $requiredResourceAccess = @{
        ResourceAppId  = "00000003-0000-0000-c000-000000000000"
        ResourceAccess = @(
            @{
                Id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d" # User.Read
                Type = "Scope"
            },
            @{
                Id   = "b340eb25-3456-403f-be2f-af7a0d370277" # User.ReadBasic.All
                Type = "Scope"
            }
        )
    }
    
    $config = @{
        displayName            = $uiAppDisplayName
        signInAudience         = "AzureADMyOrg"
        requiredResourceAccess = @($requiredResourceAccess)
        isFallbackPublicClient = $false
        spa                    = @{
            redirectUris = $replyUrls
        }
        web                    = @{
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

function Test-UIApplication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $SolutionAbbreviation,
        
        [Parameter(Mandatory = $true)]
        [string] $EnvironmentAbbreviation
    )
    
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
    
    $uiAppDisplayName = "$SolutionAbbreviation-ui-$EnvironmentAbbreviation"
    
    Write-Host "`n=== Validating Application: $uiAppDisplayName ===" -ForegroundColor Cyan
    
    # Step 1: Check if application exists
    Write-Host "`n[1/3] Checking if application exists..." -ForegroundColor Yellow
    $uiApps = Get-MgApplication -Filter "displayName eq '$uiAppDisplayName'" -All
    
    if ($null -eq $uiApps -or $uiApps.Count -eq 0) {
        $errorMessage = "Application '$uiAppDisplayName' does not exist. Please run the setup script to create it or follow manual setup steps in the documentation."
        Write-Host "❌ $errorMessage" -ForegroundColor Red
        return $false
    }
    
    Write-Host "✅ Application exists." -ForegroundColor Green
    
    # Step 2: Validate uniqueness
    Write-Host "`n[2/3] Validating uniqueness..." -ForegroundColor Yellow
    if ($uiApps.Count -gt 1) {
        $errorMessage = "Found $($uiApps.Count) applications with the name '$uiAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
        Write-Host "❌ $errorMessage" -ForegroundColor Red
        return $false
    }
    
    $uiApp = $uiApps
    Write-Host "   Application ID: $($uiApp.AppId)" -ForegroundColor Gray
    Write-Host "   Object ID: $($uiApp.Id)" -ForegroundColor Gray
    
    # Step 3: Validate configuration
    Write-Host "`n[3/3] Validating configuration..." -ForegroundColor Yellow
    $expectedAppConfig = New-UIValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppId $uiApp.AppId
    
    $configurationMatches = Test-AppMatchesConfiguration -AppObject $uiApp `
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