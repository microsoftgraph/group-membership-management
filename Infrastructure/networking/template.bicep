// =====================================================================================
// GMM Networking Infrastructure - Main Orchestrator
// =====================================================================================
// Deploys two peered VNets (Bastion, Resources) with NSGs, a Bastion Host, a jumpbox VM,
// and Private DNS Zones for PaaS private endpoints. The Resources VNet hosts the
// VmSubnet (jumpbox + NAT-attached) and the PrivateEndpointSubnet, and reserves
// address space for future per-ASP function-integration subnets.
//
// Two deployment modalities controlled by the 'deployBastion' parameter:
//   - deployBastion = true:  Full deployment including Bastion VNet, NSG, Public IP,
//                            and Bastion Host alongside the Resources VNet.
//   - deployBastion = false: Deploys only the Resources VNet and peers it to an
//                            existing Bastion VNet specified by 'existingBastionVnetId'.
//
// NOTE: When deployBastion = false, this template creates peerings on BOTH the Resources
// VNet and the existing Bastion VNet (cross-RG when 'existingBastionResourceGroupName'
// is set). The deployer must have
// 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings/write' permission on the
// existing Bastion VNet for this to succeed.
// =====================================================================================

// -----------------------------------------------
// Common Parameters
// -----------------------------------------------

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Classify the types of resources in this resource group.')
param resourceGroupClassification string = 'networking'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Azure region for all resources.')
param location string = resourceGroup().location

// -----------------------------------------------
// Bastion Deployment Modality
// -----------------------------------------------

@description('When true, deploys a new Bastion VNet and Bastion Host. When false, peers to an existing Bastion VNet.')
param deployBastion bool = true

@description('Resource ID of an existing Bastion VNet to peer with. Required when deployBastion is false.')
param existingBastionVnetId string = ''

@description('Name of the existing Bastion VNet. Required when deployBastion is false (used for peering resource names).')
param existingBastionVnetName string = ''

@description('Resource group name of the existing Bastion VNet. Required when the existing Bastion VNet is in a different resource group (e.g., a shared nonprod bastion). Defaults to the current resource group.')
param existingBastionResourceGroupName string = ''

// -----------------------------------------------
// Address Space Parameters
// -----------------------------------------------

@description('Address prefix for the Bastion VNet.')
param bastionVnetAddressPrefix string = '10.0.0.0/24'

@description('Address prefix for the AzureBastionSubnet.')
param bastionSubnetAddressPrefix string = '10.0.0.0/26'

@description('Address prefix for the Resources VNet (hosts VmSubnet, PrivateEndpointSubnet, and reserved future function-integration address space).')
param resourcesVnetAddressPrefix string = '10.1.0.0/16'

// -----------------------------------------------
// Function VNET Integration (FlexConsumption / FC1)
// -----------------------------------------------
// Function-integration subnets are allocated from CIDR `cidrSubnet(resourcesVnetAddressPrefix, 26, index + 4)`.
// The `+ 4` skips the four /26 blocks that make up VmSubnet (10.x.0.0/24).
// Public indices [0, 59] = up to 60 subnets (0..18 assigned today, 19..59 reserved).
// Additional indices [60, ∞) = passed via additionalFunctionSubnets (3 assigned: 60, 61, 62).
//
// Allocation is split between two append-only lists sharing one CIDR reservation:
//   - publicFunctionSubnets:    indices [0, 59]   (this template)
//   - additionalFunctionSubnets indices [60, ∞)  (additional functions, passed as parameter)
//
// Ordering rules (NORMATIVE — see contracts/function-subnet-allocation-contract.md):
//   1. Append-only in both lists.
//   2. Never renumber an existing entry.
//   3. Never reuse a tombstoned index; set `removed: true` to retire an entry while
//      preserving its slot.
//   4. Public range MUST stay in [0, functionSubnetIndexFloor - 1].
//   5. Additional range MUST be >= functionSubnetIndexFloor; enforced at deploy time
//      via the `_additionalIndexFloorCheck` guard array below.

