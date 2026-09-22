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

@description('AgentReader function url.')
@secure()
param agentReaderUrl string

@description('AgentReader function key.')
@secure()
param agentReaderFunctionKey string

@description('Name of adf storage account')
@secure()
param storageAccountName string

@description('Function authentication app client id.')
param functionAuthAppClientId string

@description('Enable function authentication.')
param enableFunctionAuthentication bool = false

@description('Maximum number of users whose ManagerId is missing from the extract before PopulateDestinationPipeline fails.')
param maxInvalidUserCount int = 25

@description('Number of most recent tables to retain in each of the users and mappings schemas. Cleanup of older tables is attempted after all non-cleanup activities succeed.')
@minValue(2)
param tablesToRetain int = 30

var dataFactoryName = factoryName
var azureBlobStorageLinkedService = 'AzureBlobStorage_${resourceSuffix}'
var destinationDatabaseLinkedService = 'DestinationDatabase_${resourceSuffix}'
var azureUserReaderLinkedService = 'AzureUserReader_${resourceSuffix}'
var agentReaderLinkedService = 'AgentReader_${resourceSuffix}'
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

var tableCleanupScriptTemplate = 'DECLARE @SchemaName NVARCHAR(128);\nDECLARE @TableName NVARCHAR(128);\nDECLARE @SQL NVARCHAR(MAX);\n\nDECLARE table_cursor CURSOR FOR\nSELECT s.[name], t.[name]\nFROM sys.tables AS t\nINNER JOIN sys.schemas AS s ON t.schema_id = s.schema_id\nWHERE s.name = \'__SCHEMA__\'\nAND t.[name] NOT IN (\n    SELECT TOP (${tablesToRetain}) t.[name]\n    FROM sys.tables AS t\n    INNER JOIN sys.schemas AS s ON t.schema_id = s.schema_id\n    WHERE s.name = \'__SCHEMA__\'\n    ORDER BY t.create_date DESC\n);\n\nOPEN table_cursor;\nFETCH NEXT FROM table_cursor INTO @SchemaName, @TableName;\n\nWHILE @@FETCH_STATUS = 0\nBEGIN\n    BEGIN TRY\n        SET @SQL = N\'DROP TABLE \' + QUOTENAME(@SchemaName) + \'.\' + QUOTENAME(@TableName);\n        EXEC sp_executesql @SQL;\n    END TRY\n    BEGIN CATCH\n        PRINT \'Error dropping \' + @TableName + \': \' + ERROR_MESSAGE();\n    END CATCH\n    FETCH NEXT FROM table_cursor INTO @SchemaName, @TableName;\nEND;\n\nCLOSE table_cursor;\nDEALLOCATE table_cursor;'
var cleanUpUsersTablesScript = replace(tableCleanupScriptTemplate, '__SCHEMA__', 'users')
var cleanUpMappingsTablesScript = replace(tableCleanupScriptTemplate, '__SCHEMA__', 'mappings')

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
      serviceEndpoint: 'https://${storageAccountName}.blob.${environment().suffixes.storage}'
      accountKind: 'StorageV2'
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

resource linkedService_AzureUserReader 'Microsoft.DataFactory/factories/linkedServices@2018-06-01' = if (enableFunctionAuthentication) {
  parent: dataFactory
  name: azureUserReaderLinkedService
  properties: {
    annotations: []
    type: 'AzureFunction'
    typeProperties: {
      functionAppUrl: azureUserReaderUrl
      authentication: 'MSI'
      resourceId: 'api://${functionAuthAppClientId}'
      functionKey: {
        type: 'SecureString'
        value: azureUserReaderFunctionKey
      }
    }
  }
  dependsOn: []
}

resource linkedService_AzureUserReader_NoAuth 'Microsoft.DataFactory/factories/linkedServices@2018-06-01' = if (!enableFunctionAuthentication) {
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
    }
  }
  dependsOn: []
}

