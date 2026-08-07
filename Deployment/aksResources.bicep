// ============================================================================
// aksResources.bicep — GMM shared AKS cluster (empty, S360-compliant)
// ============================================================================
// Deploys a hardened, empty AKS cluster into the compute resource group
// `<solutionAbbreviation>-compute-<environmentAbbreviation>`. No workloads,
// no ACR, no kubectl/Helm — just the cluster.
//
// S360 items covered:
//   - Azure Linux 3.0 nodepool (no Ubuntu CVE surface)
//   - Standard Load Balancer egress via a BYO *tagged* outbound public IP
//     (ipTags FirstPartyUsage=/GroupMembershipManagement) so the outbound IP
//     satisfies the S360 IP-tagging KPI in every environment
//   - Local accounts disabled + Managed AAD + Azure RBAC
//   - OIDC issuer + Workload Identity
//   - Azure Policy add-on
//   - Microsoft Defender for Containers (wired to the shared
//     `<solutionAbbreviation>-data-<environmentAbbreviation>` Log Analytics
//     workspace created in the data stage)
//   - Image Cleaner (weekly)
//   - Azure network policy
//   - Bring-your-own node subnet with defaultOutboundAccess:false + an NSG, so
//     the cluster does NOT create an AKS-managed VNet whose subnets get flagged
//     by S360 (SFI-NS2.6.1 "Service has Subnets with Default Outbound Access").
//     Egress stays on the Standard LB + tagged outbound IP (explicit outbound
//     method), so disabling default outbound access is safe.
//
// Explicitly deferred (add when workloads are onboarded):
//   - ACR + AcrPull for kubelet identity
//   - Auto-scaling on the node pool
//   - apiServerAccessProfile.authorizedIPRanges (needs corp NAT IPs)
//   - Private cluster / private-endpoint API server (the node subnet is now
//     BYO, but the API server is still public)
//   - Encryption at host (requires a VM SKU that supports it — B2s does not)
//   - Auto-upgrade channel (K8s version upgrades are currently manual)
//   - Diagnostic settings routing control-plane logs to Log Analytics
//   - MC_* managed-RG Contributor grant to service connection SPN
// ============================================================================

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

@description('Optional override for the AKS cluster name. When empty, defaults to `<solutionAbbreviation>-compute-<environmentAbbreviation>-aks`.')
param aksClusterName string = ''

@description('Object ID of the Entra security group whose members become Kubernetes cluster-admins. When empty, falls back to `sqlAdministratorsGroupId` so the same team that administers SQL also administers AKS.')
param aksAdministratorsGroupId string = ''

@description('Object ID of the SQL administrators security group. Also used as the fallback source for `aksAdministratorsGroupId`.')
param sqlAdministratorsGroupId string

@description('Node pool VM size. Standard_B2s for cheap/dev; Standard_D*s_v5 for prod (also needed to enable encryption at host later).')
param nodeVmSize string = 'Standard_B2s'

@description('System node pool count. An empty cluster runs comfortably on 1 node.')
@minValue(1)
@maxValue(100)
param nodeCount int = 1

@description('AKS control-plane tier. Free = no SLA (LP/dev). Standard = 99.95% uptime SLA (prod-ready).')
@allowed([
  'Free'
  'Standard'
])
param aksSkuTier string = 'Free'

@description('Master switch — set to true to deploy the AKS cluster. Defaults to false so no environment provisions AKS unless it explicitly opts in via its per-env parameters file.')
param deployAks bool = false

@description('When true, this deployment creates the Network Contributor role assignment on the outbound IP. Kept false in every GMM environment so the least-privilege deploy service principal (Contributor only) never needs Microsoft.Authorization/roleAssignments/write on the compute resource group (S360: SPNs must follow least privilege). When false, the grant is pre-provisioned out-of-band by an elevated human once per environment — mirroring how the rest of GMM grants managed-identity RBAC via Scripts/PostDeploymentRoleAssignments.')
param setRBACPermissions bool = false

@description('Enable Microsoft Defender for Containers. Recommended on for all envs; disable only during initial bootstrap.')
param enableDefenderForContainers bool = true

@description('Disable Kubernetes local (non-AAD) accounts. Keep true for S360 compliance. Set false ONLY during initial LP bootstrap or if you are locked out and need to recover via local kubeconfig.')
param disableLocalAccounts bool = true

