
$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal that can read and update the Graph.
Be aware that running this in VS Code doesn't work for some reason, it works better if you run it in a regular Powershell session.
You may have to open the created Azure AD app in your demo tenant and consent to the permissions!

Basically, this script is designed to create an Azure AD app with the appropriate permissions in a given tenant
(application permissions User.Read.All and GroupMember.Read.All) and write its credentials to a key vault in another tenant.
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

.PARAMETER Clean
When re-running the script, this flag is used to indicate if we need to recreate the application or use the existing one.

.EXAMPLE
# these are arbitrary guids and subscription names, you'll have to change them.
Set-GraphCredentialsAzureADApplication	-SubscriptionName "<subscription-name>" `
									-SolutionAbbreviation "<solution-abbreviation>" `
									-EnvironmentAbbreviation "<environment-abbreviation>" `
									-TenantIdToCreateAppIn "<app-tenant-id>" `
									-TenantIdWithKeyVault "<keyvault-tenant-id>" `
									-Clean $false `
									-Verbose
#>

function Set-GraphCredentialsAzureADApplication {
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
		[string] $CertificateName,
		[Parameter(Mandatory=$False)]
		[string] $SubscriptionName,
		[Parameter(Mandatory = $False)]
		[boolean] $SaveToKeyVault = $True,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipIfApplicationExists = $True,
		[Parameter(Mandatory=$False)]
		[boolean] $Clean = $False,
		[Parameter(Mandatory=$False)]
		[boolean] $CreateNewSecret = $True,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Host "`nSet-GraphCredentialsAzureADApplication starting...`n"

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
    $graphAppDisplayName = "$SolutionAbbreviation-Graph-$EnvironmentAbbreviation"
	$graphApps = Get-MgApplication -Filter "displayName eq '$graphAppDisplayName'"
	
	# Validate that we don't have multiple applications with the same name
	if($null -ne $graphApps -and $graphApps.Count -gt 1) {
		Write-Error "Found $($graphApps.Count) applications with the name '$graphAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
		throw "Multiple applications found with the same display name: $graphAppDisplayName"
	}
	
	# Convert to single application object if we have exactly one
	$graphApp = if($null -ne $graphApps -and $graphApps.Count -eq 1) { $graphApps } else { $null }
	$updatedAPIPermissions = $false

	if($null -ne $graphApp -and $SkipIfApplicationExists -eq $true -and $Clean -eq $false)
	{
		Write-Host "Application $graphAppDisplayName already exists. Skipping creation..."
		return @{ ApplicationId = $graphApp.AppId; TenantId = $AppTenantId; ApplicationName = $graphAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions; }
	}

	if($Clean -eq $true -and $null -ne $graphApp)
	{
		$displayName = $graphApp.DisplayName;
		$objectId = $graphApp.Id;
		try {
			Remove-MgApplication -ApplicationId $objectId
			Write-Host "Removed $displayName..." -ForegroundColor Green;
			$graphApp = $null
		}
		catch {
			Write-Host "Failed to remove $displayName..." -ForegroundColor Red;
			throw
		}
	}
    #endregion

    # These are the function apps that need to interact with the graph.
    $replyUrls = @("graphupdater", "groupmembershipobtainer") |
        ForEach-Object { "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$_.azurewebsites.net"};
    $replyUrls += "http://localhost";

	#region Create Application
	if($null -eq $graphApp)
	{
		Write-Host "Creating Azure AD app $graphAppDisplayName"

		$appCreationParameters = New-GraphCredentialsValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation
		
		# Create application body for Microsoft Graph
		$graphApp = New-MgApplication -BodyParameter $appCreationParameters
		$updatedAPIPermissions = $true
		
		New-MgServicePrincipal -AppId $graphApp.AppId

		# Update with identifier URI
		$updatedAppParameters = New-GraphCredentialsValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-AppId $graphApp.AppId

		Update-MgApplication -ApplicationId $graphApp.Id -BodyParameter $updatedAppParameters
		Write-Host "Created Azure AD app $graphAppDisplayName"
	}
	else
	{
		Write-Host "Azure AD app $graphAppDisplayName already exists."
		Write-Host "Checking if app needs update..."

		$expectedAppConfig = New-GraphCredentialsValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-AppId $graphApp.AppId

		. ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
		$needsUpdate = -not (Test-AppMatchesConfiguration -AppObject $graphApp -ExpectedConfiguration $expectedAppConfig)
		
		if ($needsUpdate) {
			Write-Host "App $graphAppDisplayName needs update. Updating..."
			Update-MgApplication -ApplicationId $graphApp.Id -BodyParameter $expectedAppConfig
			$updatedAPIPermissions = $true
			Write-Host "Finished updating Azure AD app $graphAppDisplayName"
		}
		else {
			Write-Host "No update needed for app $graphAppDisplayName."
		}
    }

	if ($updatedAPIPermissions -eq $true) {
		Write-Host "Waiting 15 seconds for Azure AD replication..."
		Start-Sleep -Seconds 15
		Write-Host "Done waiting for Azure AD replication."
	}

	if($SaveToKeyVault -eq $true)
	{
		Set-GraphAppKeyVaultSecrets `
			-SolutionAbbreviation $SolutionAbbreviation `
			-EnvironmentAbbreviation $EnvironmentAbbreviation `
			-TenantIdToCreateAppIn $AppTenantId `
			-ApplicationClientId $graphApp.AppId `
			-CertificateName $CertificateName `
			-CreateNewSecret $CreateNewSecret
	}

	# Disconnect from Microsoft Graph before returning
	if ($global:SkipMsGraphLogin -ne $true) {
		Disconnect-MgGraph -ErrorAction SilentlyContinue

		Write-Host "Disconnected from Microsoft Graph." -ForegroundColor Green
	}

	Write-Host "`nSet-GraphCredentialsAzureADApplication completed.`n"
	return @{ ApplicationId = $graphApp.AppId; TenantId = $AppTenantId; ApplicationName = $graphAppDisplayName; UpdatedApiPermissions = $updatedAPIPermissions;}
}