resource linkedService_AgentReader 'Microsoft.DataFactory/factories/linkedServices@2018-06-01' = if (enableFunctionAuthentication) {
  parent: dataFactory
  name: agentReaderLinkedService
  properties: {
    annotations: []
    type: 'AzureFunction'
    typeProperties: {
      functionAppUrl: agentReaderUrl
      authentication: 'MSI'
      resourceId: 'api://${functionAuthAppClientId}'
      functionKey: {
        type: 'SecureString'
        value: agentReaderFunctionKey
      }
    }
  }
  dependsOn: []
}

resource linkedService_AgentReader_NoAuth 'Microsoft.DataFactory/factories/linkedServices@2018-06-01' = if (!enableFunctionAuthentication) {
  parent: dataFactory
  name: agentReaderLinkedService
  properties: {
    annotations: []
    type: 'AzureFunction'
    typeProperties: {
      functionAppUrl: agentReaderUrl
      functionKey: {
        type: 'SecureString'
        value: agentReaderFunctionKey
      }
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
        name: 'Check Manager Hierarchy'
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
              type: 'Query'
              text: {
                value: '@concat(\'\nWITH u AS (SELECT EmployeeId, ManagerId FROM [users].[\',replace(pipeline().RunId,\'-\',\'\'),\']),\nr AS (SELECT COUNT(*) AS rootCount FROM u WHERE ManagerId IS NULL),\nbad AS (\n\tSELECT u.EmployeeId, u.ManagerId\n\tFROM u\n\tWHERE u.ManagerId IS NOT NULL\n\t\tAND NOT EXISTS (SELECT 1 FROM u m WHERE m.EmployeeId = u.ManagerId)\n)\nSELECT\n\t(SELECT rootCount FROM r) AS rootCount,\n\t(SELECT COUNT(*) FROM bad) AS invalidUserCount,\n\t(SELECT COUNT(*) FROM u) AS totalUserCount,\n\t(SELECT COUNT(DISTINCT ManagerId) FROM bad) AS missingManagerCount,\n\t${maxInvalidUserCount} AS maxInvalidUserCount,\n\tISNULL((SELECT STRING_AGG(CONCAT(EmployeeId, \'\':\'\', ISNULL(CAST(ManagerId AS VARCHAR(20)), \'\'<null>\'\')), \'\', \'\')\n\t\tFROM (SELECT TOP 10 EmployeeId, ManagerId FROM bad ORDER BY EmployeeId) s), \'\'\'\') AS invalidPairs,\n\tCASE WHEN (SELECT rootCount FROM r) = 1\n\t\tAND (SELECT COUNT(*) FROM bad) <= ${maxInvalidUserCount}\n\tTHEN 1 ELSE 0 END AS isValid\n\')'
                type: 'Expression'
              }
            }
          ]
          scriptBlockExecutionTimeout: '02:00:00'
        }
      }
      {
        name: 'Gate Manager Hierarchy Validation'
        type: 'IfCondition'
        dependsOn: [
          {
            activity: 'Check Manager Hierarchy'
            dependencyConditions: [
              'Succeeded'
            ]
          }
        ]
        userProperties: []
        typeProperties: {
          expression: {
            value: '@not(equals(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].isValid, 1))'
            type: 'Expression'
          }
          ifTrueActivities: [
            {
              name: 'Fail Invalid Manager Hierarchy'
              type: 'Fail'
              dependsOn: []
              userProperties: []
              typeProperties: {
                message: {
                  value: '@concat(\'Invalid manager hierarchy detected. invalidUserCount=\', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].invalidUserCount), \' of \', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].totalUserCount), \' (max \', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].maxInvalidUserCount), \'); rootCount=\', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].rootCount), \'; missingManagerCount=\', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].missingManagerCount), \'; invalidPairs=[\', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].invalidPairs), \']; query=SELECT EmployeeId,ManagerId FROM [users].[\', replace(pipeline().RunId,\'-\',\'\'), \'] u WHERE u.ManagerId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [users].[\', replace(pipeline().RunId,\'-\',\'\'), \'] m WHERE m.EmployeeId=u.ManagerId)\')'
                  type: 'Expression'
                }
                errorCode: 'InvalidManagerHierarchy'
              }
            }
          ]
          ifFalseActivities: [
            {
              name: 'Record Manager Hierarchy Status'
              type: 'SetVariable'
              dependsOn: []
              userProperties: []
              typeProperties: {
                variableName: 'managerHierarchyStatus'
                value: {
                  value: '@concat(\'Manager hierarchy within tolerance. invalidUserCount=\', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].invalidUserCount), \' of \', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].totalUserCount), \' (max \', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].maxInvalidUserCount), \'); rootCount=\', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].rootCount), \'; missingManagerCount=\', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].missingManagerCount), \'; invalidPairs=[\', string(activity(\'Check Manager Hierarchy\').output.resultSets[0].rows[0].invalidPairs), \']\')'
                  type: 'Expression'
                }
              }
            }
          ]
        }
      }
      {
        name: 'Add Height Column'
        type: 'Script'
        dependsOn: [
          {
            activity: 'Gate Manager Hierarchy Validation'
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
            {
              type: 'NonQuery'
              text: 'IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = \'agents\')\nBEGIN\n   EXEC(\'CREATE SCHEMA agents\')\nEND'
            }
          ]
          scriptBlockExecutionTimeout: '02:00:00'
        }
      }
      {
        name: 'Create Agents Table'
        type: 'Script'
        dependsOn: [
          {
            activity: 'CreateSchemas'
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
                value: '@concat(\'CREATE TABLE agents.[\',replace(pipeline().RunId,\'-\',\'\'),\'] (AgentObjectId nvarchar(36) NOT NULL, ManagerId int NOT NULL, BlueprintId nvarchar(200) NULL, AccountEnabled bit NOT NULL)\')'
                type: 'Expression'
              }
            }
          ]
          scriptBlockExecutionTimeout: '02:00:00'
        }
      }
      {
        name: 'Create Agents Index'
        type: 'Script'
        dependsOn: [
          {
            activity: 'Create Agents Table'
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
                value: '@concat(\'CREATE CLUSTERED INDEX IDX_Agents_ManagerId ON agents.[\',replace(pipeline().RunId,\'-\',\'\'),\'](ManagerId)\')'
                type: 'Expression'
              }
            }
          ]
          scriptBlockExecutionTimeout: '02:00:00'
        }
      }
      {
        name: 'AgentReader'
        type: 'AzureFunctionActivity'
        dependsOn: [
          {
            activity: 'Create Agents Index'
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
          functionName: 'StarterFunction'
          body: {
            value: '@concat(\'{"RunId":"\', pipeline().RunId, \'"}\')'
            type: 'Expression'
          }
          headers: {}
          method: 'POST'
        }
        linkedServiceName: {
          referenceName: agentReaderLinkedService
          type: 'LinkedServiceReference'
        }
      }
      {
        name: 'Check AgentReader Completed Status'
        type: 'Until'
        dependsOn: [
          {
            activity: 'AgentReader'
            dependencyConditions: [
              'Succeeded'
            ]
          }
        ]
        userProperties: []
        typeProperties: {
          expression: {
            value: '@not(or(equals(coalesce(activity(\'Check AgentReader Status\')?.output.runtimeStatus,\'DontCheck\'), \'Pending\'), equals(coalesce(activity(\'Check AgentReader Status\')?.output.runtimeStatus,\'DontCheck\'), \'Running\')))'
            type: 'Expression'
          }
          activities: [
            {
              name: 'Wait on AgentReader'
              type: 'Wait'
              dependsOn: []
              userProperties: []
              typeProperties: {
                waitTimeInSeconds: 30
              }
            }
            {
              name: 'Check AgentReader Status'
              type: 'WebActivity'
              dependsOn: [
                {
                  activity: 'Wait on AgentReader'
                  dependencyConditions: [
                    'Succeeded'
                  ]
                }
              ]
              policy: {
                timeout: '0.00:10:00'
                retry: 2
                retryIntervalInSeconds: 300
                secureOutput: false
                secureInput: false
              }
              userProperties: []
              typeProperties: {
                method: 'GET'
                headers: {}
                linkedServices: [
                  {
                    referenceName: agentReaderLinkedService
                    type: 'LinkedServiceReference'
                  }
                ]
                url: {
                  value: '@activity(\'AgentReader\').output.statusQueryGetUri'
                  type: 'Expression'
                }
                ...(enableFunctionAuthentication ? {
                  authentication: {
                    type: 'MSI'
                    resource: 'api://${functionAuthAppClientId}'
                  }
                } : {})
              }
            }
            {
              name: 'Set AgentReader Status'
              type: 'SetVariable'
              dependsOn: [
                {
                  activity: 'Check AgentReader Status'
                  dependencyConditions: [
                    'Succeeded'
                  ]
                }
              ]
              policy: {
                secureOutput: false
                secureInput: false
              }
              userProperties: []
              typeProperties: {
                variableName: 'agentReaderRuntimeStatus'
                value: {
                  value: '@coalesce(activity(\'Check AgentReader Status\')?.output.runtimeStatus,\'DontCheck\')'
                  type: 'Expression'
                }
              }
            }
          ]
          timeout: '0.03:00:00'
        }
      }
      {
        name: 'Gate AgentReader Terminal Status'
        type: 'IfCondition'
        dependsOn: [
          {
            activity: 'Check AgentReader Completed Status'
            dependencyConditions: [
              'Succeeded'
            ]
          }
        ]
        userProperties: []
        typeProperties: {
          expression: {
            value: '@not(equals(variables(\'agentReaderRuntimeStatus\'), \'Completed\'))'
            type: 'Expression'
          }
          ifTrueActivities: [
            {
              name: 'Fail AgentReader Step'
              type: 'Fail'
              dependsOn: []
              userProperties: []
              typeProperties: {
                message: {
                  value: '@concat(\'AgentReader did not complete successfully. Runtime status: \', variables(\'agentReaderRuntimeStatus\'))'
                  type: 'Expression'
                }
                errorCode: 'AgentReaderFailed'
              }
            }
          ]
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
      {
        name: 'Clean Up Old Users Tables'
        type: 'Script'
        dependsOn: [
          {
            activity: 'Calculate Height'
            dependencyConditions: [
              'Succeeded'
            ]
          }
          {
            activity: 'PopulateMappingsTableDataFlow'
            dependencyConditions: [
              'Succeeded'
            ]
          }
          {
            activity: 'Create Agents Index'
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
        linkedServiceName: {
          referenceName: destinationDatabaseLinkedService
          type: 'LinkedServiceReference'
        }
        typeProperties: {
          scripts: [
            {
              type: 'NonQuery'
              text: cleanUpUsersTablesScript
            }
          ]
          scriptBlockExecutionTimeout: '02:00:00'
        }
      }
      {
        name: 'Clean Up Old Mappings Tables'
        type: 'Script'
        dependsOn: [
          {
            activity: 'Clean Up Old Users Tables'
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
        linkedServiceName: {
          referenceName: destinationDatabaseLinkedService
          type: 'LinkedServiceReference'
        }
        typeProperties: {
          scripts: [
            {
              type: 'NonQuery'
              text: cleanUpMappingsTablesScript
            }
          ]
          scriptBlockExecutionTimeout: '02:00:00'
        }
      }
    ]
    policy: {
      elapsedTimeMetric: {}
    }
    variables: {
      managerHierarchyStatus: {
        type: 'String'
      }
      agentReaderRuntimeStatus: {
        type: 'String'
      }
    }
    annotations: []
  }
  dependsOn: [
    dataFlow_PopulateDestinationDataFlow
    dataFlow_PopulateMappingsTableDataFlow
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
      nullValue: ''
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
