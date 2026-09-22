@description('Existing Front Door profile name.')
param profileName string

@description('Existing Front Door endpoint resource name.')
param endpointName string

@description('Actual App Service default hostname used for origin TLS and the Host header.')
param originHostname string

resource profile 'Microsoft.Cdn/profiles@2024-02-01' existing = {
  name: profileName
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' existing = {
  parent: profile
  name: endpointName
}

resource originGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'webapi'
  properties: {
    sessionAffinityState: 'Disabled'
    healthProbeSettings: {
      probePath: '/healthz'
      probeRequestType: 'HEAD'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 120
    }
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
  }
}

resource origin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: originGroup
  name: 'webapi'
  properties: {
    hostName: originHostname
    originHostHeader: originHostname
    httpsPort: 443
    enforceCertificateNameCheck: true
    enabledState: 'Enabled'
    priority: 1
    weight: 1000
  }
}

resource route 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'webapi'
  properties: {
    originGroup: {
      id: originGroup.id
    }
    supportedProtocols: [
      'Https'
    ]
    patternsToMatch: [
      '/*'
    ]
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
    enabledState: 'Enabled'
    // Omit cacheConfiguration to disable caching for API and SignalR traffic.
  }
  dependsOn: [
    origin
  ]
}
