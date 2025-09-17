function Test-AppNeedsUpdate {
    param(
        [Parameter(Mandatory)]
        $AppObject,

        [Parameter(Mandatory)]
        $ExpectedRequiredResourceAccess,

		[Parameter(Mandatory)]
		[string]$ExpectedSignInAudience,

		[Parameter(Mandatory)]
		[bool]$ExpectedEnableAccessTokenIssuance,

		[Parameter(Mandatory)]
		[bool]$ExpectedEnableIdTokenIssuance
    )

	# Check SignInAudience and ImplicitGrantSettings
	if ($AppObject.SignInAudience -ne $ExpectedSignInAudience -or
        $AppObject.Web.ImplicitGrantSetting.EnableAccessTokenIssuance -ne $ExpectedEnableAccessTokenIssuance -or
        $AppObject.Web.ImplicitGrantSetting.EnableIdTokenIssuance -ne $ExpectedEnableIdTokenIssuance) {
        return $true
    }

    # Check RequiredResourceAccess
    $actualRRA   = @($AppObject.RequiredResourceAccess)
    $expectedRRA = @($ExpectedRequiredResourceAccess)

    if ($actualRRA.Count -ne $expectedRRA.Count) {
        return $true
    }

    foreach ($expected in $expectedRRA) {
        $match = $actualRRA | Where-Object {
            $_.ResourceAppId -eq $expected.ResourceAppId
        }

        if (-not $match) { return $true }

        # Compare ResourceAccess sets
        $actualAccess   = $match.ResourceAccess | Sort-Object Id, Type
        $expectedAccess = $expected.ResourceAccess | Sort-Object Id, Type

        for ($i = 0; $i -lt $expectedAccess.Count; $i++) {
            if ($actualAccess[$i].Id -ne $expectedAccess[$i].Id -or
                $actualAccess[$i].Type -ne $expectedAccess[$i].Type) {
                return $true
            }
        }
    }

    return $false
}
