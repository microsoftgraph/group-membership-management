param prereqsKeyVaultName string
param prereqsKeyVaultSkuName string
param prereqsKeyVaultSkuFamily string
param location string
param tenantId string
param keyVaultReaders array

//secrets
param graphAppCertificateName string
param graphAppClientId string
@secure()
param graphAppClientSecret string
param graphAppTenantId string
@secure()
param senderPassword string
param senderUsername string
param supportEmailAddresses string
param syncDisabledCCEmailAddresses string
param syncCompletedCCEmailAddresses string
param teamsChannelServiceAccountObjectId string
@secure()
param teamsChannelServiceAccountPassword string
param teamsChannelServiceAccountUsername string
param sqlMembershipAppId string
@secure()
param sqlMembershipAppPasswordCredentialValue string
param webapiClientId string
param webApiTenantId string

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

module prereqsKeyVaultPoliciesTemplate '../Infrastructure/data/keyVaultAccessPolicy.bicep' = {
  name: 'prereqsKeyVaultPoliciesTemplate'
  params: {
    name: prereqsKeyVaultName
    policies: keyVaultReaders
    tenantId: tenantId
  }
  dependsOn: [
    prereqsKeyVault
  ]
}

module prereqsScretsTemplate '../Infrastructure/data/keyVaultSecrets.bicep' = {
  name: 'prereqsScretsTemplate'
  params: {
    keyVaultName: prereqsKeyVaultName
    keyVaultParameters: [
      {
        name: 'graphAppCertificateName'
        value: graphAppCertificateName
      }
      {
        name: 'graphAppClientId'
        value: graphAppClientId
      }
      {
        name: 'graphAppClientSecret'
        value: graphAppClientSecret
      }
      {
        name: 'graphAppTenantId'
        value: graphAppTenantId
      }
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
        name: 'syncDisabledCCEmailAddresses'
        value: syncDisabledCCEmailAddresses
      }
      {
        name: 'syncCompletedCCEmailAddresses'
        value: syncCompletedCCEmailAddresses
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
      {
        name: 'sqlMembershipAppId'
        value: sqlMembershipAppId
      }
      {
        name: 'sqlMembershipAppPasswordCredentialValue'
        value: sqlMembershipAppPasswordCredentialValue
      }
      {
        name: 'webapiClientId'
        value: webapiClientId
      }
      {
        name: 'webApiTenantId'
        value: webApiTenantId
      }
    ]
  }
  dependsOn: [
    prereqsKeyVault
    prereqsKeyVaultPoliciesTemplate
  ]
}
