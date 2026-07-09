// common parameters
param location string
param aiLocation string
param environmentAbbreviation string
param solutionAbbreviation string
param tenantId string
param functionAuthAppClientId string
param enableFunctionAuthentication bool = false
param managedResourceGroupName string = ''
param isManagedApplication bool = false
param appConfigurationName string
param setRBACPermissions bool

@description('When true, networking resources (private endpoints) under \'webApiComputeResources\' are skipped.')
param skipNetworkingDeployment bool = true

// UI parameters
param customDomainName string = ''
param apiServiceBaseUri string
param uiLocation string
param branch string = 'not-set'
param repositoryUrl string = 'https://url'

// API parameters
param pipeline string

//WebAPI, Notifier
param apiHostname string = ''
var resolvedApiHostname = apiHostname == '' ? '${solutionAbbreviation}-compute-${environmentAbbreviation}-webapi.azurewebsites.net' : apiHostname

// Message Splitter
param availableMessageSplitterSubscriptions array = [
  {
    name: 's1'
    subscription: 'Small'
  }
  {
    name: 'l1'
    subscription: 'Large'
  }
]

//AzureUserReader
param storageAccountSecretName string = 'adfStorageAccountName'

// GraphUpdater
param concurrentAddRequests int = 1
param concurrentRemoveRequests int = 1


// Used by: JobTrigger, DestinationAttributesUpdater, AzureUserReader, Notifier, JobScheduler, WebApi, NonProdService, GraphUpdater
param featureFlags object = {
  skipListingFunctionAppKeys : true
  enableTeamsChannel: false
  enableOpenAI: false
}

@description('When true, attaches every public function/app-service to its delegated function-integration subnet in the consolidated Resources VNET (FC1 / Microsoft.App/environments). Default false preserves pre-feature behavior.')
param enableFunctionVnetIntegration bool = false

@description('Resource group hosting the consolidated Resources VNET (created by the networking deployment). When empty, defaults to `<solutionAbbreviation>-networking-<environmentAbbreviation>`.')
param networkingResourceGroupName string = ''

@description('Name of the consolidated Resources VNET. When empty, defaults to `<solutionAbbreviation>-networking-<environmentAbbreviation>-resources-vnet` (matches the networking template).')
param resourcesVnetName string = ''

// -------------------- Per-host Flex Consumption maximumInstanceCount caps --------------------
// Single map of per-host Flex Consumption maximumInstanceCount caps, sized for the default
// 250-core-per-subscription-per-region quota so that no single app can monopolize the regional
// quota while idle apps cannot waste it. Deployments with a larger quota can raise individual
// caps by supplying a maxInstanceCountOverrides object; only the keys present there replace the
// defaults below (union() overlay), so unspecified hosts keep their default.
@description('Per-host Flex maximumInstanceCount overrides (host key -> cap). Only supplied keys override the defaults; unspecified hosts keep their default.')
param maxInstanceCountOverrides object = {}

var defaultMaxInstanceCounts = {
  autoApprover: 25
  azureMaintenance: 10
  azureUserReader: 4
  destinationAttributesUpdater: 6
  graphUpdater: 45
  graphUpdaterSmall: 15
  graphUpdaterLarge: 45
  groupMembershipObtainer: 45
  groupOwnershipObtainer: 10
  jobScheduler: 10
  jobTrigger: 15
  membershipAggregator: 45
  messageSplitterS1: 50
  messageSplitterL1: 45
  nonProdService: 4
  notifier: 15
  placeMembershipObtainer: 6
  sqlDataChecker: 6
  sqlMembershipObtainer: 45
  syncJobUpdater: 10
  teamsChannelMembershipObtainer: 10
  teamsChannelUpdater: 10
}

// Public defaults overlaid with any per-environment overrides (override keys win).
var maxInstanceCounts = union(defaultMaxInstanceCounts, maxInstanceCountOverrides)

var _resolvedNetworkingResourceGroupName = empty(networkingResourceGroupName) ? '${solutionAbbreviation}-networking-${environmentAbbreviation}' : networkingResourceGroupName
var _resolvedResourcesVnetName = empty(resourcesVnetName) ? '${solutionAbbreviation}-networking-${environmentAbbreviation}-resources-vnet' : resourcesVnetName

