function Test-AppMatchesConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $AppObject,
        
        [Parameter(Mandatory = $true)]
        [hashtable] $ExpectedConfiguration,
        
        [Parameter(Mandatory = $false)]
        [switch] $ShowDetailedReport
    )
    
    Write-Host "Starting validation of app against expected configuration..."
    
    try {
        $allMismatches = @()
        
        # Validate basic properties
        $basicMismatches = Test-BasicAppProperties -AppObject $AppObject -ExpectedConfig $ExpectedConfiguration
        $allMismatches += $basicMismatches
        
        # Validate web configuration
        if ($ExpectedConfiguration.ContainsKey('web')) {
            $webMismatches = Test-WebConfiguration -AppObject $AppObject -ExpectedWebConfig $ExpectedConfiguration.web
            $allMismatches += $webMismatches
        }
        
        # Validate SPA configuration
        if ($ExpectedConfiguration.ContainsKey('spa')) {
            $spaMismatches = Test-SpaConfiguration -AppObject $AppObject -ExpectedSpaConfig $ExpectedConfiguration.spa
            $allMismatches += $spaMismatches
        }
        
        # Validate API configuration
        if ($ExpectedConfiguration.ContainsKey('api')) {
            $apiMismatches = Test-ApiConfiguration -AppObject $AppObject -ExpectedApiConfig $ExpectedConfiguration.api
            $allMismatches += $apiMismatches
        }
        
        # Validate required resource access
        if ($ExpectedConfiguration.ContainsKey('requiredResourceAccess')) {
            $resourceMismatches = Test-RequiredResourceAccess -AppObject $AppObject -ExpectedResourceAccess $ExpectedConfiguration.requiredResourceAccess
            $allMismatches += $resourceMismatches
        }
        
        # Validate identifier URIs
        if ($ExpectedConfiguration.ContainsKey('identifierUris')) {
            $identifierMismatches = Test-IdentifierUris -AppObject $AppObject -ExpectedIdentifierUris $ExpectedConfiguration.identifierUris
            $allMismatches += $identifierMismatches
        }
        
        # Validate optional claims
        if ($ExpectedConfiguration.ContainsKey('optionalClaims')) {
            $claimsMismatches = Test-OptionalClaims -AppObject $AppObject -ExpectedOptionalClaims $ExpectedConfiguration.optionalClaims
            $allMismatches += $claimsMismatches
        }
        
        # Generate comprehensive report
        if ($allMismatches.Count -gt 0) {
            Write-Host "`n=== CONFIGURATION MISMATCH REPORT ===" -ForegroundColor Red
            Write-Host "Found $($allMismatches.Count) mismatch(es):`n" -ForegroundColor Red
            
            for ($i = 0; $i -lt $allMismatches.Count; $i++) {
                Write-Host "[$($i + 1)] $($allMismatches[$i])" -ForegroundColor Yellow
                
                # Add remediation guide for each mismatch only if flag is enabled
                if ($ShowDetailedReport) {
                    $remediation = Get-RemediationGuide -MismatchMessage $allMismatches[$i]
                    if ($remediation) {
                        Write-Host "    Remediation: $remediation" -ForegroundColor Cyan
                    }
                }
                Write-Host ""
            }
            
            Write-Host "=== END REPORT ===" -ForegroundColor Red
            return $false
        }
        else {
            Write-Host "App validation completed successfully - all configurations match" -ForegroundColor Green
            return $true
        }
    }
    catch {
        Write-Error "Error during app validation: $($_.Exception.Message)"
        return $false
    }
}

