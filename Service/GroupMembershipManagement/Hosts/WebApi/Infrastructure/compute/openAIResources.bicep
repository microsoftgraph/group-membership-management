@description('Location for the OpenAI resource.')
param aiLocation string

@description('The name of the Azure OpenAI resource.')
param openAIResourceName string

@description('Solution abbreviation.')
param solutionAbbreviation string = 'gmm'

@description('Environment abbreviation.')
param environmentAbbreviation string

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' existing = {
  name: '${solutionAbbreviation}-data-${environmentAbbreviation}'
  scope: resourceGroup('${solutionAbbreviation}-data-${environmentAbbreviation}')
}

resource openAI 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: openAIResourceName
  location: aiLocation
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    apiProperties: {}
    customSubDomainName: toLower(openAIResourceName)
    networkAcls: {
      defaultAction: 'Allow'
      virtualNetworkRules: []
      ipRules: []
    }
    allowProjectManagement: false
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
    restrictOutboundNetworkAccess: true
  }
  tags: {
  }
}

resource defenderForAISettings 'Microsoft.CognitiveServices/accounts/defenderForAISettings@2025-04-01-preview' = {
  parent: openAI
  name: 'Default'
  properties: {
    state: 'Disabled'
  }
}

resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = {
  parent: openAI
  name: 'gpt-4o'
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-05-13'
    }
  }
  sku: {
    name: 'standard'
    capacity: 1
  }
  dependsOn: [
    defenderForAISettings
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