@description('IP tags applied to the AKS outbound public IP at creation. Defaults to the GMM FirstPartyUsage virtual tag so every AKS cluster is born S360 IP-tagging compliant. Static literal (not an expression) so it survives the pipeline\'s verbatim default-value copy. Override per-env only if a different tag is required.')
param aksOutboundIpTags array = [
  {
    ipTagType: 'FirstPartyUsage'
    tag: '/GroupMembershipManagement'
  }
]

@description('Address space for the BYO AKS VNet. Kept under IaC control so the node subnet can set defaultOutboundAccess:false (S360 SFI-NS2.6.1) instead of relying on an AKS-managed VNet. Must not overlap any VNet this cluster is peered with.')
param aksVnetAddressPrefix string = '10.224.0.0/16'

@description('Address prefix for the AKS node subnet. Azure CNI draws pod IPs from this subnet, so size for nodes x (maxPods+1): /22 (~1000 usable) covers ~32 nodes at the default 30 pods/node.')
param aksNodeSubnetAddressPrefix string = '10.224.0.0/22'

// ----------------------------------------------------------------------------
// Derived names (var, not expression-default param — expression-default params
// are broken with the pipeline's parameter handling: Get-TemplateParameters in
// Deploy-Resources.ps1 copies defaultValue verbatim into the supplied payload,
// and ARM does not evaluate expressions in supplied parameter values.)
// ----------------------------------------------------------------------------
var _resolvedAksClusterName = empty(aksClusterName) ? '${solutionAbbreviation}-compute-${environmentAbbreviation}-aks' : aksClusterName
var _resolvedAksAdministratorsGroupId = empty(aksAdministratorsGroupId) ? sqlAdministratorsGroupId : aksAdministratorsGroupId
var _dataResourceGroupName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var _logAnalyticsName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var _aksOutboundPipName = '${_resolvedAksClusterName}-outbound-pip'
var _aksControlPlaneIdentityName = '${_resolvedAksClusterName}-identity'
var _aksVnetName = '${_resolvedAksClusterName}-vnet'
var _aksNodeNsgName = '${_resolvedAksClusterName}-nodes-nsg'
var _aksNodeSubnetName = 'aks-nodes'
// Computed resourceId string (not a symbolic ref) so the cluster can consume it
// without ARM trying to evaluate a conditional resource property; the explicit
// dependsOn on aksVnet below guarantees the subnet exists first.
var _aksNodeSubnetId = resourceId('Microsoft.Network/virtualNetworks/subnets', _aksVnetName, _aksNodeSubnetName)
// Network Contributor — the minimum role the AKS control-plane identity needs
// to attach the BYO outbound public IP to the load balancer AKS manages.
var _networkContributorRoleId = '4d97b98b-1d4f-4787-a291-c67834d212e7'

// ----------------------------------------------------------------------------
// Cross-RG reference to the existing shared Log Analytics workspace
// (created in the data stage by dataResources.bicep -> Infrastructure/data/template.bicep).
// `existing` + `.id` produces a computed resourceId string; if the workspace
// does not exist, the AKS resource provider fails loudly at deploy time
// (verified via bicep-compile inspection — no silent misconfig).
// ----------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = if (deployAks && enableDefenderForContainers) {
  name: _logAnalyticsName
  scope: resourceGroup(_dataResourceGroupName)
}

// ----------------------------------------------------------------------------
// AKS control-plane user-assigned identity.
// A user-assigned (not system-assigned) identity is REQUIRED because the
// identity must already hold Network Contributor on the BYO outbound public IP
// *before* the cluster is created. A system-assigned identity does not exist
// until creation, so it cannot be pre-authorized (Azure rejects the cluster).
// ----------------------------------------------------------------------------
resource aksControlPlaneIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' = if (deployAks) {
  name: _aksControlPlaneIdentityName
  location: location
}

