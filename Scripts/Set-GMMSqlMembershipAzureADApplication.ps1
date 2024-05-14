$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Create an Azure AD application and service principal that can access Azure Data Factory

.PARAMETER SubscriptionName
Subscription Name

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.EXAMPLE
Set-GMMSqlMembershipAzureADApplication	-SubscriptionName "<subscription name>" `
									-SolutionAbbreviation "<solution abbreviation>" `
									-EnvironmentAbbreviation "<environment abbreviation>" `
									-Clean $false `
									-Verbose
#>
function Set-GMMSqlMembershipAzureADApplication {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory=$True)]
		[boolean] $Clean,
		[Parameter(Mandatory = $False)]
		[boolean] $SkipIfApplicationExists = $True,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-GMMSqlMembershipAzureADApplication starting..."

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent

	. ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
	Install-AzModuleIfNeeded

	. ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
	Add-AzAccountIfNeeded

	Set-AzContext -SubscriptionName $SubscriptionName
	$context = Get-AzContext
	$currentTenantId = $context.Tenant.Id

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
	{
		throw "The KeyVault Group ($keyVaultName) does not exist. Unable to continue."
	}

	#region Delete Application / Service Principal if they already exist
	$sqlMembershipAppDisplayName = "GMM SqlMembership - $EnvironmentAbbreviation"
	$sqlMembershipApp = Get-AzADApplication -DisplayName "$sqlMembershipAppDisplayName"

	if($null -ne $sqlMembershipApp -and $SkipIfApplicationExists -eq $true -and $Clean -eq $false)
	{
		Write-Host "Application $sqlMembershipAppDisplayName already exists. Skipping creation..."
		return @{ ApplicationId = $sqlMembershipApp.AppId; TenantId = $currentTenantId; }
	}

	if($sqlMembershipApp -and $Clean)
	{
		Write-Verbose "Removing existing $sqlMembershipAppDisplayName"
		Update-AzADApplication	-ObjectId $sqlMembershipApp.Id `
								-AvailableToOtherTenants $false

		Write-Verbose "Removing Application for $sqlMembershipAppDisplayName..."
		do
		{
			$secondsToSleep = 5
			Write-Verbose "Waiting $secondsToSleep seconds for $sqlMembershipAppDisplayName's Application to be removed."
			Start-Sleep -Seconds $secondsToSleep
			Remove-AzADApplication 	-ObjectId $sqlMembershipApp.Id `
									-ErrorAction SilentlyContinue `
									-ErrorVariable $removeAdAppError
		}
		while($removeAdAppError)
		$sqlMembershipApp = Get-AzADApplication -DisplayName "$sqlMembershipAppDisplayName"
		Write-Verbose "Removed existing $sqlMembershipAppDisplayName"
	}
	#endregion

	#region Create Appplication
	if($null -eq $sqlMembershipApp)
	{
		$sqlMembershipAppIdentifierUriGuid = New-Guid
		$sqlMembershipApp = New-AzADApplication	-DisplayName $sqlMembershipAppDisplayName `
												-AvailableToOtherTenants $false

		$webSettings = $sqlMembershipApp.Web
		$webSettings.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
		$webSettings.ImplicitGrantSetting.EnableIdTokenIssuance = $true

		Update-AzADApplication	-ObjectId $($sqlMembershipApp.Id) `
								-Web $webSettings

	}
	else
	{
		$webSettings = $sqlMembershipApp.Web
		$webSettings.ImplicitGrantSetting.EnableAccessTokenIssuance = $true
		$webSettings.ImplicitGrantSetting.EnableIdTokenIssuance = $true

		Update-AzADApplication	-ObjectId $($sqlMembershipApp.Id) `
								-DisplayName $sqlMembershipAppDisplayName `
								-AvailableToOtherTenants $false `
								-Web $webSettings
	}

	#region Store Application (client) ID in KeyVault
	$sqlMembershipAppID = $sqlMembershipApp.AppId
    Write-Verbose "Application (client) ID is $($sqlMembershipApp.AppId)"

    $sqlMembershipAppIdKeyVaultSecretName = "sqlMembershipAppId"
	$sqlMembershipAppIdSecret = New-Object System.Security.SecureString
	$sqlMembershipAppId.ToCharArray() | ForEach-Object { $sqlMembershipAppIdSecret.AppendChar($_) }

	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $sqlMembershipAppIdKeyVaultSecretName `
						 -SecretValue $sqlMembershipAppIdSecret
	Write-Verbose "$sqlMembershipAppIdKeyVaultSecretName added to vault for $sqlMembershipAppDisplayName..."

	#region Store Application (client) secret in KeyVault
	$passwordCredential = New-AzADAppCredential -ObjectId $sqlMembershipApp.Id -StartDate (Get-Date).AddHours(-1) -EndDate (Get-Date).AddYears(1)
	$sqlMembershipAppPasswordCredentialValue = $passwordCredential.SecretText
	$sqlMembershipAppPasswordCredentialValueSecretName = "sqlMembershipAppPasswordCredentialValue"
	$sqlMembershipAppPasswordCredentialValueSecret = New-Object System.Security.SecureString
	$sqlMembershipAppPasswordCredentialValue.ToCharArray() | ForEach-Object { $sqlMembershipAppPasswordCredentialValueSecret.AppendChar($_) }

	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $sqlMembershipAppPasswordCredentialValueSecretName `
						 -SecretValue $sqlMembershipAppPasswordCredentialValueSecret
	Write-Verbose "$sqlMembershipAppPasswordCredentialValueSecretName added to vault for $sqlMembershipAppDisplayName..."

	#region Create Service Principal and store ServicePrinipal.ObjectId in KeyVault
	$sqlMembershipAppServicePrincipal = Get-AzADServicePrincipal -ApplicationId  $sqlMembershipAppID
	if($null -eq $sqlMembershipAppServicePrincipal)
	{
		$sqlMembershipAppServicePrincipal = New-AzADServicePrincipal -ApplicationId $sqlMembershipAppID
		Write-Verbose "SPN of sqlMembershipApplication is: $($sqlMembershipAppServicePrincipal.Id)"
		$sqlMembershipAppServicePrincipal = Get-AzADServicePrincipal -ApplicationId  $sqlMembershipAppID
	}

	$sqlMembershipAppServicePrincipalObjectIDKeyVaultSecretName = "sqlMembershipAppServicePrincipalObjectId"
	$sqlMembershipAppServicePrincipalObjectIDSecret = New-Object System.Security.SecureString
	$sqlMembershipAppServicePrincipal.Id.ToCharArray() | ForEach-Object { $sqlMembershipAppServicePrincipalObjectIDSecret.AppendChar($_) }

	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
	                     -Name $sqlMembershipAppServicePrincipalObjectIDKeyVaultSecretName `
						 -SecretValue $sqlMembershipAppServicePrincipalObjectIDSecret
	Write-Verbose "$sqlMembershipAppServicePrincipalObjectIDKeyVaultSecretName (SPN) added to vault for $sqlMembershipAppDisplayName..."

	#endregion
	Write-Verbose "Set-GMMSqlMembershipAzureADApplication completed."

	return @{ ApplicationId = $sqlMembershipApp.AppId; TenantId = $currentTenantId; }
}