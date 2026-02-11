@description('The name of the Azure OpenAI resource.')
param openAIResourceName string

@description('Location of the OpenAI resource.')
param aiLocation string

@description('Allowed IP addresses for the OpenAI resource (comma-separated).')
param allowedIpAddresses string

var ipAddressArray = empty(allowedIpAddresses) ? [] : split(allowedIpAddresses, ',')
var trimmedIpArray = [for ip in ipAddressArray: trim(ip)]
var uniqueIpArray = filter(trimmedIpArray, (ip, index) => indexOf(trimmedIpArray, ip) == index && !empty(ip))

var ipRules = [for ip in uniqueIpArray: {
  value: ip
}]

resource openAINetworkUpdate 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: openAIResourceName
  location: aiLocation
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
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
    publicNetworkAccess: empty(allowedIpAddresses) ? 'Disabled' : 'Enabled'
    disableLocalAuth: true
    restrictOutboundNetworkAccess: true
  }
}
