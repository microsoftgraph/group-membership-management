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

module prereqsScretsTemplate '../Infrastructure/data/keyVaultSecretsSecure.bicep' = {
  name: 'prereqsScretsTemplate'
  params: {
    keyVaultName: prereqsKeyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'senderPassword'
          value: senderPassword
        }
        {
          name: 'senderUsername'
          value: senderUsername
        }
        {
          name: 'supportEmailAddresses'
          value: supportEmailAddresses
        }
        {
          name: 'teamsChannelServiceAccountObjectId'
          value: teamsChannelServiceAccountObjectId
        }
        {
          name: 'teamsChannelServiceAccountPassword'
          value: teamsChannelServiceAccountPassword
        }
        {
          name: 'teamsChannelServiceAccountUsername'
          value: teamsChannelServiceAccountUsername
        }
      ]
    }
  }
  dependsOn: [
    prereqsKeyVault
  ]
}
