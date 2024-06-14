@description('Name of Azure Data Factory')
param factoryName string

@description('Enter an abbreviation for the environment')
param environmentAbbreviation string

@description('Location for Azure Data Factory account')
param location string

@description('Name of SQL Server')
param sqlServerName string

@description('Name of SQL Server')
param sqlDataBaseName string

@description('AzureUserReader function url.')
@secure()
param azureUserReaderUrl string

@description('AzureUserReader function key.')
@secure()
param azureUserReaderFunctionKey string

@description('Connection string of adf storage account')
@secure()
param storageAccountConnectionString string

var sqlServerUrl = '${sqlServerName}${environment().suffixes.sqlServerHostname}'


resource dataFactory 'Microsoft.DataFactory/factories@2018-06-01' = {
  name: factoryName
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    globalParameters: {
      environment: {
        type: 'String'
        value: environmentAbbreviation
      }
    }
  }
  location: location
}

resource linkedService_AzureBlobStorage 'Microsoft.DataFactory/factories/linkedServices@2018-06-01' = {
  name: '${factoryName}/AzureBlobStorage'
  properties: {
    annotations: []
    type: 'AzureBlobStorage'
    typeProperties: {
      connectionString: storageAccountConnectionString
    }
  }
  dependsOn: [
    dataFactory
  ]
}

resource linkedService_DestinationDatabase 'Microsoft.DataFactory/factories/linkedservices@2018-06-01' = {
  name: '${factoryName}/DestinationDatabase'
  properties: {
    annotations: []
    type: 'AzureSqlDatabase'
    typeProperties: {
      connectionString: 'Integrated Security=False;Encrypt=True;Connection Timeout=30;Data Source=${sqlServerUrl};Initial Catalog=${sqlDataBaseName}'
    }
  }
  dependsOn: [
    dataFactory
  ]
}

resource linkedService_AzureUserReader 'Microsoft.DataFactory/factories/linkedServices@2018-06-01' = {
  name: '${factoryName}/AzureUserReader'
  properties: {
    annotations: []
    type: 'AzureFunction'
    typeProperties: {
      functionAppUrl: azureUserReaderUrl
      functionKey: {
        type: 'SecureString'
        value: azureUserReaderFunctionKey
      }
      authentication: 'Anonymous'
    }
  }
  dependsOn: [
    dataFactory
  ]
}

resource Pipeline_PopulateDestinationPipeline 'Microsoft.DataFactory/factories/pipelines@2018-06-01' = {
  name: '${factoryName}/PopulateDestinationPipeline'
  properties: {
    activities: [
      {
        name: 'AzureUserReader'
        type: 'AzureFunctionActivity'
        dependsOn: []
        policy: {
          timeout: '0.12:00:00'
          retry: 0
          retryIntervalInSeconds: 30
          secureOutput: false
          secureInput: false
        }
        userProperties: []
        typeProperties: {
          functionName: {
            value: 'StarterFunction'
            type: 'Expression'
          }
          method: 'POST'
          headers: {}
          body: {
            value: '{"ContainerName":"csvcontainer","BlobPath":"memberids.csv"}'
            type: 'Expression'
          }
        }
        linkedServiceName: {
          referenceName: 'AzureUserReader'
          type: 'LinkedServiceReference'
        }
      }
      {
        name: 'PopulateDestinationDataFlow'
        type: 'ExecuteDataFlow'
        dependsOn: [
          {
            activity: 'AzureUserReader'
            dependencyConditions: [
              'Succeeded'
            ]
          }
        ]
        policy: {
          timeout: '0.12:00:00'
          retry: 0
          retryIntervalInSeconds: 30
          secureOutput: false
          secureInput: false
        }
        userProperties: []
        typeProperties: {
          dataFlow: {
            referenceName: 'PopulateDestinationDataFlow'
            type: 'DataFlowReference'
            parameters: {}
            datasetParameters: {
              memberids: {}
              memberHRData: {}
              sink: {
                TableName: {
                  value: 'tbl@{replace(pipeline().RunId,\'-\',\'\')}'
                  type: 'Expression'
                }
              }
            }
          }
          staging: {}
          compute: {
            coreCount: 8
            computeType: 'General'
          }
          traceLevel: 'Fine'
        }
      }
    ]
    policy: {
      elapsedTimeMetric: {}
    }
    annotations: []
  }
  dependsOn: [
    dataFactory
    dataFlow_PopulateDestinationDataFlow
    linkedService_AzureUserReader
  ]
}

