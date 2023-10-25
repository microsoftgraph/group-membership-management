@description('Service plan name.')
@minLength(1)
param name string

@description('Service plan sku.')
param sku string

@description('Service plan location.')
param location string

@description('Maximum elastic worker count.')
param maximumElasticWorkerCount int

resource servicePlan 'Microsoft.Web/serverfarms@2018-02-01' = {
  name: name
  location: location
  sku: {
    name: sku
    tier: 'ElasticPremium'
  }
  kind: 'elastic'
  properties: {
    maximumElasticWorkerCount: maximumElasticWorkerCount
    targetWorkerCount: maximumElasticWorkerCount
  }
}