// Helper: compute the subnet ID for a given public function short name.
// Subnet names follow `func-pub-<name>` for populated indices in [0, 18]
// (indices 19..59 are reserved but unallocated; 
func publicFunctionSubnetId(subscriptionId string, networkingRg string, vnetName string, shortName string) string => resourceId(subscriptionId, networkingRg, 'Microsoft.Network/virtualNetworks/subnets', vnetName, 'func-pub-${shortName}')

var _vnetSubnetIdAutoApprover                = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'autoapprover') : ''
var _vnetSubnetIdAzureMaintenance            = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'azuremaintenance') : ''
var _vnetSubnetIdAzureUserReader             = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'azureuserreader') : ''
var _vnetSubnetIdDestinationAttributesUpdater = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'destinationattributesupdater') : ''
var _vnetSubnetIdGraphUpdater                = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'graphupdater') : ''
var _vnetSubnetIdGroupMembershipObtainer     = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'groupmembershipobtainer') : ''
var _vnetSubnetIdGroupOwnershipObtainer      = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'groupownershipobtainer') : ''
var _vnetSubnetIdJobScheduler                = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'jobscheduler') : ''
var _vnetSubnetIdJobTrigger                  = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'jobtrigger') : ''
var _vnetSubnetIdMembershipAggregator        = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'membershipaggregator') : ''
var _vnetSubnetIdMessageSplitter             = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'messagesplitter') : ''
var _vnetSubnetIdNonProdService              = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'nonprodservice') : ''
var _vnetSubnetIdNotifier                    = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'notifier') : ''
var _vnetSubnetIdPlaceMembershipObtainer     = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'placemembershipobtainer') : ''
var _vnetSubnetIdSqlDataChecker              = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'sqldatachecker') : ''
var _vnetSubnetIdSqlMembershipObtainer       = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'sqlmembershipobtainer') : ''
var _vnetSubnetIdSyncJobUpdater              = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'syncjobupdater') : ''
var _vnetSubnetIdTeamsChannelMembershipObtainer = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'teamschannelmembershipobtainer') : ''
var _vnetSubnetIdTeamsChannelUpdater         = enableFunctionVnetIntegration ? publicFunctionSubnetId(subscription().subscriptionId, _resolvedNetworkingResourceGroupName, _resolvedResourcesVnetName, 'teamschannelupdater') : ''


var prereqsResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'
var dataResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-data-${environmentAbbreviation}'
var computeResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-compute-${environmentAbbreviation}'

// function resources
// ----------------- JobTrigger
module jobTriggerDataResources '../Service/GroupMembershipManagement/Hosts/JobTrigger/Infrastructure/data/template.bicep' = {
  name: 'jobTriggerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module jobTriggerComputeResources '../Service/GroupMembershipManagement/Hosts/JobTrigger/Infrastructure/compute/template.bicep' = {
  name: 'jobTriggerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.jobTrigger
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdJobTrigger
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
  }
  dependsOn: [
    jobTriggerDataResources
  ]
}

// ----------------- DestinationAttributesUpdater
module destinationAttributesUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/destinationAttributesUpdater/Infrastructure/data/template.bicep' = {
  name: 'destinationAttributesUpdaterDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module destinationAttributesUpdaterComputeResources '../Service/GroupMembershipManagement/Hosts/destinationAttributesUpdater/Infrastructure/compute/template.bicep' = {
  name: 'destinationAttributesUpdaterComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.destinationAttributesUpdater
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdDestinationAttributesUpdater
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
  }
  dependsOn: [
    destinationAttributesUpdaterDataResources
  ]
}

// ----------------- GroupMembershipObtainer
module groupMembershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/GroupMembershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'groupMembershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module groupMembershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/GroupMembershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'groupMembershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.groupMembershipObtainer
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdGroupMembershipObtainer
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    groupMembershipObtainerDataResources
  ]
}

