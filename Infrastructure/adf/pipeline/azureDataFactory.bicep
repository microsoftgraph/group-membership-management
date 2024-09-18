@description('Name of Azure Data Factory')
param factoryName string

@description('Resource name suffix')
param resourceSuffix string

@description('Enter an abbreviation for the environment')
param environmentAbbreviation string

@description('Location for Azure Data Factory account')
param location string

@description('Name of SQL Server')
param sqlServerName string

@description('Name of SQL Database')
param sqlDatabaseName string

@description('AzureUserReader function url.')
@secure()
param azureUserReaderUrl string

@description('AzureUserReader function key.')
@secure()
param azureUserReaderFunctionKey string

@description('Connection string of adf storage account')
@secure()
param storageAccountConnectionString string

var dataFactoryName = factoryName
var azureBlobStorageLinkedService = 'AzureBlobStorage_${resourceSuffix}'
var destinationDatabaseLinkedService = 'DestinationDatabase_${resourceSuffix}'
var azureUserReaderLinkedService = 'AzureUserReader_${resourceSuffix}'
var populateDestinationPipelineName = 'PopulateDestinationPipeline_${resourceSuffix}'
var destinationTableDataSet = 'DestinationTable_${resourceSuffix}'
var mappingsTableDataSet = 'MappingsTable_${resourceSuffix}'
var jobLevelMapDataSet = 'JobLevelMap_${resourceSuffix}'
var jobTitleMapDataSet = 'JobTitleMap_${resourceSuffix}'
var countryMapDataSet = 'CountryMap_${resourceSuffix}'
var memberIdsDataSet = 'MemberIds_${resourceSuffix}'
var memberHRDataDataSet = 'MemberHRData_${resourceSuffix}'
var populateMappingsTableDataFlow = 'PopulateMappingsTableDataFlow_${resourceSuffix}'
var populateDestinationDataFlow = 'PopulateDestinationDataFlow_${resourceSuffix}'

resource dataFactory 'Microsoft.DataFactory/factories@2018-06-01' = {
  name: dataFactoryName
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
  parent: dataFactory
  name: azureBlobStorageLinkedService
  properties: {
    annotations: []
    type: 'AzureBlobStorage'
    typeProperties: {
      connectionString: storageAccountConnectionString
    }
  }
}

resource linkedService_DestinationDatabase 'Microsoft.DataFactory/factories/linkedServices@2018-06-01' = {
  parent: dataFactory
  name: destinationDatabaseLinkedService
  properties: {
    annotations: []
    type: 'AzureSqlDatabase'
    typeProperties: {
      connectionString: 'Integrated Security=False;Encrypt=True;Connection Timeout=90;Data Source=${sqlServerName}.database.windows.net;Initial Catalog=${sqlDatabaseName}'
    }
  }
}

resource linkedService_AzureUserReader 'Microsoft.DataFactory/factories/linkedServices@2018-06-01' = {
  parent: dataFactory
  name: azureUserReaderLinkedService
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
  dependsOn: []
}