@description('Additional function-subnet list to append to the public list. Each entry: { name: string, index: int, removed: bool? }. Index MUST be >= functionSubnetIndexFloor (60). Default `[]` keeps the OSS / public-only deploy path identical to pre-feature behavior.')
param additionalFunctionSubnets array = []

var functionSubnetIndexFloor = 60

// Canonical public function-subnet catalog. Indices 0..18 are assigned today;
// 19..59 are reserved for future public functions (WebApi will be re-added
// when Standard plan + Swift VNET integration work lands). Names MUST match
// Service/GroupMembershipManagement/Hosts/<X>/Infrastructure/compute/ folders.
// APPEND-ONLY: never reorder, never renumber, tombstone removals.
var publicFunctionSubnets = [
  { name: 'autoapprover', index: 0 }
  { name: 'azuremaintenance', index: 1 }
  { name: 'azureuserreader', index: 2 }
  { name: 'destinationattributesupdater', index: 3 }
  { name: 'graphupdater', index: 4 }
  { name: 'groupmembershipobtainer', index: 5 }
  { name: 'groupownershipobtainer', index: 6 }
  { name: 'jobscheduler', index: 7 }
  { name: 'jobtrigger', index: 8 }
  { name: 'membershipaggregator', index: 9 }
  { name: 'messagesplitter', index: 10 }
  { name: 'nonprodservice', index: 11 }
  { name: 'notifier', index: 12 }
  { name: 'placemembershipobtainer', index: 13 }
  { name: 'sqldatachecker', index: 14 }
  { name: 'sqlmembershipobtainer', index: 15 }
  { name: 'syncjobupdater', index: 16 }
  { name: 'teamschannelmembershipobtainer', index: 17 }
  { name: 'teamschannelupdater', index: 18 }
  // indices 19..59 reserved for future public functions
]

var allFunctionSubnets = concat(publicFunctionSubnets, additionalFunctionSubnets)
var activeFunctionSubnets = filter(allFunctionSubnets, s => !(s.?removed ?? false))

// Index-floor enforcement (per spec FR-007 / FR-008 + contract §3).
// Bicep `assert` is not enabled in the build pipeline's Bicep version
// (0.41.x — no bicepconfig.json + experimental `assertions` feature opt-in).
// Fallback: guard array. If any additional entry has index < 60, the conditional
// expression injects the string 'INVALID_INDEX_BELOW_FLOOR' into an int slot,
// causing ARM template type-coercion to fail deployment before any infra change.
// `_additionalIndexFloorCheck` is referenced from a benign output below to prevent
// the symbol from being pruned by the compiler.
var _additionalIndexFloorCheck = [
  for s in additionalFunctionSubnets: s.index >= functionSubnetIndexFloor ? s.index : json('"INVALID_INDEX_BELOW_FLOOR"')
]

// Project active function subnets into the shape virtualNetwork.bicep consumes.
// Use `resourceId()` (computable at deploy start) rather than `resourcesNsg.outputs.id`
// (runtime-only) so the for-expression is valid in a variable.
var resourcesFunctionSubnets = [
  for s in activeFunctionSubnets: {
    name: 'func-${s.index < functionSubnetIndexFloor ? 'pub' : 'priv'}-${s.name}'
    addressPrefix: cidrSubnet(resourcesVnetAddressPrefix, 26, s.index + 4)
    nsgId: resourceId('Microsoft.Network/networkSecurityGroups', resourcesNsgName)
  }
]

// -----------------------------------------------
// VM Parameters
// -----------------------------------------------

@description('Size of the jumpbox VM.')
param vmSize string = 'Standard_B2ms'

@description('Daily auto-shutdown time for the jumpbox VM in UTC (24-hour format, e.g. 0200 = 2:00 AM UTC). Default is 0200 which equals 6:00 PM PST.')
param vmDailyAutoShutdownTimeUTC string = '0200'

@description('When true, writes vm admin secrets into data Key Vault before VM provisioning.')
param setVmAdminSecrets bool = false

