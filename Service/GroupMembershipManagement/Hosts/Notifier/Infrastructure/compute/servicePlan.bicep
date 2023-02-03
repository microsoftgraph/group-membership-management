@description('Service plan name.')
@minLength(1)
param name string

@description('Service plan sku.')
@allowed([
  'D1'
  'F1'
  'B1'
  'B2'
  'B3'
  'S1'
  'S2'
  'S3'
  'P1'
  'P2'
  'P3'
  'P1V2'
  'P2V2'
  'P3V2'
  'I1'
  'I2'
  'I3'
  'Y1'
])
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