function Test-BasicAppProperties {
    [CmdletBinding()]
    param(
        [object] $AppObject,
        [hashtable] $ExpectedConfig
    )
    
    $mismatches = @()
    
    # Check display name
    if ($ExpectedConfig.ContainsKey('displayName')) {
        if ($AppObject.DisplayName -ne $ExpectedConfig.displayName) {
            $mismatches += "DisplayName mismatch. Expected: '$($ExpectedConfig.displayName)', Actual: '$($AppObject.DisplayName)'"
        }
    }
    
    # Check sign-in audience
    if ($ExpectedConfig.ContainsKey('signInAudience')) {
        if ($AppObject.SignInAudience -ne $ExpectedConfig.signInAudience) {
            $mismatches += "SignInAudience mismatch. Expected: '$($ExpectedConfig.signInAudience)', Actual: '$($AppObject.SignInAudience)'"
        }
    }
    
    # Check fallback public client
    if ($ExpectedConfig.ContainsKey('isFallbackPublicClient')) {
        # Normalize null/empty to false for comparison
        $actualValue = if ($null -eq $AppObject.IsFallbackPublicClient -or $AppObject.IsFallbackPublicClient -eq '') { $false } else { $AppObject.IsFallbackPublicClient }
        $expectedValue = $ExpectedConfig.isFallbackPublicClient
        
        if ($actualValue -ne $expectedValue) {
            $mismatches += "IsFallbackPublicClient mismatch. Expected: '$expectedValue', Actual: '$actualValue'"
        }
    }
    
    return $mismatches
}

function Test-WebConfiguration {
    [CmdletBinding()]
    param(
        [object] $AppObject,
        [hashtable] $ExpectedWebConfig
    )
    
    $mismatches = @()
    
    if ($null -eq $AppObject.Web) {
        $mismatches += "App Web configuration is null but expected configuration exists"
        return $mismatches
    }
    
    # Check redirect URIs
    if ($ExpectedWebConfig.ContainsKey('redirectUris')) {
        $arrayMismatches = Test-ArraysEqual -Actual $AppObject.Web.RedirectUris -Expected $ExpectedWebConfig.redirectUris -Name "RedirectUris"
        $mismatches += $arrayMismatches
    }
    
    # Check implicit grant settings
    if ($ExpectedWebConfig.ContainsKey('implicitGrantSettings')) {
        $expectedImplicit = $ExpectedWebConfig.implicitGrantSettings
        $actualImplicit = $AppObject.Web.ImplicitGrantSettings
        
        if ($null -eq $actualImplicit) {
            $mismatches += "App ImplicitGrantSetting is null but expected configuration exists"
        }
        else {
            if ($expectedImplicit.ContainsKey('enableAccessTokenIssuance')) {
                if ($actualImplicit.EnableAccessTokenIssuance -ne $expectedImplicit.enableAccessTokenIssuance) {
                    $mismatches += "EnableAccessTokenIssuance mismatch. Expected: '$($expectedImplicit.enableAccessTokenIssuance)', Actual: '$($actualImplicit.EnableAccessTokenIssuance)'"
                }
            }
            
            if ($expectedImplicit.ContainsKey('enableIdTokenIssuance')) {
                if ($actualImplicit.EnableIdTokenIssuance -ne $expectedImplicit.enableIdTokenIssuance) {
                    $mismatches += "EnableIdTokenIssuance mismatch. Expected: '$($expectedImplicit.enableIdTokenIssuance)', Actual: '$($actualImplicit.EnableIdTokenIssuance)'"
                }
            }
        }
    }
    
    return $mismatches
}

function Test-SpaConfiguration {
    [CmdletBinding()]
    param(
        [object] $AppObject,
        [hashtable] $ExpectedSpaConfig
    )
    
    $mismatches = @()
    
    if ($null -eq $AppObject.Spa) {
        $mismatches += "App SPA configuration is null but expected configuration exists"
        return $mismatches
    }
    
    # Check SPA redirect URIs
    if ($ExpectedSpaConfig.ContainsKey('redirectUris')) {
        $arrayMismatches = Test-ArraysEqual -Actual $AppObject.Spa.RedirectUris -Expected $ExpectedSpaConfig.redirectUris -Name "SPA RedirectUris"
        $mismatches += $arrayMismatches
    }
    
    return $mismatches
}