@description('VM admin username value written to Key Vault when setVmAdminSecrets is true.')
@secure()
param vmAdminUsername string = ''

@description('VM admin password value written to Key Vault when setVmAdminSecrets is true.')
@secure()
param vmAdminPassword string = ''

@description('IP tags to apply to the Bastion Public IP Address.')
param bastionPipIpTags array = []

@description('IP tags to apply to the NAT Gateway Public IP Address.')
param natGatewayPipIpTags array = []

@description('Tags to apply to the Bastion Host resource.')
param bastionTags object = {}

// -----------------------------------------------
// Private DNS Zone Parameters
// -----------------------------------------------

// List of Private DNS Zone names to create (one per PaaS service type with private endpoints).
var privateDnsZoneNames = [
  'privatelink.vaultcore.azure.net'
  'privatelink.database.windows.net'
  'privatelink.servicebus.windows.net'
  'privatelink.blob.core.windows.net'
  'privatelink.azconfig.io'
  'privatelink.table.core.windows.net'
  'privatelink.queue.core.windows.net'
  'privatelink.service.signalr.net'
]

// -----------------------------------------------
// RBAC / Security Parameters
// -----------------------------------------------
// Computed Names
// -----------------------------------------------

var prereqsResourceGroupName = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'
var dataResourceGroupName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var prereqsKeyVaultName = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'
var dataKeyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var vmAdminUsernameSecretName = 'vmAdminUsername'
var vmAdminPasswordSecretName = 'vmAdminPassword'
var sqlServerName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var replicaSqlServerName = '${sqlServerName}-R'
var dataResourceGroupId = subscriptionResourceId('Microsoft.Resources/resourceGroups', dataResourceGroupName)
var jobsStorageAccountName = 'jobs${environmentAbbreviation}${uniqueString(dataResourceGroupId)}'
var functionsStorageAccountName = take('fn${solutionAbbreviation}${environmentAbbreviation}${uniqueString(dataResourceGroupId)}', 24)
var appConfigurationName = '${solutionAbbreviation}-appConfig-${environmentAbbreviation}'
var dcrName = '${solutionAbbreviation}-data-${environmentAbbreviation}-vm-dcr'
var namePrefix = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'
var bastionNsgName = '${namePrefix}-bastion-nsg'
var bastionVnetName = '${namePrefix}-bastion-vnet'
var bastionHostName = '${namePrefix}-bastion'
var bastionPipName = '${namePrefix}-bastion-pip'
var managementNsgName = '${namePrefix}-management-nsg'
var managementVmName = '${namePrefix}-management-vm'
var managementVmComputerName = take('${solutionAbbreviation}-${environmentAbbreviation}-vm', 15)
var managementNicName = '${namePrefix}-management-nic'
var natGatewayName = '${namePrefix}-nat'
var natGatewayPipName = '${namePrefix}-nat-pip'
var resourcesNsgName = '${namePrefix}-resources-nsg'
var resourcesVnetName = '${namePrefix}-resources-vnet'
var vmSubnetAddressPrefix = cidrSubnet(resourcesVnetAddressPrefix, 24, 0)
var privateEndpointSubnetAddressPrefix = cidrSubnet(resourcesVnetAddressPrefix, 17, 1)

// Reference the data Key Vault to retrieve VM admin credentials
resource dataKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: dataKeyVaultName
  scope: resourceGroup(dataResourceGroupName)
}

// Reference the prereqs Key Vault to provision its private endpoint
resource prereqsKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: prereqsKeyVaultName
  scope: resourceGroup(prereqsResourceGroupName)
}

// Resolve which Bastion VNet ID and name to use for peering
var resolvedBastionVnetId = deployBastion ? bastionVnet.outputs.id : existingBastionVnetId
var resolvedBastionVnetName = deployBastion ? bastionVnet.outputs.name : existingBastionVnetName

