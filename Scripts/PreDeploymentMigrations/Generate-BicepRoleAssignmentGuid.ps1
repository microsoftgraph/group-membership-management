$ErrorActionPreference = "Stop"

<#
.SYNOPSIS
Generates a RFC 4122 v5 GUID using Bicep's deterministic approach in GMM.

.DESCRIPTION
Generates a consistent, deterministic GUID (UUID v5) using SHA-1 hashing with a fixed namespace.
This matches the algorithm used by Bicep for uniqueString() function and role assignment names.

.PARAMETER RoleDefinitionId
The role definition ID (from roleIdMapping).

.PARAMETER PrincipalId
The principal ID (user, group, or service principal).

.PARAMETER ResourceId
The resource ID (typically Key Vault).

.EXAMPLE
$guid = Generate-BicepGuid -RoleDefinitionId "4633458b-17de-408a-b874-0445c86b69e6" -PrincipalId "d2f187e8-8fe2-44d5-a047-1dbb647754e5" -ResourceId "/subscriptions/32176032-eca1-4a63-9ff0-2ee074d308b5/resourceGroups/dan-data-x1/providers/Microsoft.KeyVault/vaults/dan-data-x1"
Write-Host "Generated GUID: $guid"

.NOTES
Uses the RFC 4122 v5 (SHA-1 based) UUID specification with namespace 11fb06fb-712d-4ddd-98c7-e71bbd588830.
#>
function Generate-BicepRoleAssignmentGuid {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string]$RoleDefinitionId,
		[Parameter(Mandatory = $true)]
		[string]$PrincipalId,
		[Parameter(Mandatory = $true)]
		[string]$ResourceId
	)

	Write-Verbose "Generate-BicepGuid starting..."
	Write-Verbose "  RoleDefinitionId: $RoleDefinitionId"
	Write-Verbose "  PrincipalId: $PrincipalId"
	Write-Verbose "  ResourceId: $ResourceId"

	# Helper function to convert GUID bytes to big-endian format for RFC 4122 v5 compliance
	function ConvertTo-BigEndian {
		param([byte[]]$Bytes)
		# RFC 4122 v5 requires big-endian byte order for the time and clock fields
		[Array]::Reverse($Bytes, 0, 4)
		[Array]::Reverse($Bytes, 4, 2)
		[Array]::Reverse($Bytes, 6, 2)
		return $Bytes
	}

	# Step 1: Prepare input for hashing
	# Bicep uses a fixed namespace (11fb06fb-712d-4ddd-98c7-e71bbd588830) for deterministic GUID generation
	$namespaceId = [guid]'11fb06fb-712d-4ddd-98c7-e71bbd588830'
	
	# Combine the three inputs with hyphens to match Bicep's format
	$nameString = "$RoleDefinitionId-$PrincipalId-$ResourceId"
    $nameBytes = [System.Text.Encoding]::UTF8.GetBytes($nameString)
    
    # Step 2: Convert namespace to bytes and apply big-endian format
    # This ensures the namespace bytes match Bicep's internal representation
    $namespaceBytes = $namespaceId.ToByteArray()
    $namespaceBytes = ConvertTo-BigEndian $namespaceBytes
    
    # Step 3: Combine namespace and input for hashing
    # RFC 4122 v5 requires concatenating the namespace with the input name
    $combinedBytes = $namespaceBytes + $nameBytes
    
    # Step 4: Compute SHA-1 hash
    # RFC 4122 v5 uses SHA-1 to deterministically generate a GUID from the inputs
    # This produces a 20-byte hash from which we extract the first 16 bytes for the GUID
    $hash = [System.Security.Cryptography.SHA1]::Create().ComputeHash($combinedBytes)
    
    # Step 5: Extract first 16 bytes for GUID and set version/variant bits
    # RFC 4122 v5 requires specific bit patterns to identify the version and variant
    $guidBytes = @($hash[0..15])
    
    # Set version to 5 (bits 12-15 = 0101 binary)
    # This identifies the GUID as version 5 (SHA-1 based)
    $guidBytes[6] = ($guidBytes[6] -band 0x0F) -bor 0x50

    # Set variant to RFC 4122 (bits 6-7 = 10 binary)
    # This identifies the GUID as RFC 4122 compliant
    $guidBytes[8] = ($guidBytes[8] -band 0x3F) -bor 0x80
    
    # Step 6: Convert back to big-endian and create final GUID
    # Apply big-endian conversion to produce the final GUID in the correct byte order
    $guidBytes = ConvertTo-BigEndian $guidBytes
    return New-Object System.Guid -ArgumentList @(,[byte[]]$guidBytes)
	
	Write-Verbose "Generate-BicepGuid completed. Result: $resultGuid"
	
	return $resultGuid
}