function Set-GraphAppKeyVaultSecrets {
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
		[AllowNull()]
		[Parameter(Mandatory = $False)]
		[string] $AppSecret = $null,
		[Parameter(Mandatory=$False)]
		[string] $CertificateName,
		[Parameter(Mandatory=$False)]
		[boolean] $CreateNewSecret = $True
	)

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')
    . ($scriptsDirectory + '/ReusableModules/Set-KeyVaultSecretWithFirewallRetry.ps1')

	# These need to go into the key vault
	$graphAppTenantId = $AppTenantId;
	$graphAppClientId = $ApplicationClientId;
	$graphAppDisplayName = "$SolutionAbbreviation-Graph-$EnvironmentAbbreviation" 

	# Create new secret if requested
	$graphAppClientSecret = $AppSecret
	if ($CreateNewSecret -eq $true) {
		$endDate = [System.DateTime]::Now.AddYears(1)
		$passwordCredential = @{
			displayName = "GMM Generated Secret"
			startDateTime = [System.DateTime]::Now
			endDateTime = $endDate
		}
		$appObjectId = (Get-MgApplication -Filter "appId eq '$graphAppClientId'").Id
	    $graphAppClientSecret = (Add-MgApplicationPassword -ApplicationId $appObjectId -PasswordCredential $passwordCredential).SecretText
		Write-Host "Created new application secret for app $graphAppClientId"
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
    $graphClientIdKeyVaultSecretName = "graphAppClientId"

	$graphClientIdSecret = New-Object System.Security.SecureString
	$graphAppClientId.ToString().ToCharArray() | ForEach-Object { $graphClientIdSecret.AppendChar($_) }

	Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
										-ResourceGroup $keyVault.ResourceGroupName `
										-SecretName $graphClientIdKeyVaultSecretName `
										-SecretValue $graphClientIdSecret

	Write-Host "$graphClientIdKeyVaultSecretName added to vault for $graphAppDisplayName."

	# Store Application secret in KeyVault (only if a new secret was created)
	if (-not [string]::IsNullOrEmpty($graphAppClientSecret)) {
		$graphAppClientSecretName = "graphAppClientSecret"

		Write-Host "Storing Graph application client secret in KeyVault"
		$graphClientSecret = New-Object System.Security.SecureString
		$graphAppClientSecret.ToCharArray() | ForEach-Object { $graphClientSecret.AppendChar($_) }

		Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
										-ResourceGroup $keyVault.ResourceGroupName `
										-SecretName $graphAppClientSecretName `
										-SecretValue $graphClientSecret

		Write-Host "$graphAppClientSecretName added to vault for $graphAppDisplayName."
	} else {
		Write-Host "Skipping application secret storage as no new secret was created"
	}

	# Store tenantID in KeyVault
	$graphTenantSecretName = "graphAppTenantId"

	$graphTenantSecret = New-Object System.Security.SecureString
	$graphAppTenantId.ToString().ToCharArray() | ForEach-Object { $graphTenantSecret.AppendChar($_) }

	Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
										-ResourceGroup $keyVault.ResourceGroupName `
										-SecretName $graphTenantSecretName `
										-SecretValue $graphTenantSecret

    Write-Host "$graphTenantSecretName added to vault for $graphAppDisplayName."

	# Store certificate name in KeyVault
	$graphAppCertificateName = "graphAppCertificateName"
	$graphAppCertificate = Get-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName -ResourceGroup $keyVault.ResourceGroupName -SecretName $graphAppCertificateName -AsPlainText
    $setGraphAppCertificate = $false

	if(!$graphAppCertificate -and !$CertificateName){
		$CertificateName = "not-set"
		$setGraphAppCertificate = $true
	} elseif ($CertificateName) {
		$setGraphAppCertificate = $true
	}

	if($setGraphAppCertificate){
		Write-Host "Certificate name is $CertificateName"
		$graphAppCertificateSecret = New-Object System.Security.SecureString
		$CertificateName.ToCharArray() | ForEach-Object { $graphAppCertificateSecret.AppendChar($_) }

		Set-KeyVaultSecretWithFirewallRetry -VaultName $keyVault.VaultName `
											-ResourceGroup $keyVault.ResourceGroupName `
											-SecretName $graphAppCertificateName `
											-SecretValue $graphAppCertificateSecret
		Write-Host "$graphAppCertificateName added to vault for $graphAppDisplayName."
	}

	Write-Host "Set-GraphCredentialsAzureADApplication completed."
}

function New-GraphCredentialsValidationConfiguration {
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
	
	$graphAppDisplayName = "$SolutionAbbreviation-Graph-$EnvironmentAbbreviation"
	
	# These are the function apps that need to interact with the graph
	$replyUrls = @("graphupdater", "groupmembershipobtainer") |
		ForEach-Object { "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$_.azurewebsites.net" }
	$replyUrls += "http://localhost"
	
	# Get Microsoft Graph service principal to retrieve current permission IDs
	$mgGraphServicePrincipal = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"
	
	$appPermissions = $mgGraphServicePrincipal.AppRoles `
		| Where-Object { ($_.Value -eq "User.Read.All") -or ($_.Value -eq "GroupMember.Read.All") -or ($_.Value -eq "Member.Read.Hidden") } `
		| ForEach-Object { @{Id = $_.Id; Type = "Role" } }

	$delegatedPermissions = $mgGraphServicePrincipal.Oauth2PermissionScopes `
		| Where-Object { ($_.Value -eq "ChannelMember.ReadWrite.All") -or ($_.Value -eq "Mail.Send") } `
		| ForEach-Object { @{Id = $_.Id; Type = "Scope" } }

	$requiredResourceAccess = @{
		ResourceAppId = "00000003-0000-0000-c000-000000000000"
		ResourceAccess = $appPermissions + $delegatedPermissions
	}
	
	$config = @{
		displayName            = $graphAppDisplayName
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

function Test-GraphCredentialsApplication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $SolutionAbbreviation,
        
        [Parameter(Mandatory = $true)]
        [string] $EnvironmentAbbreviation
    )
    
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '/ApplicationSetupScripts/Test-AppMatchesConfiguration.ps1')
    
    $graphAppDisplayName = "$SolutionAbbreviation-Graph-$EnvironmentAbbreviation"
    
    Write-Host "`n=== Validating Application: $graphAppDisplayName ===" -ForegroundColor Cyan
    
    # Step 1: Check if application exists
    Write-Host "`n[1/3] Checking if application exists..." -ForegroundColor Yellow
    $graphApps = Get-MgApplication -Filter "displayName eq '$graphAppDisplayName'" -All
    
    if ($null -eq $graphApps -or $graphApps.Count -eq 0) {
        $errorMessage = "Application '$graphAppDisplayName' does not exist. Please run the setup script to create it or follow manual setup steps in the documentation."
		Write-Host "❌ $errorMessage" -ForegroundColor Red
		return $false
	}
    
    Write-Host "✅ Application exists." -ForegroundColor Green
    
    # Step 2: Validate uniqueness
    Write-Host "`n[2/3] Validating uniqueness..." -ForegroundColor Yellow
    if ($graphApps.Count -gt 1) {
        $errorMessage = "Found $($graphApps.Count) applications with the name '$graphAppDisplayName'. This is ambiguous and could lead to unexpected behavior. Please ensure application names are unique or manually remove duplicate applications before running this script."
        Write-Host "❌ $errorMessage" -ForegroundColor Red
        return $false
    }
    
    $graphApp = $graphApps
    Write-Host "   Application ID: $($graphApp.AppId)" -ForegroundColor Gray
    Write-Host "   Object ID: $($graphApp.Id)" -ForegroundColor Gray
    
    # Step 3: Validate configuration
    Write-Host "`n[3/3] Validating configuration..." -ForegroundColor Yellow
    $expectedAppConfig = New-GraphCredentialsValidationConfiguration -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppId $graphApp.AppId
    
    $configurationMatches = Test-AppMatchesConfiguration -AppObject $graphApp `
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