// ----------------- SqlMembershipObtainer
module sqlMembershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/SqlMembershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'sqlMembershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module sqlMembershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/SqlMembershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'sqlMembershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.sqlMembershipObtainer
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdSqlMembershipObtainer
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    authority: 'https://login.windows.net/${tenantId}'
    subscriptionId: subscription().subscriptionId
    pipeline: pipeline
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    sqlMembershipObtainerDataResources
  ]
}

// ----------------- GroupOwnershipObtainer
module groupOwnershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/GroupOwnershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'groupOwnershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module groupOwnershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/GroupOwnershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'groupOwnershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.groupOwnershipObtainer
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdGroupOwnershipObtainer
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    groupOwnershipObtainerDataResources
  ]
}

// ----------------- PlaceMembershipObtainer
module placeMembershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/PlaceMembershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'placeMembershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module placeMembershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/PlaceMembershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'placeMembershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.placeMembershipObtainer
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdPlaceMembershipObtainer
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    placeMembershipObtainerDataResources
  ]
}

// ----------------- TeamsChannelMembershipObtainer
module teamsChannelMembershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/TeamsChannelMembershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'teamsChannelMembershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module teamsChannelMembershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/TeamsChannelMembershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'teamsChannelMembershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.teamsChannelMembershipObtainer
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdTeamsChannelMembershipObtainer
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    teamsChannelMembershipObtainerDataResources
  ]
}

// ----------------- MembershipAggregator
module membershipAggregatorDataResources '../Service/GroupMembershipManagement/Hosts/MembershipAggregator/Infrastructure/data/template.bicep' = {
  name: 'membershipAggregatorDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module membershipAggregatorComputeResources '../Service/GroupMembershipManagement/Hosts/MembershipAggregator/Infrastructure/compute/template.bicep' = {
  name: 'membershipAggregatorComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.membershipAggregator
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdMembershipAggregator
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    membershipAggregatorDataResources
  ]
}

// ----------------- GraphUpdater

var guinstanceIds = [
  ''
  'small'
  'large'
]

module graphUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/GraphUpdater/Infrastructure/data/template.bicep' = [for instance in guinstanceIds: {
  name: instance == '' ? 'graphUpdaterDataResourcesTemplate' : 'graphUpdater${instance}DataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    instanceIdentifier: instance
  }
}]

module graphUpdaterComputeResources '../Service/GroupMembershipManagement/Hosts/GraphUpdater/Infrastructure/compute/template.bicep' = [for instance in guinstanceIds: {
  name: instance == '' ? 'graphUpdaterComputeResourcesTemplate' : 'graphUpdater${instance}ComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: instance == 'large' ? maxInstanceCounts.graphUpdaterLarge : (instance == 'small' ? maxInstanceCounts.graphUpdaterSmall : maxInstanceCounts.graphUpdater)
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdGraphUpdater
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    instanceIdentifier: instance
    concurrentAddRequests: concurrentAddRequests
    concurrentRemoveRequests: concurrentRemoveRequests
  }
  dependsOn: [
    graphUpdaterDataResources
  ]
}]


// ----------------- TeamsChannelUpdater
module teamsChannelUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/TeamsChannelUpdater/Infrastructure/data/template.bicep' = {
  name: 'teamsChannelUpdaterDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module teamsChannelUpdaterComputeResources '../Service/GroupMembershipManagement/Hosts/TeamsChannelUpdater/Infrastructure/compute/template.bicep' = {
  name: 'teamsChannelUpdaterComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.teamsChannelUpdater
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdTeamsChannelUpdater
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    teamsChannelUpdaterDataResources
  ]
}

// ----------------- NonProdService
module nonProdServiceDataResources '../Service/GroupMembershipManagement/Hosts/NonProdService/Infrastructure/data/template.bicep' = {
  name: 'nonProdServiceDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module nonProdServiceComputeResources '../Service/GroupMembershipManagement/Hosts/NonProdService/Infrastructure/compute/template.bicep' = {
  name: 'nonProdServiceComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.nonProdService
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdNonProdService
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    appConfigurationName: appConfigurationName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
  }
  dependsOn: [
    nonProdServiceDataResources
  ]
}

