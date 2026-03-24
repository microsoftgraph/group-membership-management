param prereqsKeyVaultName string
param prereqsKeyVaultSkuName string = 'standard'
param prereqsKeyVaultSkuFamily string = 'A'
param location string
param tenantId string

//secrets
@secure()
param senderPassword string
param senderUsername string
param supportEmailAddresses string
param teamsChannelServiceAccountObjectId string
@secure()
param teamsChannelServiceAccountPassword string
param teamsChannelServiceAccountUsername string
param functionAuthAppClientId string = ''

param isInitialDeployment bool

// prereqs resources
module prereqsKeyVault '../Infrastructure/data/keyVault.bicep' = {
  name: 'prereqsKeyVaultTemplate'
  params: {
    name: prereqsKeyVaultName
    skuName: prereqsKeyVaultSkuName
    skuFamily: prereqsKeyVaultSkuFamily
    location: location
    tenantId: tenantId
  }
}

var secretsToUpdate = union(
  isInitialDeployment || !empty(senderPassword) ? [{ name: 'senderPassword', value: senderPassword }] : [],
  isInitialDeployment || !empty(senderUsername) ? [{ name: 'senderUsername', value: senderUsername }] : [],
  isInitialDeployment || !empty(supportEmailAddresses) ? [{ name: 'supportEmailAddresses', value: supportEmailAddresses }] : [],
  isInitialDeployment || !empty(teamsChannelServiceAccountObjectId) ? [{ name: 'teamsChannelServiceAccountObjectId', value: teamsChannelServiceAccountObjectId }] : [],
  isInitialDeployment || !empty(teamsChannelServiceAccountPassword) ? [{ name: 'teamsChannelServiceAccountPassword', value: teamsChannelServiceAccountPassword }] : [],
  isInitialDeployment || !empty(teamsChannelServiceAccountUsername) ? [{ name: 'teamsChannelServiceAccountUsername', value: teamsChannelServiceAccountUsername }] : [],
  isInitialDeployment || !empty(functionAuthAppClientId) ? [{ name: 'functionAuthAppClientId', value: functionAuthAppClientId }] : []
)

module prereqsScretsTemplate '../Infrastructure/data/keyVaultSecretsSecure.bicep' = if (!empty(secretsToUpdate)) {
  name: 'prereqsScretsTemplate'
  params: {
    keyVaultName: prereqsKeyVaultName
    keyVaultSecrets: {
      secrets: secretsToUpdate
    }
  }
  dependsOn: [
    prereqsKeyVault
  ]
}