resource Pipeline_PopulateDestinationPipeline 'Microsoft.DataFactory/factories/pipelines@2018-06-01' = {
  parent: dataFactory
  name: populateDestinationPipelineName
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
          body: {
            value: '{"ContainerName":"csvcontainer","BlobPath":"memberids.csv"}'
            type: 'Expression'
          }
          headers: {}
          method: 'POST'
        }
        linkedServiceName: {
          referenceName: azureUserReaderLinkedService
          type: 'LinkedServiceReference'
        }
      }
      {
        name: 'PopulateDestinationDataFlow'
        type: 'ExecuteDataFlow'
        dependsOn: [
          {
            activity: 'CreateSchemas'
            dependencyConditions: [
              'Succeeded'
            ]
          }
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
            referenceName: populateDestinationDataFlow
            type: 'DataFlowReference'
            parameters: {}
            datasetParameters: {
              memberids: {}
              memberHRData: {}
              sink: {
                TableName: {
                  value: '@{replace(pipeline().RunId,\'-\',\'\')}'
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
      {
        name: 'Add Height Column'
        type: 'Script'
        dependsOn: [
          {
            activity: 'PopulateDestinationDataFlow'
            dependencyConditions: [
              'Succeeded'
            ]
          }
        ]
        policy: {
          timeout: '7.00:00:00'
          retry: 0
          retryIntervalInSeconds: 30
          secureOutput: false
          secureInput: false
        }
        userProperties: []
        linkedServiceName: {
          referenceName: destinationDatabaseLinkedService
          type: 'LinkedServiceReference'
        }
        typeProperties: {
          scripts: [
            {
              type: 'Query'
              text: {
                value: '@concat(\'ALTER TABLE users.[\',replace(pipeline().RunId,\'-\',\'\'),\'] ADD Height int\')'
                type: 'Expression'
              }
            }
          ]
          scriptBlockExecutionTimeout: '02:00:00'
        }
      }
      {
        name: 'Calculate Height'
        type: 'Script'
        dependsOn: [
          {
            activity: 'Add Height Column'
            dependencyConditions: [
              'Succeeded'
            ]
          }
        ]
        policy: {
          timeout: '7.00:00:00'
          retry: 0
          retryIntervalInSeconds: 30
          secureOutput: false
          secureInput: false
        }
        userProperties: []
        linkedServiceName: {
          referenceName: destinationDatabaseLinkedService
          type: 'LinkedServiceReference'
        }
        typeProperties: {
          scripts: [
            {
              type: 'Query'
              text: {
                value: '@concat(\'\n\nWITH DirectReports(Email, RootId, ManagerId, EmployeeId, RelativeEmployeeLevel) AS\n(\n\tSELECT Email, EmployeeId RootId, ManagerId, EmployeeId, 0 AS RelativeEmployeeLevel\n\tFROM users.[\',replace(pipeline().RunId,\'-\',\'\'),\']\t\n\tUNION ALL\n\tSELECT d.Email, d.RootId, e.ManagerId, e.EmployeeId, RelativeEmployeeLevel + 1\n\tFROM users.[\',replace(pipeline().RunId,\'-\',\'\'),\'] AS e\n\t\tINNER JOIN DirectReports AS d\n\t\tON e.ManagerId = d.EmployeeId\t \n), q2 as\n(\n    SELECT Email,\n           RootId,\n           ManagerId,\n           EmployeeId,\n           RelativeEmployeeLevel,\n           max(RelativeEmployeeLevel) over (partition by RootId) - RelativeEmployeeLevel LevelsBelow\n    FROM DirectReports\n), ForUpd as\n(SELECT rootId, EmployeeId, LevelsBelow FROM q2 where rootid = EmployeeId)\n\nUPDATE users.[\',replace(pipeline().RunId,\'-\',\'\'),\'] SET Height = B.LevelsBelow\nFROM users.[\',replace(pipeline().RunId,\'-\',\'\'),\'] A\nJOIN ForUpd B ON A.EmployeeId = B.EmployeeId\n\n\')'
                type: 'Expression'
              }
            }
          ]
          scriptBlockExecutionTimeout: '02:00:00'
        }
      }
      {
        name: 'CreateSchemas'
        type: 'Script'
        dependsOn: []
        policy: {
          timeout: '0.12:00:00'
          retry: 0
          retryIntervalInSeconds: 30
          secureOutput: false
          secureInput: false
        }
        userProperties: []
        linkedServiceName: {
          referenceName: destinationDatabaseLinkedService
          type: 'LinkedServiceReference'
        }
        typeProperties: {
          scripts: [
            {
              type: 'NonQuery'
              text: 'IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = \'users\')\nBEGIN\n   EXEC(\'CREATE SCHEMA users\')\nEND'
            }
            {
              type: 'NonQuery'
              text: 'IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = \'mappings\')\nBEGIN\n   EXEC(\'CREATE SCHEMA mappings\')\nEND'
            }
          ]
          scriptBlockExecutionTimeout: '02:00:00'
        }
      }
      {
        name: 'PopulateMappingsTableDataFlow'
        type: 'ExecuteDataFlow'
        dependsOn: [
          {
            activity: 'CreateSchemas'
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
            referenceName: populateMappingsTableDataFlow
            type: 'DataFlowReference'
            parameters: {}
            datasetParameters: {
              CountryMap: {}
              JobLevelMap: {}
              JobTitleMap: {}
              sink1: {
                TableName: {
                  value: '@{replace(pipeline().RunId,\'-\',\'\')}'
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
    dataFlow_PopulateDestinationDataFlow
  ]
}

resource dataSet_DestinationTable 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: dataFactory
  name: destinationTableDataSet
  properties: {
    linkedServiceName: {
      referenceName: destinationDatabaseLinkedService
      type: 'LinkedServiceReference'
    }
    parameters: {
      TableName: {
        type: 'String'
      }
    }
    annotations: []
    type: 'SqlServerTable'
    schema: []
    typeProperties: {
      schema: 'users'
      table: {
        value: '@dataset().TableName'
        type: 'Expression'
      }
    }
  }
  dependsOn: [
    linkedService_DestinationDatabase
  ]
}

resource dataSet_MappingsTable 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: dataFactory
  name: mappingsTableDataSet
  properties: {
    linkedServiceName: {
      referenceName: destinationDatabaseLinkedService
      type: 'LinkedServiceReference'
    }
    parameters: {
      TableName: {
        type: 'string'
      }
    }
    annotations: []
    type: 'AzureSqlTable'
    schema: []
    typeProperties: {
      schema: 'mappings'
      table: {
        value: '@dataset().TableName'
        type: 'Expression'
      }
    }
  }
  dependsOn: [
    linkedService_DestinationDatabase
  ]
}

resource dataSet_JobLevelMap 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: dataFactory
  name: jobLevelMapDataSet
  properties: {
    linkedServiceName: {
      referenceName: azureBlobStorageLinkedService
      type: 'LinkedServiceReference'
    }
    annotations: []
    type: 'DelimitedText'
    typeProperties: {
      location: {
        type: 'AzureBlobStorageLocation'
        fileName: 'jobLevelMap.csv'
        container: 'csvcontainer'
      }
      columnDelimiter: ','
      escapeChar: '\\'
      firstRowAsHeader: true
      quoteChar: '"'
    }
    schema: [
      {
        name: 'JobLevelCode'
        type: 'String'
      }
      {
        name: 'JobLevelDesc'
        type: 'String'
      }
    ]
  }
  dependsOn: [
    linkedService_AzureBlobStorage
  ]
}

resource dataSet_JobTitleMap 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: dataFactory
  name: jobTitleMapDataSet
  properties: {
    linkedServiceName: {
      referenceName: azureBlobStorageLinkedService
      type: 'LinkedServiceReference'
    }
    annotations: []
    type: 'DelimitedText'
    typeProperties: {
      location: {
        type: 'AzureBlobStorageLocation'
        fileName: 'jobTitleMap.csv'
        container: 'csvcontainer'
      }
      columnDelimiter: ','
      escapeChar: '\\'
      firstRowAsHeader: true
      quoteChar: '"'
    }
    schema: [
      {
        name: 'JobTitleCode'
        type: 'String'
      }
      {
        name: 'JobTitleDesc'
        type: 'String'
      }
    ]
  }
  dependsOn: [
    linkedService_AzureBlobStorage
  ]
}

resource dataSet_CountryMap 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: dataFactory
  name: countryMapDataSet
  properties: {
    linkedServiceName: {
      referenceName: azureBlobStorageLinkedService
      type: 'LinkedServiceReference'
    }
    annotations: []
    type: 'DelimitedText'
    typeProperties: {
      location: {
        type: 'AzureBlobStorageLocation'
        fileName: 'countryMap.csv'
        container: 'csvcontainer'
      }
      columnDelimiter: ','
      escapeChar: '\\'
      firstRowAsHeader: true
      quoteChar: '"'
    }
    schema: [
      {
        name: 'CountryCode'
        type: 'String'
      }
      {
        name: 'CountryDesc'
        type: 'String'
      }
    ]
  }
  dependsOn: [
    linkedService_AzureBlobStorage
  ]
}

resource dataSet_MemberIds 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: dataFactory
  name: memberIdsDataSet
  properties: {
    linkedServiceName: {
      referenceName: azureBlobStorageLinkedService
      type: 'LinkedServiceReference'
    }
    annotations: []
    type: 'DelimitedText'
    typeProperties: {
      location: {
        type: 'AzureBlobStorageLocation'
        fileName: 'memberids.csv'
        container: 'csvcontainer'
      }
      columnDelimiter: ','
      escapeChar: '\\'
      firstRowAsHeader: true
      quoteChar: '"'
    }
    schema: [
      {
        name: 'PersonnelNumber'
        type: 'String'
      }
      {
        name: 'AzureObjectId'
        type: 'String'
      }
      {
        name: 'UserPrincipalName'
        type: 'String'
      }
    ]
  }
  dependsOn: [
    linkedService_AzureBlobStorage
  ]
}

resource dataSet_MemberHRData 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: dataFactory
  name: memberHRDataDataSet
  properties: {
    linkedServiceName: {
      referenceName: azureBlobStorageLinkedService
      type: 'LinkedServiceReference'
    }
    annotations: []
    type: 'DelimitedText'
    typeProperties: {
      location: {
        type: 'AzureBlobStorageLocation'
        fileName: 'memberHRData.csv'
        container: 'csvcontainer'
      }
      columnDelimiter: ','
      escapeChar: '\\'
      firstRowAsHeader: true
      quoteChar: '"'
    }
    schema: [
      {
        name: 'EmployeeIdentificationNumber'
        type: 'String'
      }
      {
        name: 'ManagerIdentificationNumber'
        type: 'String'
      }
      {
        name: 'Department'
        type: 'String'
      }
      {
        name: 'JobTitleCode'
        type: 'String'
      }
      {
        name: 'JobLevelCode'
        type: 'String'
      }
      {
        name: 'CountryCode'
        type: 'String'
      }
    ]
  }
  dependsOn: [
    linkedService_AzureBlobStorage
  ]
}

resource dataFlow_PopulateMappingsTableDataFlow 'Microsoft.DataFactory/factories/dataflows@2018-06-01' = {
  parent: dataFactory
  name: populateMappingsTableDataFlow
  properties: {
    type: 'MappingDataFlow'
    typeProperties: {
      sources: [
        {
          dataset: {
            referenceName: countryMapDataSet
            type: 'DatasetReference'
          }
          name: 'CountryMap'
          description: 'Import data from countryMap.csv'
        }
        {
          dataset: {
            referenceName: jobLevelMapDataSet
            type: 'DatasetReference'
          }
          name: 'JobLevelMap'
          description: 'Import data from jobLevelMap.csv'
        }
        {
          dataset: {
            referenceName: jobTitleMapDataSet
            type: 'DatasetReference'
          }
          name: 'JobTitleMap'
          description: 'Import data from jobTitleMap.csv'
        }
      ]
      sinks: [
        {
          dataset: {
            referenceName: mappingsTableDataSet
            type: 'DatasetReference'
          }
          name: 'sink1'
        }
      ]
      transformations: [
        {
          name: 'CreateNewColumns1'
        }
        {
          name: 'CreateNewColumns2'
        }
        {
          name: 'CreateNewColumns3'
        }
        {
          name: 'CountryMapSelect'
        }
        {
          name: 'JobLevelSelect'
        }
        {
          name: 'JobTitleMapSelect'
        }
        {
          name: 'union1'
        }
      ]
      scriptLines: [
        'source(output('
        '          CountryCode as string,'
        '          CountryDesc as string'
        '     ),'
        '     allowSchemaDrift: true,'
        '     validateSchema: false,'
        '     ignoreNoFilesFound: false,'
        '     wildcardPaths:[\'countryMap.csv\']) ~> CountryMap'
        'source(output('
        '          JobLevelCode as string,'
        '          JobLevelDesc as string'
        '     ),'
        '     allowSchemaDrift: true,'
        '     validateSchema: false,'
        '     ignoreNoFilesFound: false) ~> JobLevelMap'
        'source(output('
        '          JobTitleCode as string,'
        '          JobTitleDesc as string'
        '     ),'
        '     allowSchemaDrift: true,'
        '     validateSchema: false,'
        '     ignoreNoFilesFound: false) ~> JobTitleMap'
        'CountryMap derive(ColumnName = \'Country\','
        '          Code = CountryCode,'
        '          Description = CountryDesc) ~> CreateNewColumns1'
        'JobLevelMap derive(ColumnName = \'JobLevel\','
        '          Code = JobLevelCode,'
        '          Description = JobLevelDesc) ~> CreateNewColumns2'
        'JobTitleMap derive(ColumnName = \'JobTitleMap\','
        '          Code = JobTitleCode,'
        '          Description = JobTitleDesc) ~> CreateNewColumns3'
        'CreateNewColumns1 select(mapColumn('
        '          ColumnName,'
        '          Code,'
        '          Description'
        '     ),'
        '     skipDuplicateMapInputs: true,'
        '     skipDuplicateMapOutputs: true) ~> CountryMapSelect'
        'CreateNewColumns2 select(mapColumn('
        '          ColumnName,'
        '          Code,'
        '          Description'
        '     ),'
        '     skipDuplicateMapInputs: true,'
        '     skipDuplicateMapOutputs: true) ~> JobLevelSelect'
        'CreateNewColumns3 select(mapColumn('
        '          ColumnName,'
        '          Code,'
        '          Description'
        '     ),'
        '     skipDuplicateMapInputs: true,'
        '     skipDuplicateMapOutputs: true) ~> JobTitleMapSelect'
        'CountryMapSelect, JobLevelSelect, JobTitleMapSelect union(byName: true)~> union1'
        'union1 sink(allowSchemaDrift: true,'
        '     validateSchema: false,'
        '     deletable:false,'
        '     insertable:true,'
        '     updateable:false,'
        '     upsertable:false,'
        '     format: \'table\','
        '     skipDuplicateMapInputs: true,'
        '     skipDuplicateMapOutputs: true,'
        '     saveOrder: 1,'
        '     errorHandlingOption: \'stopOnFirstError\') ~> sink1'
      ]
    }
  }
  dependsOn: [
    dataSet_CountryMap
    dataSet_JobLevelMap
    dataSet_JobTitleMap
    dataSet_MappingsTable
  ]
}

resource dataFlow_PopulateDestinationDataFlow 'Microsoft.DataFactory/factories/dataflows@2018-06-01' = {
  parent: dataFactory
  name: populateDestinationDataFlow
  properties: {
    type: 'MappingDataFlow'
    typeProperties: {
      sources: [
        {
          dataset: {
            referenceName: memberIdsDataSet
            type: 'DatasetReference'
          }
          name: 'memberids'
        }
        {
          dataset: {
            referenceName: memberHRDataDataSet
            type: 'DatasetReference'
          }
          name: 'memberHRData'
        }
      ]
      sinks: [
        {
          dataset: {
            referenceName: destinationTableDataSet
            type: 'DatasetReference'
          }
          name: 'sink'
        }
      ]
      transformations: [
        {
          name: 'join'
        }
        {
          name: 'CastColumns'
        }
      ]
      scriptLines: [
        'source(output('
        '          PersonnelNumber as string,'
        '          AzureObjectId as string,'
        '          UserPrincipalName as string'
        '     ),'
        '     allowSchemaDrift: true,'
        '     validateSchema: false,'
        '     ignoreNoFilesFound: false,'
        '     wildcardPaths:[\'memberids.csv\']) ~> memberids'
        'source(output('
        '          EmployeeIdentificationNumber as string,'
        '          ManagerIdentificationNumber as string,'
        '          Department as string,'
        '          JobTitleCode as string,'
        '          JobLevelCode as string,'
        '          CountryCode as string'
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
        'join cast(output('
        '          PersonnelNumber as integer,'
        '          EmployeeIdentificationNumber as integer,'
        '          ManagerIdentificationNumber as integer'
        '     ),'
        '     errors: true) ~> CastColumns'
        'CastColumns sink(allowSchemaDrift: true,'
        '     validateSchema: false,'
        '     deletable:false,'
        '     insertable:true,'
        '     updateable:false,'
        '     upsertable:false,'
        '     format: \'table\','
        '     mapColumn('
        '          AzureObjectId,'
        '          Email = UserPrincipalName,'
        '          EmployeeId = PersonnelNumber,'
        '          ManagerId = ManagerIdentificationNumber,'
        '          JobTitle_Code = JobTitleCode,'
        '          JobLevel_Code = JobLevelCode,'
        '          Country_Code = CountryCode,'
        '          Department'
        '     )) ~> sink'
      ]
    }
  }
  dependsOn: [
    dataSet_DestinationTable
    dataSet_MemberIds
    dataSet_MemberHRData
  ]
}

output systemAssignedIdentityId string = dataFactory.identity.principalId