// ----------------- AzureUserReader
module azureUserReaderDataResources '../Service/GroupMembershipManagement/Hosts/AzureUserReader/Infrastructure/data/template.bicep' = {
  name: 'azureUserReaderDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module azureUserReaderComputeResources '../Service/GroupMembershipManagement/Hosts/AzureUserReader/Infrastructure/compute/template.bicep' = {
  name: 'azureUserReaderComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.azureUserReader
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdAzureUserReader
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    storageAccountSecretName: storageAccountSecretName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
  }
  dependsOn: [
    azureUserReaderDataResources
  ]
}

// ----------------- Notifier
module notifierDataResources '../Service/GroupMembershipManagement/Hosts/Notifier/Infrastructure/data/template.bicep' = {
  name: 'notifierDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module notifierComputeResources '../Service/GroupMembershipManagement/Hosts/Notifier/Infrastructure/compute/template.bicep' = {
  name: 'notifierComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.notifier
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdNotifier
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    apiHostname: resolvedApiHostname
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
  }
  dependsOn: [
    notifierDataResources
  ]
}

// ----------------- AutoApprover
module autoApproverDataResources '../Service/GroupMembershipManagement/Hosts/AutoApprover/Infrastructure/data/template.bicep' = {
  name: 'autoApproverDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module autoApproverComputeResources '../Service/GroupMembershipManagement/Hosts/AutoApprover/Infrastructure/compute/template.bicep' = {
  name: 'autoApproverComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.autoApprover
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdAutoApprover
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
  }
  dependsOn: [
    autoApproverDataResources
  ]
}

// ----------------- JobScheduler
module jobSchedulerDataResources '../Service/GroupMembershipManagement/Hosts/JobScheduler/Infrastructure/data/template.bicep' = {
  name: 'jobSchedulerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module jobSchedulerComputeResources '../Service/GroupMembershipManagement/Hosts/JobScheduler/Infrastructure/compute/template.bicep' = {
  name: 'jobSchedulerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.jobScheduler
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdJobScheduler
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
  }
  dependsOn: [
    jobSchedulerDataResources
  ]
}

// ----------------- SyncJobUpdater
module syncJobUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/SyncJobUpdater/Infrastructure/data/template.bicep' = {
  name: 'syncJobUpdaterDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module syncJobUpdaterComputeResources '../Service/GroupMembershipManagement/Hosts/SyncJobUpdater/Infrastructure/compute/template.bicep' = {
  name: 'syncJobUpdaterComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.syncJobUpdater
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdSyncJobUpdater
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    syncJobUpdaterDataResources
  ]
}

// ----------------- MessageSplitter instances
var instanceIds = [
  's1'
  'l1'
]

module messageSplitterDataResources '../Service/GroupMembershipManagement/Hosts/MessageSplitter/Infrastructure/data/template.bicep' = [for instance in instanceIds: {
  name: 'messageSplitter${instance}DataResources'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    instanceIdentifier: instance
  }
}]

module messageSplitterComputeResources '../Service/GroupMembershipManagement/Hosts/MessageSplitter/Infrastructure/compute/template.bicep' = [for instance in instanceIds: {
  name: 'messageSplitter${instance}ComputeResources'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: instance == 's1' ? maxInstanceCounts.messageSplitterS1 : maxInstanceCounts.messageSplitterL1
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdMessageSplitter
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    availableMessageSplitterSubscriptions: availableMessageSplitterSubscriptions
    instanceIdentifier: instance
  }
  dependsOn: [
    messageSplitterDataResources
  ]
}]

// ----------------- AzureMaintenance
module azureMaintenanceDataResources '../Service/GroupMembershipManagement/Hosts/AzureMaintenance/Infrastructure/data/template.bicep' = {
  name: 'azureMaintenanceDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module azureMaintenanceComputeResources '../Service/GroupMembershipManagement/Hosts/AzureMaintenance/Infrastructure/compute/template.bicep' = {
  name: 'azureMaintenanceComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.azureMaintenance
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdAzureMaintenance
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    setRBACPermissions: setRBACPermissions
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
  }
  dependsOn: [
    azureMaintenanceDataResources
  ]
}

