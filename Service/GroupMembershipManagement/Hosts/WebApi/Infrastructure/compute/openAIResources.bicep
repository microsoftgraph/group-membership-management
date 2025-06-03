@description('Location for the OpenAI resource.')
param aiLocation string

@description('The name of the Azure OpenAI resource.')
param openAIResourceName string

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
  }
  tags: {
  }
}

resource openAIResourceName_Default 'Microsoft.CognitiveServices/accounts/defenderForAISettings@2025-04-01-preview' = {
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
}