// Resolve the subscription and resource group for existing bastion peering (cross-subscription + cross-RG support)
var bastionPeeringSubscriptionId = !empty(existingBastionVnetId) ? split(existingBastionVnetId, '/')[2] : subscription().subscriptionId
var bastionPeeringResourceGroupName = !empty(existingBastionResourceGroupName) ? existingBastionResourceGroupName : resourceGroup().name

// =====================================================================================
// NSGs
// =====================================================================================

// --- Bastion NSG (mandatory rules for Azure Bastion) ---
module bastionNsg 'networkSecurityGroup.bicep' = if (deployBastion) {
  name: 'deploy-${bastionNsgName}'
  params: {
    name: bastionNsgName
    location: location
    securityRules: [
      // Inbound rules
      {
        name: 'AllowHttpsInbound'
        properties: {
          priority: 120
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowGatewayManagerInbound'
        properties: {
          priority: 130
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'GatewayManager'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowAzureLoadBalancerInbound'
        properties: {
          priority: 140
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'AzureLoadBalancer'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowBastionHostCommunicationInbound'
        properties: {
          priority: 150
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRanges: [
            '8080'
            '5701'
          ]
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: 'VirtualNetwork'
        }
      }
      {
        name: 'DenyAllInbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
      // Outbound rules
      {
        name: 'AllowSshRdpOutbound'
        properties: {
          priority: 100
          direction: 'Outbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRanges: [
            '22'
            '3389'
          ]
          sourceAddressPrefix: '*'
          destinationAddressPrefix: 'VirtualNetwork'
        }
      }
      {
        name: 'AllowAzureCloudOutbound'
        properties: {
          priority: 110
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: 'AzureCloud'
        }
      }
      {
        name: 'AllowBastionCommunicationOutbound'
        properties: {
          priority: 120
          direction: 'Outbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRanges: [
            '8080'
            '5701'
          ]
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: 'VirtualNetwork'
        }
      }
      {
        name: 'AllowHttpOutbound'
        properties: {
          priority: 130
          direction: 'Outbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '80'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: 'Internet'
        }
      }
      {
        name: 'DenyAllOutbound'
        properties: {
          priority: 4096
          direction: 'Outbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

// --- Management NSG ---
module managementNsg 'networkSecurityGroup.bicep' = {
  name: 'deploy-${managementNsgName}'
  params: {
    name: managementNsgName
    location: location
    securityRules: [
      {
        name: 'DenyAllInbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

// --- Resources NSG ---
module resourcesNsg 'networkSecurityGroup.bicep' = {
  name: 'deploy-${resourcesNsgName}'
  params: {
    name: resourcesNsgName
    location: location
    securityRules: [
      {
        name: 'AllowVirtualNetworkInbound'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'DenyAllInbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

// =====================================================================================
// VNets
// =====================================================================================

// --- Bastion VNet ---
module bastionVnet 'virtualNetwork.bicep' = if (deployBastion) {
  name: 'deploy-${bastionVnetName}'
  params: {
    name: bastionVnetName
    location: location
    addressPrefix: bastionVnetAddressPrefix
    subnets: [
      {
        name: 'AzureBastionSubnet'
        addressPrefix: bastionSubnetAddressPrefix
        nsgId: bastionNsg.outputs.id
        natGatewayId: null
      }
    ]
  }
}

// --- Resources VNet ---
module resourcesVnet 'virtualNetwork.bicep' = {
  name: 'deploy-${resourcesVnetName}'
  params: {
    name: resourcesVnetName
    location: location
    addressPrefix: resourcesVnetAddressPrefix
    subnets: [
      {
        name: 'PrivateEndpointSubnet'
        addressPrefix: privateEndpointSubnetAddressPrefix
        nsgId: resourcesNsg.outputs.id
        natGatewayId: null
        // VERIFY before first validation deploy: confirm this matches the value on the
        // LIVE int/ua PrivateEndpointSubnet (it holds ~10 live private endpoints — a
        // wrong value drifts it). 'Disabled' is the conventional default for PE subnets.
        privateEndpointNetworkPolicies: 'Disabled'
      }
      {
        name: 'VmSubnet'
        addressPrefix: vmSubnetAddressPrefix
        nsgId: managementNsg.outputs.id
        natGatewayId: natGateway.outputs.id
      }
    ]
    functionSubnets: resourcesFunctionSubnets
  }
  dependsOn: [
    resourcesNsg
  ]
}

// =====================================================================================
// VNet Peerings
// =====================================================================================

// --- Bastion <-> Resources (Resources side) ---
module resourcesToBastionPeering 'vnetPeering.bicep' = {
  name: 'deploy-${resourcesVnetName}-to-bastion-peering'
  params: {
    localVnetName: resourcesVnet.outputs.name
    remoteVnetId: resolvedBastionVnetId
    remoteVnetName: resolvedBastionVnetName
  }
}

// --- Bastion <-> Resources (Bastion side - new Bastion) ---
module bastionToResourcesPeering 'vnetPeering.bicep' = if (deployBastion) {
  name: 'deploy-${bastionVnetName}-to-resources-peering'
  params: {
    localVnetName: bastionVnet.outputs.name
    remoteVnetId: resourcesVnet.outputs.id
    remoteVnetName: resourcesVnet.outputs.name
  }
}

// --- Bastion <-> Resources (Bastion side - existing Bastion) ---
// NOTE: This creates a peering on the existing Bastion VNet. The deployer must have
// 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings/write' permission on the
// existing Bastion VNet for this to succeed.
// When 'existingBastionResourceGroupName' is set, the peering deploys into that RG
// (cross-RG peering for shared bastion scenarios).
module existingBastionToResourcesPeering 'vnetPeering.bicep' = if (!deployBastion) {
  name: 'deploy-existing-bastion-to-resources-peering'
  scope: resourceGroup(bastionPeeringSubscriptionId, bastionPeeringResourceGroupName)
  params: {
    localVnetName: existingBastionVnetName
    remoteVnetId: resourcesVnet.outputs.id
    remoteVnetName: resourcesVnet.outputs.name
  }
}

// =====================================================================================
// Bastion Host + Public IP (conditional)
// =====================================================================================

// =====================================================================================
// NAT Gateway + Public IP (for Jumpbox outbound internet access)
// =====================================================================================

module natGatewayPip 'publicIpAddress.bicep' = {
  name: 'deploy-${natGatewayPipName}'
  params: {
    name: natGatewayPipName
    location: location
    skuName: 'Standard'
    allocationMethod: 'Static'
    ipTags: natGatewayPipIpTags
  }
}

module natGateway 'natGateway.bicep' = {
  name: 'deploy-${natGatewayName}'
  params: {
    name: natGatewayName
    location: location
    publicIpId: natGatewayPip.outputs.id
  }
}

module bastionPip 'publicIpAddress.bicep' = if (deployBastion) {
  name: 'deploy-${bastionPipName}'
  params: {
    name: bastionPipName
    location: location
    skuName: 'Standard'
    allocationMethod: 'Static'
    ipTags: bastionPipIpTags
  }
}

module bastionHost 'bastion.bicep' = if (deployBastion) {
  name: 'deploy-${bastionHostName}'
  params: {
    name: bastionHostName
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', bastionVnetName, 'AzureBastionSubnet')
    publicIpId: bastionPip.outputs.id
    tags: bastionTags
  }
  // subnetId is now a plain resourceId string (no longer a module-output reference), so
  // the implicit dependency on the Bastion VNet/subnet is gone — make it explicit.
  dependsOn: [
    bastionVnet
  ]
}

// =====================================================================================
// Jumpbox VM + NIC
// =====================================================================================

module managementNic 'networkInterface.bicep' = {
  name: 'deploy-${managementNicName}'
  params: {
    name: managementNicName
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'VmSubnet')
  }
  // subnetId is now a plain resourceId string (no longer a module-output reference), so
  // make the dependency on the Resources VNet (which creates VmSubnet) explicit.
  dependsOn: [
    resourcesVnet
  ]
}

module vmAdminSecrets '../data/keyVaultSecretsSecure.bicep' = if (setVmAdminSecrets) {
  name: 'deploy-${namePrefix}-vm-admin-secrets'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: vmAdminUsernameSecretName
          value: vmAdminUsername
        }
        {
          name: vmAdminPasswordSecretName
          value: vmAdminPassword
        }
      ]
    }
  }
}

module managementVm 'virtualMachine.bicep' = {
  name: 'deploy-${managementVmName}'
  params: {
    name: managementVmName
    location: location
    vmSize: vmSize
    computerName: managementVmComputerName
    adminUsername: prereqsKeyVault.getSecret(vmAdminUsernameSecretName)
    adminPassword: prereqsKeyVault.getSecret(vmAdminPasswordSecretName)
    nicId: managementNic.outputs.id
    dailyAutoShutdownTimeUTC: vmDailyAutoShutdownTimeUTC
  }
  dependsOn: [
    managementNic
    vmAdminSecrets
  ]
}

module dcrAssociation '../data/dataCollectionRuleAssociation.bicep' = {
  name: 'deploy-${managementVmName}-dcr-assoc'
  params: {
    vmName: managementVmName
    dataCollectionRuleId: resourceId(dataResourceGroupName, 'Microsoft.Insights/dataCollectionRules', dcrName)
    associationName: '${managementVmName}-dcr-assoc'
  }
  dependsOn: [
    managementVm
  ]
}


// =====================================================================================
// Private DNS Zones + VNet Links
// =====================================================================================

module dnsZones 'privateDnsZone.bicep' = [
  for zone in privateDnsZoneNames: {
    name: 'deploy-dns-${replace(zone, '.', '-')}'
    params: {
      name: zone
    }
  }
]

// Link each DNS zone to the Resources VNet (provides DNS resolution for both VmSubnet and PrivateEndpointSubnet)
module dnsZonePrivateLinkLinks 'privateDnsZoneVnetLink.bicep' = [
  for (zone, i) in privateDnsZoneNames: {
    name: 'deploy-dnslink-${replace(zone, '.', '-')}-to-resources'
    params: {
      dnsZoneName: zone
      vnetId: resourcesVnet.outputs.id
      vnetName: resourcesVnet.outputs.name
    }
    dependsOn: [
      dnsZones[i]
    ]
  }
]

// =====================================================================================
// Private Endpoints — Prereqs Key Vault
// =====================================================================================

module prereqsKvPrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-prereqs-kv-pe'
  params: {
    name: '${namePrefix}-prereqs-kv-pe'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
    privateLinkServiceId: prereqsKeyVault.id
    groupIds: ['vault']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.vaultcore.azure.net')].outputs.id
  }
  dependsOn: [
    resourcesVnet
    dnsZonePrivateLinkLinks
  ]
}

module dataKvPrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-data-kv-pe'
  params: {
    name: '${namePrefix}-data-kv-pe'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
    privateLinkServiceId: dataKeyVault.id
    groupIds: ['vault']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.vaultcore.azure.net')].outputs.id
  }
  dependsOn: [
    resourcesVnet
    dnsZonePrivateLinkLinks
  ]
}

// -----------------------------------------------
// SQL Private Endpoints (Primary + Replica)
// -----------------------------------------------

module primarySqlPrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-primary-sql-pe'
  params: {
    name: '${namePrefix}-primary-sql-pe'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Sql/servers', sqlServerName)
    groupIds: ['sqlServer']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.database.windows.net')].outputs.id
  }
  dependsOn: [
    resourcesVnet
    dnsZonePrivateLinkLinks
  ]
}

module replicaSqlPrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-replica-sql-pe'
  params: {
    name: '${namePrefix}-replica-sql-pe'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Sql/servers', replicaSqlServerName)
    groupIds: ['sqlServer']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.database.windows.net')].outputs.id
  }
  dependsOn: [
    resourcesVnet
    dnsZonePrivateLinkLinks
  ]
}

// -----------------------------------------------
// Jobs Storage Account Private Endpoint (Blob)
// -----------------------------------------------

module jobsStorageBlobPrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-jobs-sa-blob-pe'
  params: {
    name: '${namePrefix}-jobs-sa-blob-pe'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Storage/storageAccounts', jobsStorageAccountName)
    groupIds: ['blob']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.blob.core.windows.net')].outputs.id
  }
  dependsOn: [
    resourcesVnet
    dnsZonePrivateLinkLinks
  ]
}

// -----------------------------------------------
// Functions Storage Account Private Endpoint (Blob)
// -----------------------------------------------

module functionsStorageBlobPrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-fn-sa-blob-pe'
  params: {
    name: '${namePrefix}-fn-sa-blob-pe'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Storage/storageAccounts', functionsStorageAccountName)
    groupIds: ['blob']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.blob.core.windows.net')].outputs.id
  }
  dependsOn: [
    resourcesVnet
    dnsZonePrivateLinkLinks
  ]
}

// -----------------------------------------------
// Functions Storage Account Private Endpoint (Table)
// -----------------------------------------------

module functionsStorageTablePrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-fn-sa-table-pe'
  params: {
    name: '${namePrefix}-fn-sa-table-pe'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Storage/storageAccounts', functionsStorageAccountName)
    groupIds: ['table']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.table.core.windows.net')].outputs.id
  }
  dependsOn: [
    resourcesVnet
    dnsZonePrivateLinkLinks
  ]
}

// -----------------------------------------------
// Functions Storage Account Private Endpoint (Queue)
// -----------------------------------------------

module functionsStorageQueuePrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-fn-sa-queue-pe'
  params: {
    name: '${namePrefix}-fn-sa-queue-pe'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Storage/storageAccounts', functionsStorageAccountName)
    groupIds: ['queue']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.queue.core.windows.net')].outputs.id
  }
  dependsOn: [
    resourcesVnet
    dnsZonePrivateLinkLinks
  ]
}

// -----------------------------------------------
// App Configuration Private Endpoint
// -----------------------------------------------

module appConfigPrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-appconfig-pe'
  params: {
    name: '${namePrefix}-appconfig-pe'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.AppConfiguration/configurationStores', appConfigurationName)
    groupIds: ['configurationStores']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.azconfig.io')].outputs.id
  }
  dependsOn: [
    resourcesVnet
    dnsZonePrivateLinkLinks
  ]
}

// =====================================================================================
// Outputs
// =====================================================================================

output bastionVnetId string = deployBastion ? bastionVnet.outputs.id : existingBastionVnetId
output resourcesVnetId string = resourcesVnet.outputs.id
output resourcesVnetName string = resourcesVnet.outputs.name
output privateEndpointSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'PrivateEndpointSubnet')
output vmSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'VmSubnet')

