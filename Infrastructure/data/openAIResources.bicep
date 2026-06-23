@description('Location for the OpenAI resource.')
param aiLocation string

@description('The name of the Azure OpenAI resource.')
param openAIResourceName string

@description('Solution abbreviation.')
param solutionAbbreviation string = 'gmm'

@description('Environment abbreviation.')
param environmentAbbreviation string

@description('Allowed IP addresses for the OpenAI resource (comma-separated).')
param allowedIpAddresses string = ''

@description('Content filter policy name for the OpenAI deployment.')
param openAIContentFilterName string = 'DefaultV2'

@description('Base policy name for the OpenAI content filter configuration.')
param openAIContentFilterBasePolicyName string = 'Microsoft.DefaultV2'

var ipAddressArray = empty(allowedIpAddresses) ? [] : split(allowedIpAddresses, ',')
var trimmedIpArray = [for ip in ipAddressArray: trim(ip)]
var uniqueIpArray = filter(trimmedIpArray, (ip, index) => indexOf(trimmedIpArray, ip) == index && !empty(ip))

var ipRules = [for ip in uniqueIpArray: {
  value: ip
}]

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' existing = {
  name: '${solutionAbbreviation}-data-${environmentAbbreviation}'
  scope: resourceGroup('${solutionAbbreviation}-data-${environmentAbbreviation}')
}

resource openAI 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: openAIResourceName
  location: aiLocation
  kind: 'OpenAI'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
  properties: {
    apiProperties: {}
    customSubDomainName: toLower(openAIResourceName)
    networkAcls: {
      defaultAction: 'Deny'
      virtualNetworkRules: []
      ipRules: ipRules
    }
    allowProjectManagement: false
    publicNetworkAccess: empty(allowedIpAddresses) ? 'Disabled' : 'Enabled'  // Disabled when no IPs, Enabled with restrictions when IPs provided
    disableLocalAuth: true
    restrictOutboundNetworkAccess: true
  }
  tags: {
    // Bypass buggy CloudGov RAI deny policies (CloudGov_Input_CF_Hate / _Out_CF_Hate).
    // Per Cory Delamarter (CloudGov) 2026-06-12 — proper fix tracked in WI 16265440.
    'skip-cloudgov-AIFoundry_InputContentFilter_Hate': 'WI 16265440 - pending CloudGov fix'
    'skip-cloudgov-AIFoundry_OutputContentFilter_Hate': 'WI 16265440 - pending CloudGov fix'
  }
}

resource defenderForAISettings 'Microsoft.CognitiveServices/accounts/defenderForAISettings@2025-04-01-preview' = {
  parent: openAI
  name: 'Default'
  properties: {
    state: 'Disabled'
  }
}

resource openAIContentFilterPolicy 'Microsoft.CognitiveServices/accounts/raiPolicies@2025-06-01' = {
  parent: openAI
  name: openAIContentFilterName
  dependsOn: [
    defenderForAISettings
  ]
  properties: {
    basePolicyName: openAIContentFilterBasePolicyName
    mode: 'Blocking'
    contentFilters: [
      {
        name: 'hate'
        blocking: true
        enabled: true
        severityThreshold: 'Low'
        source: 'Prompt'
      }
      {
        name: 'hate'
        blocking: true
        enabled: true
        severityThreshold: 'Low'
        source: 'Completion'
      }
      {
        name: 'violence'
        blocking: true
        enabled: true
        severityThreshold: 'Low'
        source: 'Prompt'
      }
      {
        name: 'violence'
        blocking: true
        enabled: true
        severityThreshold: 'Low'
        source: 'Completion'
      }
      {
        name: 'sexual'
        blocking: true
        enabled: true
        severityThreshold: 'Low'
        source: 'Prompt'
      }
      {
        name: 'sexual'
        blocking: true
        enabled: true
        severityThreshold: 'Low'
        source: 'Completion'
      }
      {
        name: 'selfharm'
        blocking: true
        enabled: true
        severityThreshold: 'Low'
        source: 'Prompt'
      }
      {
        name: 'selfharm'
        blocking: true
        enabled: true
        severityThreshold: 'Low'
        source: 'Completion'
      }
      {
        name: 'profanity'
        blocking: true
        enabled: true
        source: 'Prompt'
      }
      {
        name: 'profanity'
        blocking: true
        enabled: true
        source: 'Completion'
      }
      {
        name: 'jailbreak'
        blocking: true
        enabled: true
        source: 'Prompt'
      }
      {
        name: 'indirect_attack'
        blocking: true
        enabled: true
        source: 'Prompt'
      }
      {
        name: 'protected_material_text'
        blocking: true
        enabled: true
        source: 'Completion'
      }
      {
        name: 'protected_material_code'
        blocking: true
        enabled: true
        source: 'Completion'
      }
    ]
  }
}

resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = {
  parent: openAI
  name: 'gpt-4o'
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20'
    }
    raiPolicyName: openAIContentFilterPolicy.name
    versionUpgradeOption: 'NoAutoUpgrade'
  }
  sku: {
    name: 'standard'
    capacity: 1
  }
  dependsOn: [
    openAIContentFilterPolicy
  ]
}

resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'openAI-diagnostics'
  scope: openAI
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
        retentionPolicy: {
          days: 0
          enabled: false
        }
      }
    ]
    metrics: [
      {
        category: 'allMetrics'
        enabled: true
        retentionPolicy: {
          days: 0
          enabled: false
        }
      }
    ]
  }
  dependsOn: [
    gpt4oDeployment
  ]
}

output openAIEndpoint string = openAI.properties.endpoint