function Test-ApiConfiguration {
    [CmdletBinding()]
    param(
        [object] $AppObject,
        [hashtable] $ExpectedApiConfig
    )
    
    $mismatches = @()
    
    if ($null -eq $AppObject.Api) {
        $mismatches += "App API configuration is null but expected configuration exists"
        return $mismatches
    }
    
    # Check requested access token version
    if ($ExpectedApiConfig.ContainsKey('requestedAccessTokenVersion')) {
        # Handle null/empty as version 1 (the default)
        $actualValue = if ($null -eq $AppObject.Api.RequestedAccessTokenVersion -or $AppObject.Api.RequestedAccessTokenVersion -eq '') { 1 } else { $AppObject.Api.RequestedAccessTokenVersion }
        $expectedValue = $ExpectedApiConfig.requestedAccessTokenVersion
        
        if ($actualValue -ne $expectedValue) {
            $mismatches += "RequestedAccessTokenVersion mismatch. Expected: '$expectedValue', Actual: '$actualValue'"
        }
    }
    
    # Check OAuth2 permission scopes
    if ($ExpectedApiConfig.ContainsKey('oauth2PermissionScopes')) {
        $scopeMismatches = Test-OAuth2PermissionScopes -Actual $AppObject.Api.Oauth2PermissionScopes -Expected $ExpectedApiConfig.oauth2PermissionScopes
        $mismatches += $scopeMismatches
    }

    # Check pre-authorized applications
    if ($ExpectedApiConfig.ContainsKey('preAuthorizedApplications')) {
        $preAuthMismatches = Test-PreAuthorizedApplications -Actual $AppObject.Api.PreAuthorizedApplications -Expected $ExpectedApiConfig.preAuthorizedApplications
        $mismatches += $preAuthMismatches
    }
    
    return $mismatches
}

function Test-PreAuthorizedApplications {
    [CmdletBinding()]
    param(
        [object[]] $Actual,
        [hashtable[]] $Expected
    )

    $mismatches = @()
    
    # Handle both being null or empty
    if (($null -eq $Actual -or $Actual.Count -eq 0) -and ($null -eq $Expected -or $Expected.Count -eq 0)) {
        Write-Host "Both actual and expected preAuthorizedApplications are null or empty - match"
        return $mismatches
    }
    
    # Handle one being null/empty while the other is not
    if (($null -eq $Actual -or $Actual.Count -eq 0) -and ($null -ne $Expected -and $Expected.Count -gt 0)) {
        $mismatches += "PreAuthorizedApplications mismatch: actual is null/empty but expected has $($Expected.Count) items"
        return $mismatches
    }
    
    if (($null -ne $Actual -and $Actual.Count -gt 0) -and ($null -eq $Expected -or $Expected.Count -eq 0)) {
        $mismatches += "PreAuthorizedApplications mismatch: actual has $($Actual.Count) items but expected is null/empty"
        return $mismatches
    }
    
    $actualArray = @($Actual)
    $expectedArray = @($Expected)
    
    if ($actualArray.Count -ne $expectedArray.Count) {
        $mismatches += "PreAuthorizedApplications count mismatch. Expected: $($expectedArray.Count), Actual: $($actualArray.Count)"
    }
    
    foreach ($expectedApp in $expectedArray) {
        # Find matching app by AppId
        $matchingApp = $actualArray | Where-Object { $_.AppId -eq $expectedApp.appId }
        
        if (-not $matchingApp) {
            $mismatches += "PreAuthorizedApplication with AppId '$($expectedApp.appId)' not found"
            continue
        }
        
        # Check delegated permission IDs
        if ($expectedApp.ContainsKey('delegatedPermissionIds')) {
            $expectedPermissions = @($expectedApp.delegatedPermissionIds) | Sort-Object
            $actualPermissions = @($matchingApp.DelegatedPermissionIds) | Sort-Object
            
            if ($expectedPermissions.Count -ne $actualPermissions.Count) {
                $mismatches += "DelegatedPermissionIds count mismatch for AppId '$($expectedApp.appId)'. Expected: $($expectedPermissions.Count), Actual: $($actualPermissions.Count)"
            }
            else {
                for ($i = 0; $i -lt $expectedPermissions.Count; $i++) {
                    if ($expectedPermissions[$i] -ne $actualPermissions[$i]) {
                        $mismatches += "DelegatedPermissionId mismatch at index $i for AppId '$($expectedApp.appId)'. Expected: '$($expectedPermissions[$i])', Actual: '$($actualPermissions[$i])'"
                    }
                }
            }
        }
    }
    
    if ($mismatches.Count -eq 0) {
        Write-Host "PreAuthorizedApplications validation passed"
    }
    
    return $mismatches
}

