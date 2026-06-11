@description('Service plan name.')
@minLength(1)
param name string

@description('Service plan sku.')
@allowed([
  'FC1'
])
param sku string = 'FC1'

@description('Service plan location.')
param location string

resource servicePlan 'Microsoft.Web/serverfarms@2018-02-01' = {
  name: name
  location: location
  kind: 'functionapp'
  properties: {
    reserved: true
  }
  sku: {
    name: sku
    tier: 'FlexConsumption'
  }
}
