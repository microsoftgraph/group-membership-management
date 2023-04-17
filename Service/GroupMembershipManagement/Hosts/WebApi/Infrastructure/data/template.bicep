@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Enter app configuration name.')
@minLength(1)
@maxLength(24)
param appConfigurationName string = '${solutionAbbreviation}-appConfig-${environmentAbbreviation}'

param appConfigurationKeyData array = [
  {
    key: 'WebAPI:Settings:Sentinel$WebAPI'
    value: '1'
    contentType: 'string'
    tag: {
      tag1: 'WebApi'
    }
  }
]

module appConfigurationTemplate 'appConfigurationValues.bicep' = {
  name: 'appConfigurationTemplate'
  params: {
    configStoreName: appConfigurationName
    appConfigurationKeyData: appConfigurationKeyData
  }
}