resource dataSet_DestinationTable 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  name: '${factoryName}/DestinationTable'
  properties: {
    linkedServiceName: {
      referenceName: 'DestinationDatabase'
      type: 'LinkedServiceReference'
    }
    annotations: []
    type: 'AzureSqlTable'
    schema: []
    typeProperties: {
      table: {
        value: '@replace(pipeline().RunId,\'-\',\'\')'
        type: 'Expression'
      }
    }
  }
  dependsOn: [
    linkedService_DestinationDatabase
  ]
}

resource dataSet_DelimitedText 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  name: '${factoryName}/DelimitedText'
  properties: {
    linkedServiceName: {
      referenceName: 'AzureBlobStorage'
      type: 'LinkedServiceReference'
    }
    annotations: []
    type: 'DelimitedText'
    typeProperties: {
      location: {
        type: 'AzureBlobStorageLocation'
        container: 'csvcontainer'
      }
      columnDelimiter: ','
      escapeChar: '\\'
      firstRowAsHeader: true
      quoteChar: '"'
    }
    schema: []
  }
  dependsOn: [
    linkedService_AzureBlobStorage
  ]
}

resource dataFlow_PopulateDestinationDataFlow 'Microsoft.DataFactory/factories/dataflows@2018-06-01' = {
  name: '${factoryName}/PopulateDestinationDataFlow'
  properties: {
    type: 'MappingDataFlow'
    typeProperties: {
      sources: [
        {
          dataset: {
            referenceName: 'DelimitedText'
            type: 'DatasetReference'
          }
          name: 'memberids'
        }
        {
          dataset: {
            referenceName: 'DelimitedText'
            type: 'DatasetReference'
          }
          name: 'memberHRData'
        }
      ]
      sinks: [
        {
          dataset: {
            referenceName: 'DestinationTable'
            type: 'DatasetReference'
          }
          name: 'sink'
        }
      ]
      transformations: [
        {
          name: 'join'
        }
      ]
      scriptLines: [
        'source(output('
        '          PersonnelNumber as integer,'
        '          AzureObjectId as string,'
        '          UserPrincipalName as string'
        '     ),'
        '     allowSchemaDrift: true,'
        '     validateSchema: false,'
        '     ignoreNoFilesFound: false,'
        '     wildcardPaths:[\'memberids.csv\']) ~> memberids'
        'source(output('
        '          EmployeeIdentificationNumber as integer,'
        '          ManagerIdentificationNumber as integer,'
        '          Position as string,'
        '          Level as integer,'
        '          Country as string'
        '     ),'
        '     allowSchemaDrift: true,'
        '     validateSchema: false,'
        '     ignoreNoFilesFound: false,'
        '     wildcardPaths:[\'memberHRData.csv\']) ~> memberHRData'
        'memberids, memberHRData join(PersonnelNumber == EmployeeIdentificationNumber,'
        '     joinType:\'inner\','
        '     matchType:\'exact\','
        '     ignoreSpaces: false,'
        '     broadcast: \'auto\')~> join'
        'join sink(allowSchemaDrift: true,'
        '     validateSchema: false,'
        '     deletable:false,'
        '     insertable:true,'
        '     updateable:false,'
        '     upsertable:false,'
        '     format: \'table\','
        '     mapColumn('
        '          AzureObjectId,'
        '          Email = UserPrincipalName,'
        '          EmployeeId = EmployeeIdentificationNumber,'
        '          ManagerId = ManagerIdentificationNumber,'
        '          Position,'
        '          Level,'
        '          Country'
        '     )) ~> sink'
      ]
    }
  }
  dependsOn: [
    dataSet_DelimitedText
    dataSet_DestinationTable
  ]
}

output systemAssignedIdentityId string = dataFactory.identity.principalId
