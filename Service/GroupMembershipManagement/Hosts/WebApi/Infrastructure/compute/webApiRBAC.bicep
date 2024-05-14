@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultResourceGroup string

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool

@description('The principalId of the webapi.')
param webApiPrincipalId string

module webapiPrereqsRBAC 'keyvaultRBAC.bicep' = if (setRBACPermissions) {
  name: 'prereqsKV-rbac-webapi'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    keyVaultName: prereqsKeyVaultName
    principalId: webApiPrincipalId
    roleName: 'Key Vault Secrets User'
  }
}

module webapiDataRBAC 'keyvaultRBAC.bicep' = if (setRBACPermissions) {
  name: 'dataKV-rbac-webapi'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    principalId: webApiPrincipalId
    roleName: 'Key Vault Secrets User'
  }
}