function Test-OAuth2PermissionScopes {
    [CmdletBinding()]
    param(
        [object[]] $Actual,
        [hashtable[]] $Expected
    )
    
    $mismatches = @()
    
    if ($null -eq $Actual -and ($null -eq $Expected -or $Expected.Count -eq 0)) {
        return $mismatches
    }
    
    if ($null -eq $Actual -or $null -eq $Expected) {
        $mismatches += "OAuth2PermissionScopes mismatch: one is null while the other is not"
        return $mismatches
    }
    
    $actualArray = @($Actual)
    $expectedArray = @($Expected)
    
    if ($actualArray.Count -ne $expectedArray.Count) {
        $mismatches += "OAuth2PermissionScopes count mismatch. Expected: $($expectedArray.Count), Actual: $($actualArray.Count)"
    }
    
    foreach ($expectedScope in $expectedArray) {
        $matchingScope = $actualArray | Where-Object { $_.Value -eq $expectedScope.value }
        
        if (-not $matchingScope) {
            $mismatches += "OAuth2PermissionScope with value '$($expectedScope.value)' not found"
            continue
        }
        
        # Validate scope properties (excluding ID which is auto-generated)
        $scopeProperties = @('adminConsentDescription', 'adminConsentDisplayName', 'userConsentDescription', 'userConsentDisplayName', 'isEnabled', 'type')
        
        foreach ($property in $scopeProperties) {
            if ($expectedScope.ContainsKey($property)) {
                $expectedValue = $expectedScope[$property]
                $actualValue = $matchingScope.$property
                
                if ($actualValue -ne $expectedValue) {
                    $mismatches += "OAuth2PermissionScope property '$property' mismatch for scope '$($expectedScope.value)'. Expected: '$expectedValue', Actual: '$actualValue'"
                }
            }
        }
    }
    
    return $mismatches
}

function Test-RequiredResourceAccess {
    [CmdletBinding()]
    param(
        [object] $AppObject,
        [hashtable[]] $ExpectedResourceAccess
    )

    $mismatches = @()
    $actualRRA = @($AppObject.RequiredResourceAccess)
    $expectedRRA = @($ExpectedResourceAccess)

    foreach ($expected in $expectedRRA) {
        $match = $actualRRA | Where-Object { $_.ResourceAppId -eq $expected.ResourceAppId }
        if (-not $match) {
            $resourceName = Get-ResourceAppIdName -ResourceAppId $expected.ResourceAppId
            $mismatches += "Missing Permissions for ResourceAppId $($expected.ResourceAppId) ($resourceName)"
            continue
        }

        $expectedIds = ($expected.ResourceAccess | ForEach-Object { $_.Id.Guid }) | Sort-Object
        $actualIds = ($match.ResourceAccess | ForEach-Object { $_.Id.Guid }) | Sort-Object

        if (-not ($expectedIds -join ',' -eq $actualIds -join ',')) {
            $resourceName = Get-ResourceAppIdName -ResourceAppId $expected.ResourceAppId
            $mismatches += "ResourceAccess mismatch for ResourceAppId $($expected.ResourceAppId) ($resourceName). Expected: $($expectedIds -join ', '), Actual: $($actualIds -join ', ')"
        }
    }

    if ($mismatches.Count -eq 0) {
        Write-Host "All RequiredResourceAccess entries match."
    }
    
    return $mismatches
}


function Test-IdentifierUris {
    [CmdletBinding()]
    param(
        [object] $AppObject,
        [string[]] $ExpectedIdentifierUris
    )
    
    return Test-ArraysEqual -Actual $AppObject.IdentifierUris -Expected $ExpectedIdentifierUris -Name "IdentifierUris"
}

