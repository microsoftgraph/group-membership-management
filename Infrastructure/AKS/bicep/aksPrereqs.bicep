targetScope = 'resourceGroup'

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Resource location.')
param location string

@description('Services to create a workload identity + federated credential for.')
param services array = [
  {
    name: 'JobTrigger'
    serviceAccount: 'jobtrigger-sa'
  }
]

@description('When true, this deployment also creates the role assignments.')
param setRBACPermissions bool = false

@description('Registry SKU.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param acrSku string = 'Basic'

@description('Number of days an untagged manifest is kept before the scheduled purge task deletes it.')
param purgeAfterDays int = 30

@description('Cron schedule for the purge task.')
param purgeSchedule string = '0 3 * * 0'

@description('Address space for the AKS VNet.')
param aksVnetAddressPrefix string = '10.224.0.0/16'

@description('Address prefix for the AKS node subnet.')
param aksNodeSubnetAddressPrefix string = '10.224.0.0/22'

@description('IP tags applied to the AKS outbound public IP.')
param aksOutboundIpTags array = []

var _aksClusterName    = '${solutionAbbreviation}-compute-${environmentAbbreviation}-aks'
var _kubeletIdentityName = '${_aksClusterName}-kubelet-identity'
var _aksVnetName       = '${_aksClusterName}-vnet'
var _aksNodeSubnetName = 'aks-nodes'
var _acrName           = '${solutionAbbreviation}computeacr${environmentAbbreviation}' // 5-50 chars, alphanumeric only, globally unique.

var _acrPullRoleId               = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var _networkContributorRoleId    = '4d97b98b-1d4f-4787-a291-c67834d212e7'
var _managedIdentityOperatorRole = 'f1a07417-d97a-45cb-824c-7a7467783830'

resource kubeletIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: _kubeletIdentityName
  location: location
}

resource aksControlPlaneIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${_aksClusterName}-identity'
  location: location
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: _acrName
  location: location
  sku: {
    name: acrSku
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    policies: {
      quarantinePolicy: {
        status: 'disabled'
      }
      trustPolicy: {
        type: 'Notary'
        status: 'disabled'
      }
      retentionPolicy: {
        days: purgeAfterDays
        status: 'disabled'
      }
    }
  }
}

resource purgeTask 'Microsoft.ContainerRegistry/registries/tasks@2019-06-01-preview' = {
  parent: acr
  name: 'purge-old-images'
  location: location
  properties: {
    status: 'Enabled'
    platform: {
      os: 'Linux'
      architecture: 'amd64'
    }
    agentConfiguration: {
      cpu: 2
    }
    timeout: 3600
    step: {
      type: 'EncodedTask'
      encodedTaskContent: base64('''
version: v1.1.0
steps:
  - cmd: acr purge --filter "gmm/.*:.*" --untagged --ago ${{ .Values.purgeAfterDays }}d --keep 3
    disableWorkingDirectoryOverride: true
    timeout: 3600
''')
      encodedValuesContent: base64('purgeAfterDays: ${purgeAfterDays}\n')
    }
    trigger: {
      timerTriggers: [
        {
          name: 'weekly'
          schedule: purgeSchedule
          status: 'Enabled'
        }
      ]
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

resource aksNodeNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: '${_aksClusterName}-nodes-nsg'
  location: location
}

resource aksVnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: _aksVnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        aksVnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: _aksNodeSubnetName
        properties: {
          addressPrefix: aksNodeSubnetAddressPrefix
          defaultOutboundAccess: false
          networkSecurityGroup: {
            id: aksNodeNsg.id
          }
        }
      }
    ]
  }
}

resource aksNodeSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: aksVnet
  name: _aksNodeSubnetName
}

resource aksOutboundPip 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: '${_aksClusterName}-outbound-pip'
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
    ipTags: aksOutboundIpTags
  }
}

resource kubeletAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (setRBACPermissions) {
  name: guid(acr.id, kubeletIdentity.id, _acrPullRoleId)
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', _acrPullRoleId)
    principalId: kubeletIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource controlPlaneManagedIdentityOperator 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (setRBACPermissions) {
  name: guid(kubeletIdentity.id, aksControlPlaneIdentity.id, _managedIdentityOperatorRole)
  scope: kubeletIdentity
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', _managedIdentityOperatorRole)
        principalId: aksControlPlaneIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource controlPlaneNodeSubnetNetworkContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (setRBACPermissions) {
  name: guid(aksNodeSubnet.id, aksControlPlaneIdentity.id, _networkContributorRoleId)
  scope: aksNodeSubnet
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', _networkContributorRoleId)
        principalId: aksControlPlaneIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource workloadIdentities 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = [for svc in services: {
  name: '${solutionAbbreviation}-identity-${environmentAbbreviation}-aks-${svc.name}'
  location: location
}]
