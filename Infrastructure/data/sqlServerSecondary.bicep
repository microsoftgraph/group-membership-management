// Secondary region SQL logical servers: the failover group partner server and the secondary region
// read replica server. A module because it targets a different resource group, and only a module
// can change deployment scope.

@description('Name of the secondary SQL logical server that partners the primary in the failover group.')
param secondarySqlServerName string

@description('Name of the secondary region read replica server.')
param secondaryReplicaSqlServerName string

@description('Resource location for the secondary region.')
param secondaryLocation string

@description('Tenant Id.')
param tenantId string

@description('Administrators Azure AD Group Object Id')
param sqlAdministratorsGroupId string

@description('Resource id of the Log Analytics workspace that receives SQLSecurityAuditEvents.')
param logAnalyticsWorkspaceId string

resource secondarySqlServer 'Microsoft.Sql/servers@2021-11-01-preview' = {
  name: secondarySqlServerName
  location: secondaryLocation
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: 'GMM SQL admin group: ${sqlAdministratorsGroupId}'
      sid: sqlAdministratorsGroupId
      tenantId: tenantId
      azureADOnlyAuthentication: true
    }
  }

  resource aadAuthentication 'azureADOnlyAuthentications@2022-11-01-preview' = {
    name: 'Default'
    properties: {
      azureADOnlyAuthentication: true
    }
  }

  resource sqlServerFirewall 'firewallRules@2021-02-01-preview' = {
    name: 'AllowAllWindowsAzureIpsSecondary'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }

  resource masterDataBase 'databases@2021-02-01-preview' = {
    location: secondaryLocation
    name: 'master'
    properties: {}
  }

  // Auditing policies belong to each logical server and are not inherited through replication.
  // This server becomes the write server after a failover, so without its own policy the write
  // path would lose its audit route.
  resource auditingSettings 'auditingSettings@2017-03-01-preview' = {
    name: 'default'
    properties: {
      state: 'Enabled'
      isAzureMonitorTargetEnabled: true
    }
  }
}

// The secondary region counterpart of the primary region read replica server. It is provisioned up
// front so that a failover repoints the read endpoint rather than provisioning one.
resource secondaryReplicaSqlServer 'Microsoft.Sql/servers@2021-11-01-preview' = {
  name: secondaryReplicaSqlServerName
  location: secondaryLocation
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: 'GMM SQL admin group: ${sqlAdministratorsGroupId}'
      sid: sqlAdministratorsGroupId
      tenantId: tenantId
      azureADOnlyAuthentication: true
    }
  }

  resource aadAuthentication 'azureADOnlyAuthentications@2022-11-01-preview' = {
    name: 'Default'
    properties: {
      azureADOnlyAuthentication: true
    }
  }

  resource sqlServerFirewall 'firewallRules@2021-02-01-preview' = {
    name: 'AllowAllWindowsAzureIpsSecondaryReplica'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }

  resource masterDataBase 'databases@2021-02-01-preview' = {
    location: secondaryLocation
    name: 'master'
    properties: {}
  }

  resource auditingSettings 'auditingSettings@2017-03-01-preview' = {
    name: 'default'
    properties: {
      state: 'Enabled'
      isAzureMonitorTargetEnabled: true
    }
  }
}

resource secondaryDiagnosticSettings 'Microsoft.Insights/diagnosticSettings@2017-05-01-preview' = {
  scope: secondarySqlServer::masterDataBase
  name: 'diagnosticSettings'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'SQLSecurityAuditEvents'
        enabled: true
        retentionPolicy: {
          days: 0
          enabled: false
        }
      }
    ]
  }
}

resource secondaryReplicaDiagnosticSettings 'Microsoft.Insights/diagnosticSettings@2017-05-01-preview' = {
  scope: secondaryReplicaSqlServer::masterDataBase
  name: 'diagnosticSettings'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'SQLSecurityAuditEvents'
        enabled: true
        retentionPolicy: {
          days: 0
          enabled: false
        }
      }
    ]
  }
}

output serverId string = secondarySqlServer.id
output serverName string = secondarySqlServer.name
output replicaServerId string = secondaryReplicaSqlServer.id
output replicaServerName string = secondaryReplicaSqlServer.name