function Test-OptionalClaims {
    [CmdletBinding()]
    param(
        [object] $AppObject,
        [hashtable] $ExpectedOptionalClaims
    )
    
    $mismatches = @()
    
    if ($null -eq $AppObject.OptionalClaims -and ($null -eq $ExpectedOptionalClaims -or $ExpectedOptionalClaims.Count -eq 0)) {
        return $mismatches
    }
    
    if ($null -eq $AppObject.OptionalClaims) {
        $mismatches += "App OptionalClaims is null but expected configuration exists"
        return $mismatches
    }
    
    # Check access token claims
    if ($ExpectedOptionalClaims.ContainsKey('accessToken')) {

        $expectedAccessTokenClaims = @($ExpectedOptionalClaims.accessToken)
        $actualAccessTokenClaims = @($AppObject.OptionalClaims.AccessToken)
        
        foreach ($expectedClaim in $expectedAccessTokenClaims) {
            $matchingClaim = $actualAccessTokenClaims | Where-Object { $_.Name -eq $expectedClaim.name }
            
            if (-not $matchingClaim) {
                $mismatches += "Optional access token claim '$($expectedClaim.name)' not found"
            }
        }
    }
    
    return $mismatches
}

function Test-ArraysEqual {
    [CmdletBinding()]
    param(
        [object[]] $Actual,
        [object[]] $Expected,
        [string] $Name
    )
    
    $mismatches = @()
    
    # Handle null/empty cases
    if (($null -eq $Expected -or $Expected.Count -eq 0)) {
        # If expected is empty, we don't care what's in actual
        return $mismatches
    }
    
    if ($null -eq $Actual -or $Actual.Count -eq 0) {
        # If actual is empty but expected has items, that's a mismatch
        $mismatches += "$Name mismatch: actual is null/empty but expected has $($Expected.Count) items"
        return $mismatches
    }
    
    $actualArray = @($Actual)
    $expectedArray = @($Expected)
    
    # Check that all expected items are present in actual
    foreach ($expectedItem in $expectedArray) {
        $found = $false
        foreach ($actualItem in $actualArray) {
            if ($actualItem -eq $expectedItem) {
                $found = $true
                break
            }
        }
        
        if (-not $found) {
            $mismatches += "$Name missing expected item: '$expectedItem'"
        }
    }
    
    return $mismatches
}

