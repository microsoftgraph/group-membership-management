// =====================================================================================
// GMM Networking Infrastructure - Main Orchestrator
// =====================================================================================
// Deploys three peered VNets (Bastion, Management, PrivateLink) with NSGs, a Bastion
// Host, a jumpbox VM, and Private DNS Zones for PaaS private endpoints.
//
// Two deployment modalities controlled by the 'deployBastion' parameter:
//   - deployBastion = true:  Full deployment including Bastion VNet, NSG, Public IP,
//                            and Bastion Host alongside Management and PrivateLink resources.
//   - deployBastion = false: Deploys only Management and PrivateLink resources and peers
//                            them to an existing Bastion VNet specified by 'existingBastionVnetId'.
//
// NOTE: When deployBastion = false, this template creates peerings on BOTH the Management
// VNet and the existing Bastion VNet. The deployer must have
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

@description('Address prefix for the Management VNet.')
param managementVnetAddressPrefix string = '10.0.1.0/24'

@description('Address prefix for the JumpboxSubnet.')
param jumpboxSubnetAddressPrefix string = '10.0.1.0/24'

@description('Address prefix for the PrivateLink VNet.')
param privateLinkVnetAddressPrefix string = '10.1.0.0/16'

@description('Address prefix for the PrivateEndpointSubnet.')
param privateEndpointSubnetAddressPrefix string = '10.1.0.0/16'

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
var managementVnetName = '${namePrefix}-management-vnet'
var managementVmName = '${namePrefix}-management-vm'
var managementVmComputerName = take('${solutionAbbreviation}-${environmentAbbreviation}-vm', 15)
var managementNicName = '${namePrefix}-management-nic'
var natGatewayName = '${namePrefix}-nat'
var natGatewayPipName = '${namePrefix}-nat-pip'
var privateLinkNsgName = '${namePrefix}-privatelink-nsg'
var privateLinkVnetName = '${namePrefix}-privatelink-vnet'

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

