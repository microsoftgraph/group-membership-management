@description('Service plan name.')
@minLength(1)
param name string

@description('Service plan sku.')
param sku string = 'Y1'

@description('Service plan location.')
param location string

@description('Maximum elastic worker count.')
param maximumElasticWorkerCount int = 1

resource servicePlan 'Microsoft.Web/serverfarms@2018-02-01' = {
  name: name
  location: location
  properties: {
    maximumElasticWorkerCount: maximumElasticWorkerCount
    targetWorkerCount: maximumElasticWorkerCount
  }
  sku: {
    name: sku
    tier: 'Dynamic'
  }
}
