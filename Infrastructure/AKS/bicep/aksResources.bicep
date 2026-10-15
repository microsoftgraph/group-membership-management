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

@description('Object ID of the Entra security group whose members become Kubernetes cluster-admins.')
param aksAdministratorsGroupId string

@description('Whether to use the production AKS control-plane tier.')
param isProduction bool = false

@description('Overlay pod IPv4 /19; missing or empty uses project default 10.244.0.0/19. Override with a valid non-overlapping /19 and audit enterprise routes before deployment.')
param aksPodCidr string = '10.244.0.0/19'

@description('Kubernetes namespace the workloads run in.')
param namespaceName string = 'gmm'

@description('Services to create a federated credential for.')
param services array = [
  'JobTrigger'
]




var _aksClusterName = '${solutionAbbreviation}-compute-${environmentAbbreviation}-aks'
var _dataResourceGroupName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var _logAnalyticsName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var _aksOutboundPipName = '${_aksClusterName}-outbound-pip'
var _aksControlPlaneIdentityName = '${_aksClusterName}-identity'
var _kubeletIdentityName = '${_aksClusterName}-kubelet-identity'
var _aksVnetName = '${_aksClusterName}-vnet'
var _aksNodeSubnetName = 'aks-nodes'
var _aksNodeSubnetId = resourceId('Microsoft.Network/virtualNetworks/subnets', _aksVnetName, _aksNodeSubnetName)

var _containerInsightsDcrName = '${_aksClusterName}-dcr'

var _containerInsightsStreams = [
  'Microsoft-ContainerLogV2'
  'Microsoft-KubeEvents'
  'Microsoft-KubePodInventory'
  'Microsoft-KubeMonAgentEvents'
  'Microsoft-Perf'
]

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: _logAnalyticsName
  scope: resourceGroup(_dataResourceGroupName)
}

resource aksControlPlaneIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: _aksControlPlaneIdentityName
}

resource kubeletIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: _kubeletIdentityName
}

resource aksOutboundPip 'Microsoft.Network/publicIPAddresses@2024-05-01' existing = {
  name: _aksOutboundPipName
}

resource aks 'Microsoft.ContainerService/managedClusters@2024-09-01' = {
  name: _aksClusterName
  location: location
  sku: {
    name: 'Base'
    tier: isProduction ? 'Standard' : 'Free'
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${aksControlPlaneIdentity.id}': {}
    }
  }
  properties: {
    dnsPrefix: _aksClusterName
    disableLocalAccounts: true
    enableRBAC: true
    aadProfile: {
      managed: true
      enableAzureRBAC: true
      adminGroupObjectIDs: [
        aksAdministratorsGroupId
      ]
    }
    oidcIssuerProfile: {
      enabled: true
    }
    identityProfile: {
      kubeletidentity: {
        resourceId: kubeletIdentity.id
        clientId: kubeletIdentity.properties.clientId
        objectId: kubeletIdentity.properties.principalId
      }
    }
    agentPoolProfiles: [
      {
        name: 'systempool'
        count: 2
        vmSize: 'Standard_D4s_v5'
        mode: 'System'
        enableAutoScaling: false
        maxPods: 50
        nodeTaints: [
          'CriticalAddonsOnly=true:NoSchedule'
        ]
        availabilityZones: [
          '1'
        ]
        upgradeSettings: {
          maxSurge: '1'
        }
        osType: 'Linux'
        osSKU: 'AzureLinux3'
        type: 'VirtualMachineScaleSets'
        vnetSubnetID: _aksNodeSubnetId
      }
      {
        name: 'apppool'
        count: 2
        vmSize: 'Standard_D4s_v5'
        mode: 'User'
        enableAutoScaling: true
        minCount: 2
        maxCount: 8
        maxPods: 30
        availabilityZones: [
          '1'
        ]
        upgradeSettings: {
          maxSurge: '1'
        }
        osType: 'Linux'
        osSKU: 'AzureLinux3'
        type: 'VirtualMachineScaleSets'
        vnetSubnetID: _aksNodeSubnetId
      }
    ]
    networkProfile: {
      networkPlugin: 'azure'
      networkPluginMode: 'overlay'
      networkDataplane: 'cilium'
      networkPolicy: 'cilium'
      podCidr: aksPodCidr == '' ? '10.244.0.0/19' : aksPodCidr
      outboundType: 'loadBalancer'
      loadBalancerSku: 'standard'
      loadBalancerProfile: {
        outboundIPs: {
          publicIPs: [
            {
              id: aksOutboundPip.id
            }
          ]
        }
      }
    }
    addonProfiles: {
      azurepolicy: {
        enabled: true
      }
      azureKeyvaultSecretsProvider: {
        enabled: true
      }
      omsagent: {
        enabled: true
        config: {
          logAnalyticsWorkspaceResourceID: logAnalytics.id
          useAADAuth: 'true'
        }
      }
    }
    workloadAutoScalerProfile: {
      keda: {
        enabled: true
      }
    }
    securityProfile: {
      defender: {
        logAnalyticsWorkspaceResourceId: logAnalytics.id
        securityMonitoring: {
          enabled: true
        }
      }
      workloadIdentity: {
        enabled: true
      }
      imageCleaner: {
        enabled: true
        intervalHours: 168
      }
    }
  }
}

resource containerInsightsDcr 'Microsoft.Insights/dataCollectionRules@2023-03-11' = {
  name: _containerInsightsDcrName
  location: location
  kind: 'Linux'
  properties: {
    dataSources: {
      extensions: [
        {
          name: 'ContainerInsightsExtension'
          extensionName: 'ContainerInsights'
          streams: _containerInsightsStreams
          extensionSettings: {
            dataCollectionSettings: {
              interval: '1m'
              namespaceFilteringMode: 'Include'
              namespaces: [
                namespaceName
              ]
              enableContainerLogV2: true
            }
          }
        }
      ]
    }
    destinations: {
      logAnalytics: [
        {
          name: 'ciworkspace'
          workspaceResourceId: logAnalytics.id
        }
      ]
    }
    dataFlows: [
      {
        streams: _containerInsightsStreams
        destinations: [
          'ciworkspace'
        ]
      }
    ]
  }
}

resource containerInsightsDcra 'Microsoft.Insights/dataCollectionRuleAssociations@2023-03-11' = {
  name: 'ContainerInsightsExtension'
  scope: aks
  properties: {
    dataCollectionRuleId: containerInsightsDcr.id
  }
}

resource workloadIdentities 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = [for svc in services: {
  name: '${solutionAbbreviation}-identity-${environmentAbbreviation}-aks-${svc}'
}]

resource federatedCredentials 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = [for (svc, i) in services: {
  parent: workloadIdentities[i]
  name: '${namespaceName}-${toLower(svc)}-sa'
  properties: {
    issuer: aks.properties.oidcIssuerProfile.issuerURL
    subject: 'system:serviceaccount:${namespaceName}:${toLower(svc)}-sa'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}]