// Catalog of active function-integration subnets keyed by both the original short
// name (from publicFunctionSubnets / additionalFunctionSubnets) and the deployed
// subnet name (`func-pub-*` / `func-priv-*`). Consumers (compute templates) look up
// subnet IDs by short name to stay tombstone-safe (array index is not stable across
// tombstones).
output functionSubnets array = [
  for s in activeFunctionSubnets: {
    name: s.name
    index: s.index
    subnetName: 'func-${s.index < functionSubnetIndexFloor ? 'pub' : 'priv'}-${s.name}'
    id: resourceId('Microsoft.Network/virtualNetworks/subnets', resourcesVnetName, 'func-${s.index < functionSubnetIndexFloor ? 'pub' : 'priv'}-${s.name}')
  }
]

// Index-floor guard surface. Emitting `_additionalIndexFloorCheck` as a strongly-typed
// `int[]` output forces ARM to validate every element against the declared type.
// If any entry in `additionalFunctionSubnets` has `index < functionSubnetIndexFloor`,
// the corresponding slot holds the string `'INVALID_INDEX_BELOW_FLOOR'` (via
// `json('"..."')`) and the deployment fails type validation BEFORE any infra change.
// This output is otherwise informational; consumers should not depend on it.
output _additionalIndexFloorEnforcement int[] = _additionalIndexFloorCheck
