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

@description('Node pool VM size.')
param nodeVmSize string = 'Standard_D4s_v5'

@description('System node pool count.')
@minValue(1)
@maxValue(100)
param nodeCount int = 2

@description('AKS control-plane tier.')
@allowed([
  'Free'
  'Standard'
])
param aksSkuTier string = 'Free'

@description('Kubernetes namespace the workloads run in.')
param namespaceName string = 'gmm'

@description('Services to create a federated credential for.')
param services array = [
  {
    name: 'JobTrigger'
    serviceAccount: 'jobtrigger-sa'
  }
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
    tier: aksSkuTier
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
        count: nodeCount
        vmSize: nodeVmSize
        mode: 'System'
        osType: 'Linux'
        osSKU: 'AzureLinux3'
        type: 'VirtualMachineScaleSets'
        vnetSubnetID: _aksNodeSubnetId
      }
    ]
    networkProfile: {
      networkPlugin: 'azure'
      networkPolicy: 'azure'
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

resource workloadIdentities 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = [for svc in services: {
  name: '${solutionAbbreviation}-identity-${environmentAbbreviation}-aks-${svc.name}'
}]

resource federatedCredentials 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = [for (svc, i) in services: {
  parent: workloadIdentities[i]
  name: '${namespaceName}-${svc.serviceAccount}'
  properties: {
    issuer: aks.properties.oidcIssuerProfile.issuerURL
    subject: 'system:serviceaccount:${namespaceName}:${svc.serviceAccount}'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}]