// --- PrivateLink NSG ---
module privateLinkNsg 'networkSecurityGroup.bicep' = {
  name: 'deploy-${privateLinkNsgName}'
  params: {
    name: privateLinkNsgName
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

// --- Management VNet ---
module managementVnet 'virtualNetwork.bicep' = {
  name: 'deploy-${managementVnetName}'
  params: {
    name: managementVnetName
    location: location
    addressPrefix: managementVnetAddressPrefix
    subnets: [
      {
        name: 'JumpboxSubnet'
        addressPrefix: jumpboxSubnetAddressPrefix
        nsgId: managementNsg.outputs.id
        natGatewayId: natGateway.outputs.id
      }
    ]
  }
}

// --- PrivateLink VNet ---
module privateLinkVnet 'virtualNetwork.bicep' = {
  name: 'deploy-${privateLinkVnetName}'
  params: {
    name: privateLinkVnetName
    location: location
    addressPrefix: privateLinkVnetAddressPrefix
    subnets: [
      {
        name: 'PrivateEndpointSubnet'
        addressPrefix: privateEndpointSubnetAddressPrefix
        nsgId: privateLinkNsg.outputs.id
        natGatewayId: null
      }
    ]
  }
}

// =====================================================================================
// VNet Peerings
// =====================================================================================

// --- Bastion <-> Management (Management side) ---
module managementToBastionPeering 'vnetPeering.bicep' = {
  name: 'deploy-${managementVnetName}-to-bastion-peering'
  params: {
    localVnetName: managementVnet.outputs.name
    remoteVnetId: resolvedBastionVnetId
    remoteVnetName: resolvedBastionVnetName
  }
}

// --- Bastion <-> Management (Bastion side - new Bastion) ---
module bastionToManagementPeering 'vnetPeering.bicep' = if (deployBastion) {
  name: 'deploy-${bastionVnetName}-to-management-peering'
  params: {
    localVnetName: bastionVnet.outputs.name
    remoteVnetId: managementVnet.outputs.id
    remoteVnetName: managementVnet.outputs.name
  }
}

// --- Bastion <-> Management (Bastion side - existing Bastion) ---
// NOTE: This creates a peering on the existing Bastion VNet. The deployer must have
// 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings/write' permission on the
// existing Bastion VNet for this to succeed.
// When 'existingBastionResourceGroupName' is set, the peering deploys into that RG
// (cross-RG peering for shared bastion scenarios).
module existingBastionToManagementPeering 'vnetPeering.bicep' = if (!deployBastion) {
  name: 'deploy-existing-bastion-to-management-peering'
  scope: resourceGroup(bastionPeeringSubscriptionId, bastionPeeringResourceGroupName)
  params: {
    localVnetName: existingBastionVnetName
    remoteVnetId: managementVnet.outputs.id
    remoteVnetName: managementVnet.outputs.name
  }
}

// --- Management <-> PrivateLink (Management side) ---
module managementToPrivateLinkPeering 'vnetPeering.bicep' = {
  name: 'deploy-${managementVnetName}-to-privatelink-peering'
  params: {
    localVnetName: managementVnet.outputs.name
    remoteVnetId: privateLinkVnet.outputs.id
    remoteVnetName: privateLinkVnet.outputs.name
  }
}

// --- Management <-> PrivateLink (PrivateLink side) ---
module privateLinkToManagementPeering 'vnetPeering.bicep' = {
  name: 'deploy-${privateLinkVnetName}-to-management-peering'
  params: {
    localVnetName: privateLinkVnet.outputs.name
    remoteVnetId: managementVnet.outputs.id
    remoteVnetName: managementVnet.outputs.name
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
    subnetId: bastionVnet.outputs.subnets[0].id
    publicIpId: bastionPip.outputs.id
    tags: bastionTags
  }
}

// =====================================================================================
// Jumpbox VM + NIC
// =====================================================================================

module managementNic 'networkInterface.bicep' = {
  name: 'deploy-${managementNicName}'
  params: {
    name: managementNicName
    location: location
    subnetId: managementVnet.outputs.subnets[0].id
  }
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
    adminUsername: dataKeyVault.getSecret(vmAdminUsernameSecretName)
    adminPassword: dataKeyVault.getSecret(vmAdminPasswordSecretName)
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

// Link each DNS zone to the Management VNet
module dnsZoneManagementLinks 'privateDnsZoneVnetLink.bicep' = [
  for (zone, i) in privateDnsZoneNames: {
    name: 'deploy-dnslink-${replace(zone, '.', '-')}-to-management'
    params: {
      dnsZoneName: zone
      vnetId: managementVnet.outputs.id
      vnetName: managementVnet.outputs.name
    }
    dependsOn: [
      dnsZones[i]
    ]
  }
]

// Link each DNS zone to the PrivateLink VNet
module dnsZonePrivateLinkLinks 'privateDnsZoneVnetLink.bicep' = [
  for (zone, i) in privateDnsZoneNames: {
    name: 'deploy-dnslink-${replace(zone, '.', '-')}-to-privatelink'
    params: {
      dnsZoneName: zone
      vnetId: privateLinkVnet.outputs.id
      vnetName: privateLinkVnet.outputs.name
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
    subnetId: privateLinkVnet.outputs.subnets[0].id
    privateLinkServiceId: prereqsKeyVault.id
    groupIds: ['vault']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.vaultcore.azure.net')].outputs.id
  }
  dependsOn: [
    dnsZonePrivateLinkLinks
  ]
}

module dataKvPrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-data-kv-pe'
  params: {
    name: '${namePrefix}-data-kv-pe'
    location: location
    subnetId: privateLinkVnet.outputs.subnets[0].id
    privateLinkServiceId: dataKeyVault.id
    groupIds: ['vault']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.vaultcore.azure.net')].outputs.id
  }
  dependsOn: [
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
    subnetId: privateLinkVnet.outputs.subnets[0].id
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Sql/servers', sqlServerName)
    groupIds: ['sqlServer']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.database.windows.net')].outputs.id
  }
  dependsOn: [
    dnsZonePrivateLinkLinks
  ]
}

module replicaSqlPrivateEndpoint 'privateEndpoint.bicep' = {
  name: 'deploy-${namePrefix}-replica-sql-pe'
  params: {
    name: '${namePrefix}-replica-sql-pe'
    location: location
    subnetId: privateLinkVnet.outputs.subnets[0].id
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Sql/servers', replicaSqlServerName)
    groupIds: ['sqlServer']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.database.windows.net')].outputs.id
  }
  dependsOn: [
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
    subnetId: privateLinkVnet.outputs.subnets[0].id
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Storage/storageAccounts', jobsStorageAccountName)
    groupIds: ['blob']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.blob.core.windows.net')].outputs.id
  }
  dependsOn: [
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
    subnetId: privateLinkVnet.outputs.subnets[0].id
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Storage/storageAccounts', functionsStorageAccountName)
    groupIds: ['blob']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.blob.core.windows.net')].outputs.id
  }
  dependsOn: [
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
    subnetId: privateLinkVnet.outputs.subnets[0].id
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Storage/storageAccounts', functionsStorageAccountName)
    groupIds: ['table']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.table.core.windows.net')].outputs.id
  }
  dependsOn: [
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
    subnetId: privateLinkVnet.outputs.subnets[0].id
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.Storage/storageAccounts', functionsStorageAccountName)
    groupIds: ['queue']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.queue.core.windows.net')].outputs.id
  }
  dependsOn: [
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
    subnetId: privateLinkVnet.outputs.subnets[0].id
    privateLinkServiceId: resourceId(dataResourceGroupName, 'Microsoft.AppConfiguration/configurationStores', appConfigurationName)
    groupIds: ['configurationStores']
    privateDnsZoneId: dnsZones[indexOf(privateDnsZoneNames, 'privatelink.azconfig.io')].outputs.id
  }
  dependsOn: [
    dnsZonePrivateLinkLinks
  ]
}

// =====================================================================================
// Outputs
// =====================================================================================

output bastionVnetId string = deployBastion ? bastionVnet.outputs.id : existingBastionVnetId
output managementVnetId string = managementVnet.outputs.id
output managementVnetName string = managementVnet.outputs.name
output privateLinkVnetId string = privateLinkVnet.outputs.id
output privateLinkVnetName string = privateLinkVnet.outputs.name
output managementSubnetId string = managementVnet.outputs.subnets[0].id
output privateEndpointSubnetId string = privateLinkVnet.outputs.subnets[0].id