function Get-RemediationGuide {
    [CmdletBinding()]
    param(
        [string] $MismatchMessage
    )
    
    # Basic App Properties
    if ($MismatchMessage -match "DisplayName mismatch") {
        return "Update the app registration's display name in Azure Portal > App registrations > [Your App] > Branding & properties"
    }
    
    if ($MismatchMessage -match "SignInAudience mismatch") {
        return "Change supported account types in Azure Portal > App registrations > [Your App] > Authentication > Supported account types"
    }
    
    if ($MismatchMessage -match "IsFallbackPublicClient mismatch") {
        return "Update public client setting in Azure Portal > App registrations > [Your App] > Authentication > Advanced settings > Allow public client flows"
    }
    
    # Web Configuration
    if ($MismatchMessage -match "App Web configuration is null") {
        return "Add web platform configuration in Azure Portal > App registrations > [Your App] > Authentication > Add a platform > Web"
    }
    
    if ($MismatchMessage -match "RedirectUris.*mismatch") {
        return "Update redirect URIs in Azure Portal > App registrations > [Your App] > Authentication > Web > Redirect URIs"
    }
    
    if ($MismatchMessage -match "EnableAccessTokenIssuance mismatch") {
        return "Update implicit grant settings in Azure Portal > App registrations > [Your App] > Authentication > Implicit grant and hybrid flows > Access tokens"
    }
    
    if ($MismatchMessage -match "EnableIdTokenIssuance mismatch") {
        return "Update implicit grant settings in Azure Portal > App registrations > [Your App] > Authentication > Implicit grant and hybrid flows > ID tokens"
    }
    
    # SPA Configuration
    if ($MismatchMessage -match "App SPA configuration is null") {
        return "Add Single-page application platform configuration in Azure Portal > App registrations > [Your App] > Authentication > Add a platform > Single-page application"
    }
    
    if ($MismatchMessage -match "SPA RedirectUris.*mismatch") {
        return "Update SPA redirect URIs in Azure Portal > App registrations > [Your App] > Authentication > Single-page application > Redirect URIs"
    }
    
    # API Configuration
    if ($MismatchMessage -match "App API configuration is null") {
        return "Configure API settings in Azure Portal > App registrations > [Your App] > Expose an API"
    }
    
    if ($MismatchMessage -match "RequestedAccessTokenVersion mismatch") {
        return "Update access token version in Azure Portal > App registrations > [Your App] > Manifest > 'accessTokenAcceptedVersion' property"
    }
    
    if ($MismatchMessage -match "OAuth2PermissionScope.*not found") {
        return "Add the missing OAuth2 permission scope in Azure Portal > App registrations > [Your App] > Expose an API > Add a scope"
    }
    
    if ($MismatchMessage -match "OAuth2PermissionScope property.*mismatch") {
        return "Update the OAuth2 permission scope properties in Azure Portal > App registrations > [Your App] > Expose an API > [Edit scope]"
    }
    
    # Pre-authorized Applications
    if ($MismatchMessage -match "PreAuthorizedApplication.*not found") {
        return "Add pre-authorized application in Azure Portal > App registrations > [Your App] > Expose an API > Add a client application"
    }
    
    if ($MismatchMessage -match "DelegatedPermissionId.*mismatch") {
        return "Update delegated permission IDs for pre-authorized applications in Azure Portal > App registrations > [Your App] > Expose an API > Authorized client applications"
    }
    
    if ($MismatchMessage -match "PreAuthorizedApplications.*mismatch.*null") {
        return "Configure pre-authorized applications in Azure Portal > App registrations > [Your App] > Expose an API > Add a client application"
    }
    
    # Required Resource Access
    if ($MismatchMessage -match "Missing ResourceAppId") {
        return "Add API permission in Azure Portal > App registrations > [Your App] > API permissions > Add a permission"
    }
    
    if ($MismatchMessage -match "ResourceAccess mismatch") {
        return "Update API permissions in Azure Portal > App registrations > [Your App] > API permissions > [Edit permissions for the specific API]"
    }
    
    # Identifier URIs
    if ($MismatchMessage -match "IdentifierUris.*mismatch") {
        return "Update Application ID URI in Azure Portal > App registrations > [Your App] > Expose an API > Application ID URI"
    }
    
    # Optional Claims
    if ($MismatchMessage -match "App OptionalClaims is null") {
        return "Configure optional claims in Azure Portal > App registrations > [Your App] > Token configuration > Add optional claim"
    }
    
    if ($MismatchMessage -match "Optional access token claim.*not found") {
        return "Add the missing optional access token claim in Azure Portal > App registrations > [Your App] > Token configuration > Add optional claim > Access"
    }
    
    # Generic fallback
    return "Review the Azure Portal app registration configuration to match the expected values. Use PowerShell commands like Update-MgApplication for programmatic updates."
}

function Get-ResourceAppIdName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $ResourceAppId
    )
    
    # Map well-known resource app IDs to friendly names
    switch ($ResourceAppId) {
        "00000003-0000-0000-c000-000000000000" { return "Microsoft Graph" }
        # Currently we only use Graph, but these can be added as needed.
        # "00000002-0000-0000-c000-000000000000" { return "Azure Active Directory Graph" }
        # "797f4846-ba00-4fd7-ba43-dac1f8f63013" { return "Windows Azure Service Management API" }
        # "00000001-0000-0000-c000-000000000000" { return "Azure SQL Database" }
        # "a0be0c72-870c-4358-b4d6-cfe0f5b96b63" { return "Azure Storage" }
        # "e406a681-f3d4-42a8-90b6-c2b029497af1" { return "Azure Application Insights API" }
        # "2ff814a6-3304-4ab8-85cb-cd0e6f879c1d" { return "Microsoft Search" }
        # "00000009-0000-0000-c000-000000000000" { return "Microsoft 365 Management APIs" }
        default { return "Unknown API" }
    }
}