// ----------------- SqlDataChecker
module sqlDataCheckerDataResources '../Service/GroupMembershipManagement/Hosts/SqlDataChecker/Infrastructure/data/template.bicep' = {
  name: 'sqlDataCheckerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module sqlDataCheckerComputeResources '../Service/GroupMembershipManagement/Hosts/SqlDataChecker/Infrastructure/compute/template.bicep' = {
  name: 'sqlDataCheckerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    maxInstanceCount: maxInstanceCounts.sqlDataChecker
    enableVnetIntegration: enableFunctionVnetIntegration
    virtualNetworkSubnetId: _vnetSubnetIdSqlDataChecker
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    authority: 'https://login.windows.net/${tenantId}'
    subscriptionId: subscription().subscriptionId
    pipeline: pipeline
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
  }
  dependsOn: [
    sqlDataCheckerDataResources
  ]
}

/// Functions Post Compute tasks
module azureUserReaderPostCompute '../Service/GroupMembershipManagement/Hosts/AzureUserReader/Infrastructure/compute/postCompute.bicep' = {
  name: 'azureUserReaderPostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    azureUserReaderComputeResources
    messageSplitterComputeResources
  ]
}

module graphUpdaterPostCompute '../Service/GroupMembershipManagement/Hosts/GraphUpdater/Infrastructure/compute/postCompute.bicep' = {
  name: 'graphUpdaterPostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    graphUpdaterComputeResources
    messageSplitterComputeResources
  ]
}

module jobSchedulerPostCompute '../Service/GroupMembershipManagement/Hosts/JobScheduler/Infrastructure/compute/postCompute.bicep' = {
  name: 'jobSchedulerPostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    jobSchedulerComputeResources
    messageSplitterComputeResources
  ]
}

module nonProdServicePostCompute '../Service/GroupMembershipManagement/Hosts/NonProdService/Infrastructure/compute/postCompute.bicep' = {
  name: 'nonProdServicePostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    nonProdServiceComputeResources
    messageSplitterComputeResources
  ]
}

module notifierPostCompute '../Service/GroupMembershipManagement/Hosts/Notifier/Infrastructure/compute/postCompute.bicep' = {
  name: 'notifierPostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    notifierComputeResources
    messageSplitterComputeResources
  ]
}

module sqlDataCheckerPostCompute '../Service/GroupMembershipManagement/Hosts/SqlDataChecker/Infrastructure/compute/postCompute.bicep' = {
  name: 'sqlDataCheckerPostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    sqlDataCheckerComputeResources
  ]
}

// web api
module webApiDataResources '../Service/GroupMembershipManagement/Hosts/WebApi/Infrastructure/data/template.bicep' = {
  name: 'webApiDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module webApiComputeResources '../Service/GroupMembershipManagement/Hosts/WebApi/Infrastructure/compute/template.bicep' = {
  name: 'webApiComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    location: location
    aiLocation: aiLocation
    prereqsResourceGroup: prereqsResourceGroupName
    dataResourceGroup: dataResourceGroupName
    adfPipeline: pipeline
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    apiHostname: resolvedApiHostname
    skipNetworkingDeployment: skipNetworkingDeployment
  }
  dependsOn: [
    sqlMembershipObtainerComputeResources
    webApiDataResources
    jobSchedulerPostCompute
  ]
}

module uiComputeResources '../Service/GroupMembershipManagement/Hosts/UI/Infrastructure/compute/template.bicep' = {
  name: 'uiComputeResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    location: uiLocation
    branch: branch
    repositoryUrl: repositoryUrl
    customDomainName: customDomainName
    apiServiceBaseUri: apiServiceBaseUri
    dataResourceGroupName: dataResourceGroupName
    computeResourceGroupName: computeResourceGroupName
    provider: 'Custom'
  }
  dependsOn: [
    webApiComputeResources
  ]
}