// ----------------------------------------------------------------------------
// AKS outbound public IP — tagged at creation for the S360 IP-tagging KPI.
// `ipTags` are creation-only, so cluster egress MUST use this pre-tagged BYO IP
// rather than an AKS-managed outbound IP (which is always created UNtagged in
// the MC_* node resource group and gets flagged by S360).
// ----------------------------------------------------------------------------
resource aksOutboundPip 'Microsoft.Network/publicIPAddresses@2024-05-01' = if (deployAks) {
  name: _aksOutboundPipName
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

// ----------------------------------------------------------------------------
// Grant the control-plane identity Network Contributor on the outbound IP so
// AKS can attach it to the managed load balancer. Scoped to the IP (least
// privilege).
//
// Gated on `setRBACPermissions` (in addition to `deployAks`) so AKS matches
// every other GMM host: the least-privilege deploy service principal
// (Contributor only) never creates role assignments — doing so requires
// Microsoft.Authorization/roleAssignments/write on the compute RG, which S360
// flags. `setRBACPermissions` is false in every environment, so this grant is
// instead pre-provisioned out-of-band by an elevated human once per environment
// (before the cluster is deployed), mirroring Scripts/PostDeploymentRoleAssignments.
// When set true (an elevated identity runs the deploy), the cluster's dependsOn
// below ensures the grant lands before the cluster is created.
// ----------------------------------------------------------------------------
resource aksOutboundPipRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployAks && setRBACPermissions) {
  name: guid(aksOutboundPip.id, aksControlPlaneIdentity.id, _networkContributorRoleId)
  scope: aksOutboundPip
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', _networkContributorRoleId)
    // Safe: this role assignment is deployed only when deployAks is true, and
    // aksControlPlaneIdentity shares the deployAks condition, so the identity
    // always exists here (BCP318 can't prove that across two resources).
    #disable-next-line BCP318
    principalId: aksControlPlaneIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ----------------------------------------------------------------------------
// BYO node networking (S360 SFI-NS2.6.1).
// Owning the VNet/subnet in IaC lets us set defaultOutboundAccess:false on the
// node subnet, so the cluster stops creating an AKS-managed VNet whose subnets
// are flagged for default outbound access. An NSG is attached so the subnet is
// not flagged for "subnet without NSG" either. Egress remains on the Standard
// LB + tagged outbound IP (explicit outbound), so default outbound access is
// unused and safe to disable.
// ----------------------------------------------------------------------------
resource aksNodeNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = if (deployAks) {
  name: _aksNodeNsgName
  location: location
}

resource aksVnet 'Microsoft.Network/virtualNetworks@2024-05-01' = if (deployAks) {
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

// ----------------------------------------------------------------------------
// AKS managed cluster
// ----------------------------------------------------------------------------
resource aks 'Microsoft.ContainerService/managedClusters@2024-09-01' = if (deployAks) {
  name: _resolvedAksClusterName
  location: location
  // Explicit: when this deploy creates the Network Contributor grant
  // (setRBACPermissions=true), it must land before the cluster is created
  // (implicit refs cover the identity + IP but not the role assignment). When
  // setRBACPermissions=false the grant is pre-provisioned out-of-band, so the
  // cluster only needs to wait on the BYO VNet/subnet.
  dependsOn: setRBACPermissions ? [
    aksOutboundPipRoleAssignment
    aksVnet
  ] : [
    aksVnet
  ]
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
    dnsPrefix: _resolvedAksClusterName
    disableLocalAccounts: disableLocalAccounts
    enableRBAC: true
    aadProfile: {
      managed: true
      enableAzureRBAC: true
      adminGroupObjectIDs: [
        _resolvedAksAdministratorsGroupId
      ]
    }
    oidcIssuerProfile: {
      enabled: true
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
        // BYO node subnet (defaultOutboundAccess:false) so AKS does not create
        // a managed VNet whose subnets are flagged by S360 (SFI-NS2.6.1).
        vnetSubnetID: _aksNodeSubnetId
      }
    ]
    networkProfile: {
      networkPlugin: 'azure'
      networkPolicy: 'azure'
      // Standard LB egress through a BYO *tagged* public IP so the outbound IP
      // is S360 IP-tagging compliant (AKS-managed outbound IPs are always
      // created untagged). One public IP ≈ 64K SNAT ports; add more outbound
      // IPs (or migrate to a userAssignedNATGateway) before enabling
      // egress-heavy workloads.
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
    }
    securityProfile: {
      defender: enableDefenderForContainers ? {
        logAnalyticsWorkspaceResourceId: logAnalytics.id
        securityMonitoring: {
          enabled: true
        }
      } : null
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

// No outputs.
// `reference()` on a conditional resource is not short-circuited by ARM `if()`,
// so any output that reads a property of `aks` (e.g., `nodeResourceGroup`) will
// evaluate — and potentially error — even when `deployAks = false`. Since no
// caller currently consumes these outputs, they are omitted.
