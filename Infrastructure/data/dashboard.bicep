@description('Name of the dashboard.')
param dashboardName string

@description('Resource group to retrieve data from.')
param resourceGroup string

@description('Name of prereqs resource group')
param prereqsResourceGroup string

@description('Name of compute resource group')
param computeResourceGroup string

@description('Resource location.')
param location string

@description('Subscription Id for the environment')
param subscriptionId string

@description('Enter storage account name.')
param jobsStorageAccountName string

resource name_resource 'Microsoft.Portal/dashboards@2015-08-01-preview' = {
  name: resourceGroup
  location: location
  tags: {
    'hidden-title': dashboardName
  }
  properties: {
    lenses: {
      '0': {
        order: 0
        parts: {
          '0': {
            position: {
              x: 0
              y: 0
              colSpan: 1
              rowSpan: 1
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceId'
                  value: ''
                }
                {
                  name: 'subjectId'
                  value: '02847fd4-53ec-42f6-b9fb-c46a73c9029e'
                }
              ]
              type: 'Extension/Microsoft_Azure_PIMCommon/PartType/RBACRoleBladePinnedPart'
              deepLink: '#blade/Microsoft_Azure_PIMCommon/ActivationMenuBlade/azurerbac'
            }
          }
          '1': {
            position: {
              x: 1
              y: 0
              colSpan: 6
              rowSpan: 2
            }
            metadata: {
              inputs: []
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              settings: {
                content: {
                  settings: {
                    content: '# <span style="color:blue">GMM Usage Dashboard</span>\n\n## <span style="color:blue">Summary</span>\n\nUse this dashboard to view live metrics and analyze usage of GMM across your tenant.'
                    title: ''
                    subtitle: ''
                    markdownSource: 1
                    markdownUri: null
                  }
                }
              }
            }
          }
          '2': {
            position: {
              x: 7
              y: 0
              colSpan: 3
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"])\n| where Result == "Success" and DryRun == false\n| distinct Destination\n| summarize Count = count()\n\n'
                  ControlType: 'AnalyticsGrid'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Sync Count'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'count_'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'Type'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '3': {
            position: {
              x: 10
              y: 0
              colSpan: 4
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    Count: '81px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"]),\n    SourceTypesCounts = tostring(customDimensions["SourceTypesCounts"])\n| where Result == "Success" and DryRun == false\n| extend Message = replace("{|}", "", SourceTypesCounts) // Remove curly braces\n| extend Message = split(Message, ",") // Split by comma to create an array\n| mv-expand Message\n| extend SourceType = tostring(split(Message, ":")[0]) // Extract the type\n| extend Count = toint(trim(" ", tostring(split(Message, ":")[1]))) // Extract the count and convert to int\n| order by Destination, timestamp\n| where Destination != prev(Destination) and Destination != \'\'\n| summarize Count = sum(Count) by SourceType // Sum the counts for each type\n| project SourceType, Count // Project the type and total count\n\n'
                  ControlType: 'AnalyticsGrid'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Source Parts By Type'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'count_'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'Type'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '4': {
            position: {
              x: 14
              y: 0
              colSpan: 3
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourcegroups/${resourceGroup}/providers/microsoft.operationalinsights/workspaces/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "SyncComplete"\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    Service: '117px'
                    Groups: '93px'
                  }
                  Query: 'ApplicationLog_CL \n | where location_s == "JobTrigger" and Message startswith "Linked services:" \n | join kind = inner ( \n    app("${resourceGroup}").customEvents \n    | where name == "SyncComplete" \n    | project timestamp,\n        Destination = tostring(customDimensions["Destination"]),\n        Result = tostring(customDimensions["Result"]),\n        DryRun = tobool(customDimensions["IsDryRunEnabled"])\n    | where Result == "Success" and DryRun == false\n    | distinct Destination\n)\non $left.Destination_s == $right.Destination\n| project TimeGenerated, Destination_s, TargetOfficeGroupId_g, Services = split(tostring(extract("services:(.*)", 1, Message)),",") \n| mv-expand Service = Services \n| distinct Destination_s, tostring(Service) \n| summarize Groups=count() by tostring(Service) \n | order by Groups\n\n'
                }
              }
              partHeader: {
                title: 'Services'
                subtitle: resourceGroup
              }
            }
          }
          '5': {
            position: {
              x: 0
              y: 1
              colSpan: 1
              rowSpan: 1
            }
            metadata: {
              inputs: [
                {
                  name: 'id'
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_Azure_Storage/PartType/StorageBrowserPart'
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Storage/storageAccounts/${jobsStorageAccountName}/storageexplorer'
            }
          }
          '6': {
            position: {
              x: 0
              y: 2
              colSpan: 1
              rowSpan: 1
            }
            metadata: {
              inputs: [
                {
                  name: 'demoMode'
                  isOptional: true
                }
                {
                  name: 'initiator'
                  value: 'PinnedAzBladePart'
                }
                {
                  name: 'scope'
                  value: {
                    resources: [
                      {
                        resourceId: '/subscriptions/${subscriptionId}/resourcegroups/${resourceGroup}/providers/microsoft.operationalinsights/workspaces/${resourceGroup}'
                      }
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'cachedResourceType'
                  isOptional: true
                }
                {
                  name: 'workspaceResourceId'
                  isOptional: true
                }
                {
                  name: 'query'
                  isOptional: true
                }
                {
                  name: 'isQueryBase64Compressed'
                  isOptional: true
                }
                {
                  name: 'timespanInIsoFormat'
                  isOptional: true
                }
                {
                  name: 'isQueryEditorVisible'
                  isOptional: true
                }
                {
                  name: 'environment'
                  isOptional: true
                }
                {
                  name: 'telemetryInfo'
                  isOptional: true
                }
                {
                  name: 'tabTitle'
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/AnalyticsPart'
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.OperationalInsights/workspaces/${resourceGroup}/logs'
            }
          }
          '7': {
            position: {
              x: 1
              y: 2
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),\n    Type = tostring(customDimensions["Type"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"])\n| where Result == "Success" and DryRun == false\n| summarize by Destination, Type, Bin = bin(timestamp, 1d)\n| summarize count() by Bin, Type\n\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Sync Jobs Successful By Destination'
                  Dimensions: {
                    xAxis: {
                      name: 'Bin'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'count_'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'Type'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '8': {
            position: {
              x: 7
              y: 2
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),\n    Type = tostring(customDimensions["Type"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"]),\n    Onboarding = tobool(customDimensions["IsInitialSync"])\n| where Result == "Success" and DryRun == false and Onboarding == true\n| summarize count() by bin(timestamp, 1d), Type\n\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Onboardings Per Day'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'count_'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'Type'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '9': {
            position: {
              x: 13
              y: 2
              colSpan: 4
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "NumberOfJobsStarted"'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: '# of Jobs Started'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "NumberOfJobsStarted"\n| order by timestamp desc\n| project timestamp,    \n    RunId = tostring(customDimensions["RunId"])\n| distinct RunId\n| summarize Count = count()\n'
                  PartTitle: '# of Jobs Started'
                  Partsubtitle: resourceGroup
                }
              }
            }
          }
          '10': {
            position: {
              x: 0
              y: 3
              colSpan: 1
              rowSpan: 1
            }
            metadata: {
              inputs: [
                {
                  name: 'ResourceId'
                  value: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                }
              ]
              type: 'Extension/AppInsightsExtension/PartType/CuratedBladeFailuresPinnedPart'
              isAdapter: true
              asset: {
                idInputName: 'ResourceId'
                type: 'ApplicationInsights'
              }
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}/failures'
            }
          }
          '11': {
            position: {
              x: 13
              y: 4
              colSpan: 4
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "NumberOfJobsCompleted"'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: '# of Jobs Completed'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "NumberOfJobsCompleted"\n| order by timestamp desc\n| project timestamp,    \n    RunId = tostring(customDimensions["RunId"]),\n    Status = tostring(customDimensions["Status"]),\n    ResultStatus = tostring(customDimensions["ResultStatus"])\n| where ResultStatus == "Success" or ResultStatus == "Failure"\n| distinct RunId\n| summarize Count = count()\n\n'
                  PartTitle: '# of Jobs Completed'
                  Partsubtitle: resourceGroup
                }
              }
            }
          }
          '12': {
            position: {
              x: 1
              y: 6
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"]),\n    ToAdd = toint(customDimensions["MembersAdded"]),\n    ToRemove = toint(customDimensions["MembersRemoved"])\n| where Result == "Success" and DryRun == false\n| summarize UsersAdded = sum(ToAdd), UsersRemoved = sum(ToRemove) by bin(timestamp, 1d)\n\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Total Members Added and Removed'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'UsersAdded'
                        type: 'long'
                      }
                      {
                        name: 'UsersRemoved'
                        type: 'long'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '13': {
            position: {
              x: 7
              y: 6
              colSpan: 5
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'sharedTimeRange'
                  isOptional: true
                }
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                          }
                          name: 'customMetrics/MembersAdded'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components/kusto'
                          metricVisualization: {
                            displayName: 'MembersAdded'
                          }
                        }
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                          }
                          name: 'customMetrics/MembersRemoved'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components/kusto'
                          metricVisualization: {
                            displayName: 'MembersRemoved'
                          }
                        }
                      ]
                      title: 'Sync Members Added and Removed'
                      titleKind: 2
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                        axisVisualization: {
                          x: {
                            isVisible: true
                            axisType: 2
                          }
                          y: {
                            isVisible: true
                            axisType: 1
                          }
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 604800000
                        }
                        showUTCTime: false
                        grain: 1
                      }
                    }
                  }
                  isOptional: true
                }
              ]
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              settings: {
                content: {
                  options: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                          }
                          name: 'customMetrics/MembersAdded'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components/kusto'
                          metricVisualization: {
                            displayName: 'MembersAdded'
                          }
                        }
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                          }
                          name: 'customMetrics/MembersRemoved'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components/kusto'
                          metricVisualization: {
                            displayName: 'MembersRemoved'
                          }
                        }
                      ]
                      title: 'Sync Members Added and Removed'
                      titleKind: 2
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                        axisVisualization: {
                          x: {
                            isVisible: true
                            axisType: 2
                          }
                          y: {
                            isVisible: true
                            axisType: 1
                          }
                        }
                        disablePinning: true
                      }
                    }
                  }
                }
              }
            }
          }
          '14': {
            position: {
              x: 12
              y: 6
              colSpan: 5
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'sharedTimeRange'
                  isOptional: true
                }
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                          }
                          name: 'customMetrics/MembersAddedFromOnboarding'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components/kusto'
                          metricVisualization: {
                            displayName: 'MembersAddedFromOnboarding'
                          }
                        }
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                          }
                          name: 'customMetrics/MembersRemovedFromOnboarding'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components/kusto'
                          metricVisualization: {
                            displayName: 'MembersRemovedFromOnboarding'
                          }
                        }
                      ]
                      title: 'Onboarding Members Added and Removed'
                      titleKind: 2
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                        axisVisualization: {
                          x: {
                            isVisible: true
                            axisType: 2
                          }
                          y: {
                            isVisible: true
                            axisType: 1
                          }
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 604800000
                        }
                        showUTCTime: false
                        grain: 1
                      }
                    }
                  }
                  isOptional: true
                }
              ]
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              settings: {
                content: {
                  options: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                          }
                          name: 'customMetrics/MembersAddedFromOnboarding'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components/kusto'
                          metricVisualization: {
                            displayName: 'MembersAddedFromOnboarding'
                          }
                        }
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                          }
                          name: 'customMetrics/MembersRemovedFromOnboarding'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components/kusto'
                          metricVisualization: {
                            displayName: 'MembersRemovedFromOnboarding'
                          }
                        }
                      ]
                      title: 'Onboarding Members Added and Removed'
                      titleKind: 2
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                        axisVisualization: {
                          x: {
                            isVisible: true
                            axisType: 2
                          }
                          y: {
                            isVisible: true
                            axisType: 1
                          }
                        }
                        disablePinning: true
                      }
                    }
                  }
                }
              }
            }
          }
          '15': {
            position: {
              x: 1
              y: 10
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"]),\n    ToAdd = toint(customDimensions["MembersAdded"]),\n    ToRemove = toint(customDimensions["MembersRemoved"])\n| where Result == "Success" and DryRun == false and Destination != \'\'\n| extend DestinationType = tostring(parse_json(Destination)[0].type)\n| summarize UsersAdded = sum(ToAdd) by bin(timestamp, 1d), DestinationType\n\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Total Members Added by Destination'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'UsersAdded'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'DestinationType'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '16': {
            position: {
              x: 7
              y: 10
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"]),\n    ToAdd = toint(customDimensions["MembersAdded"]),\n    ToRemove = toint(customDimensions["MembersRemoved"])\n| where Result == "Success" and DryRun == false and Destination != \'\'\n| extend DestinationType = tostring(parse_json(Destination)[0].type)\n| summarize UsersRemoved = sum(ToRemove) by bin(timestamp, 1d), DestinationType\n\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Total Members Removed by Destination'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'UsersRemoved'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'DestinationType'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '17': {
            position: {
              x: 13
              y: 10
              colSpan: 5
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '10b3c2a8-28c3-4b74-a637-e5305b696ec4'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P7D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'FrameControlChart'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  value: 'StackedColumn'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  value: {
                    xAxis: {
                      name: 'DurationBin'
                      type: 'string'
                    }
                    yAxis: [
                      {
                        name: 'Jobs'
                        type: 'int'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  value: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project MembersAdded = toint(customDimensions["MembersAdded"]),\n    MembersRemoved = toint(customDimensions["MembersRemoved"]),\n    Destination = toguid(customDimensions["Destination"]),\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"],\n    RunId = toguid(customDimensions["RunId"])\n| where Result == "Success" and DryRun == "False"\n| distinct RunId, MembersAdded, MembersRemoved\n| summarize NoOps = countif(MembersAdded == 0 and MembersRemoved == 0), Total = count()\n| extend NoOpsPercent = todouble(NoOps) / Total * 100\n\n'
                  ControlType: 'AnalyticsGrid'
                }
              }
              partHeader: {
                title: 'No-Op Syncs'
                subtitle: ''
              }
            }
          }
          '18': {
            position: {
              x: 13
              y: 12
              colSpan: 5
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Sync Job Reliability %'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    ReliabilityPercentage: '458.993px'
                  }
                  Query: 'let P1 = toscalar(\n    customEvents\n\t| where name == "IdleJobsTracker"\n\t| order by timestamp desc\n\t| project timestamp,\n\t\t\t  Id = tostring(customDimensions["Destination"]),\n\t\t\t  Frequency = toint(customDimensions["Frequency"]),\n\t\t\t  JobStarted = toint(customDimensions["JobStarted"])\n\t| summarize StartReliabilityPercentage = (toreal(countif(JobStarted >= 1)) / sum(Frequency)) * 100\n);\nlet P2 = toscalar(\n    customEvents\n\t| where name == "InProgressJobsTracker"\n\t| order by timestamp desc\n\t| project timestamp,\n\t\tId = tostring(customDimensions["Destination"]),\n\t\tFrequency = toint(customDimensions["Frequency"]),\n\t\tRunId = toguid(customDimensions["RunId"])\n\t| where Frequency > 0\n\t| summarize max(Frequency) by RunId\n\t| join kind=rightouter (\n\t\tcustomEvents\n\t\t| where name == "NumberOfJobsStarted"\n\t\t| order by timestamp desc\n\t\t| project timestamp,\n\t\t\tJobStartedRunId = toguid(customDimensions["RunId"])\n\t\t| distinct JobStartedRunId)\n\t\ton $left.RunId == $right.JobStartedRunId\n\t| summarize\n\t\tCountOfJobsThatDidNotGoOverPeriod = (toreal(countif(isnull(RunId)))),\n\t\tCountOfAllExpectedJobs =  (countif(JobStartedRunId != "") + sum(max_Frequency) - countif(max_Frequency > 0))\n\t| project InProgressReliabilityPercentage = CountOfJobsThatDidNotGoOverPeriod / CountOfAllExpectedJobs * 100\n);\nprint ReliabilityPercentage = (P1 + P2) / 2\n\n'
                  ControlType: 'AnalyticsGrid'
                }
              }
              partHeader: {
                title: 'Sync Job Reliability %'
                subtitle: resourceGroup
              }
            }
          }
          '19': {
            position: {
              x: 1
              y: 14
              colSpan: 5
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '10b3c2a8-28c3-4b74-a637-e5305b696ec4'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P7D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'FrameControlChart'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  value: 'StackedColumn'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  value: {
                    xAxis: {
                      name: 'DurationBin'
                      type: 'string'
                    }
                    yAxis: [
                      {
                        name: 'Jobs'
                        type: 'int'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  value: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project MembersAdded = toint(customDimensions["MembersAdded"]),\n          Destination = tostring(customDimensions["Destination"]),\n          Result = customDimensions["Result"],\n          DryRun = customDimensions["IsDryRunEnabled"],\n          RunId = toguid(customDimensions["RunId"])\n| where Result == "Success" and DryRun == "False"\n| extend DestinationJson = parse_json(Destination)\n| project MembersAdded, \n          ObjectId = tostring(DestinationJson[0]["value"]["objectId"]),\n          ChannelId = tostring(DestinationJson[0]["value"]["channelId"]),\n          RunId\n| distinct MembersAdded, ObjectId, ChannelId, RunId\n| order by MembersAdded desc\n\n'
                  ControlType: 'AnalyticsGrid'
                }
              }
              partHeader: {
                title: 'Members Added to Destination'
                subtitle: 'Descending order'
              }
            }
          }
          '20': {
            position: {
              x: 6
              y: 14
              colSpan: 5
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '10b3c2a8-28c3-4b74-a637-e5305b696ec4'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P7D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'FrameControlChart'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  value: 'StackedColumn'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  value: {
                    xAxis: {
                      name: 'DurationBin'
                      type: 'string'
                    }
                    yAxis: [
                      {
                        name: 'Jobs'
                        type: 'int'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  value: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project MembersRemoved = toint(customDimensions["MembersRemoved"]),\n          Destination = tostring(customDimensions["Destination"]),\n          Result = customDimensions["Result"],\n          DryRun = customDimensions["IsDryRunEnabled"],\n          RunId = toguid(customDimensions["RunId"])\n| where Result == "Success" and DryRun == "False"\n| extend DestinationJson = parse_json(Destination)\n| project MembersRemoved, \n          ObjectId = tostring(DestinationJson[0]["value"]["objectId"]),\n          ChannelId = tostring(DestinationJson[0]["value"]["channelId"]),\n          RunId\n| distinct MembersRemoved, ObjectId, ChannelId, RunId\n| order by MembersRemoved desc\n'
                  ControlType: 'AnalyticsGrid'
                }
              }
              partHeader: {
                title: 'Members Removed from Destination'
                subtitle: 'Descending order'
              }
            }
          }
          '21': {
            position: {
              x: 12
              y: 14
              colSpan: 6
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourcegroups/${resourceGroup}/providers/microsoft.operationalinsights/workspaces/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1592d33b-1422-45a4-92b6-b23302415882'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    Message: '379px'
                  }
                  Query: 'AzureDiagnostics \n| where Category == "PipelineRuns" and pipelineName_s contains "Pipeline" \n| summarize arg_max(TimeGenerated, *) by runId_g\n| project TimeGenerated, Resource, status_s\n| order by TimeGenerated desc, Resource\n'
                  PartTitle: 'Pipeline Run Status'
                }
              }
            }
          }
          '22': {
            position: {
              x: 1
              y: 18
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'scope'
                  value: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}'
                }
                {
                  name: 'scopeName'
                  value: resourceGroup
                }
                {
                  name: 'view'
                  value: {
                    currency: 'USD'
                    query: {
                      type: 'ActualCost'
                      dataSet: {
                        granularity: 'Daily'
                        aggregation: {
                          totalCost: {
                            name: 'Cost'
                            function: 'Sum'
                          }
                          totalCostUSD: {
                            name: 'CostUSD'
                            function: 'Sum'
                          }
                        }
                        sorting: [
                          {
                            direction: 'ascending'
                            name: 'UsageDate'
                          }
                        ]
                        grouping: [
                          {
                            type: 'Dimension'
                            name: 'MeterCategory'
                          }
                        ]
                      }
                      timeframe: 'None'
                    }
                    chart: 'Area'
                    accumulated: 'false'
                    pivots: [
                      {
                        type: 'Dimension'
                        name: 'ServiceName'
                      }
                      {
                        type: 'Dimension'
                        name: 'ResourceLocation'
                      }
                      {
                        type: 'Dimension'
                        name: 'ResourceId'
                      }
                    ]
                    scope: 'subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}'
                    kpis: [
                      {
                        type: 'Budget'
                        id: 'COST_NAVIGATOR.BUDGET_OPTIONS.NONE'
                        enabled: true
                        extendedProperties: {
                          name: 'COST_NAVIGATOR.BUDGET_OPTIONS.NONE'
                        }
                      }
                      {
                        type: 'Forecast'
                        enabled: true
                      }
                    ]
                    displayName: 'DailyCosts'
                  }
                  isOptional: true
                }
                {
                  name: 'externalState'
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_Azure_CostManagement/PartType/CostAnalysisPinPart'
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/costanalysis'
              partHeader: {
                title: 'Daily Costs'
                subtitle: resourceGroup
              }
            }
          }
          '23': {
            position: {
              x: 7
              y: 18
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'scope'
                  value: '/subscriptions/${subscriptionId}/resourceGroups/${prereqsResourceGroup}'
                }
                {
                  name: 'scopeName'
                  value: prereqsResourceGroup
                }
                {
                  name: 'view'
                  value: {
                    currency: 'USD'
                    query: {
                      type: 'ActualCost'
                      dataSet: {
                        granularity: 'Daily'
                        aggregation: {
                          totalCost: {
                            name: 'Cost'
                            function: 'Sum'
                          }
                          totalCostUSD: {
                            name: 'CostUSD'
                            function: 'Sum'
                          }
                        }
                        sorting: [
                          {
                            direction: 'ascending'
                            name: 'UsageDate'
                          }
                        ]
                        grouping: [
                          {
                            type: 'Dimension'
                            name: 'MeterCategory'
                          }
                        ]
                      }
                      timeframe: 'None'
                    }
                    chart: 'Area'
                    accumulated: 'false'
                    pivots: [
                      {
                        type: 'Dimension'
                        name: 'ServiceName'
                      }
                      {
                        type: 'Dimension'
                        name: 'ResourceLocation'
                      }
                      {
                        type: 'Dimension'
                        name: 'ResourceId'
                      }
                    ]
                    scope: 'subscriptions/${subscriptionId}/resourceGroups/${prereqsResourceGroup}'
                    kpis: [
                      {
                        type: 'Budget'
                        id: 'COST_NAVIGATOR.BUDGET_OPTIONS.NONE'
                        enabled: true
                        extendedProperties: {
                          name: 'COST_NAVIGATOR.BUDGET_OPTIONS.NONE'
                        }
                      }
                      {
                        type: 'Forecast'
                        enabled: true
                      }
                    ]
                    displayName: 'DailyCosts'
                  }
                  isOptional: true
                }
                {
                  name: 'externalState'
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_Azure_CostManagement/PartType/CostAnalysisPinPart'
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${prereqsResourceGroup}/costanalysis'
              partHeader: {
                title: 'Daily Costs'
                subtitle: prereqsResourceGroup
              }
            }
          }
          '24': {
            position: {
              x: 13
              y: 18
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'scope'
                  value: '/subscriptions/${subscriptionId}/resourceGroups/${computeResourceGroup}'
                }
                {
                  name: 'scopeName'
                  value: computeResourceGroup
                }
                {
                  name: 'view'
                  value: {
                    currency: 'USD'
                    query: {
                      type: 'ActualCost'
                      dataSet: {
                        granularity: 'Daily'
                        aggregation: {
                          totalCost: {
                            name: 'Cost'
                            function: 'Sum'
                          }
                          totalCostUSD: {
                            name: 'CostUSD'
                            function: 'Sum'
                          }
                        }
                        sorting: [
                          {
                            direction: 'ascending'
                            name: 'UsageDate'
                          }
                        ]
                        grouping: [
                          {
                            type: 'Dimension'
                            name: 'MeterSubCategory'
                          }
                        ]
                      }
                      timeframe: 'None'
                    }
                    chart: 'Area'
                    accumulated: 'false'
                    pivots: [
                      {
                        type: 'Dimension'
                        name: 'ServiceName'
                      }
                      {
                        type: 'Dimension'
                        name: 'ResourceLocation'
                      }
                      {
                        type: 'Dimension'
                        name: 'ResourceId'
                      }
                    ]
                    scope: 'subscriptions/${subscriptionId}/resourceGroups/${computeResourceGroup}'
                    kpis: [
                      {
                        type: 'Budget'
                        id: 'COST_NAVIGATOR.BUDGET_OPTIONS.NONE'
                        enabled: true
                        extendedProperties: {
                          name: 'COST_NAVIGATOR.BUDGET_OPTIONS.NONE'
                        }
                      }
                      {
                        type: 'Forecast'
                        enabled: true
                      }
                    ]
                    displayName: 'DailyCosts'
                  }
                  isOptional: true
                }
                {
                  name: 'externalState'
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_Azure_CostManagement/PartType/CostAnalysisPinPart'
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${computeResourceGroup}/costanalysis'
              partHeader: {
                title: 'Daily Costs'
                subtitle: computeResourceGroup
              }
            }
          }
          '25': {
            position: {
              x: 1
              y: 22
              colSpan: 19
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '9f2f9f83-cec3-41fd-b120-f3fb165905c5'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    Type: '98px'
                    GroupsLessThan1k: '144px'
                    Groups1kTo5k: '118px'
                    Groups5kTo10k: '126px'
                    Groups10kTo25k: '138px'
                    Groups25kTo50k: '137px'
                    Groups50kTo75k: '135px'
                    Groups75kTo100k: '142px'
                    Groups100kTo200k: '148px'
                    Groups200kTo300k: '150px'
                    LessThan1k: '106px'
                    From1kTo5k: '114px'
                    From5kTo10k: '117px'
                    From10kTo25k: '124px'
                    From25kTo50k: '125px'
                    From50kTo75k: '124px'
                    From75kTo100k: '129px'
                    From100kTo200k: '135px'
                    From200kTo300k: '139px'
                    From300kTo400k: '137px'
                    MoreThan400k: '126px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),    \n    Result = tostring(customDimensions["Result"]),\n    MemberCount = toint(customDimensions["ProjectedMemberCount"])\n| where Result == "Success"\n| summarize MaxMemberCount = max(MemberCount) by Destination\n| summarize All = countif(MaxMemberCount >= 0), MoreThan1k = countif(MaxMemberCount >= 1000), MoreThan5k = countif(MaxMemberCount >= 5000), MoreThan10k = countif(MaxMemberCount >= 10000), MoreThan25k = countif(MaxMemberCount >= 25000), MoreThan50k = countif(MaxMemberCount >= 50000), MoreThan75k = countif(MaxMemberCount >= 75000), MoreThan100k = countif(MaxMemberCount >= 100000), MoreThan200k = countif(MaxMemberCount >= 200000), MoreThan300k = countif(MaxMemberCount >= 300000), MoreThan400k = countif(MaxMemberCount >= 400000)\n\n'
                  PartTitle: 'Group Counts based on Size Buckets'
                  Partsubtitle: resourceGroup
                }
              }
            }
          }
          '26': {
            position: {
              x: 1
              y: 24
              colSpan: 17
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '9f2f9f83-cec3-41fd-b120-f3fb165905c5'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    Type: '98px'
                    GroupsLessThan1k: '144px'
                    Groups1kTo5k: '118px'
                    Groups5kTo10k: '126px'
                    Groups10kTo25k: '138px'
                    Groups25kTo50k: '137px'
                    Groups50kTo75k: '135px'
                    Groups75kTo100k: '142px'
                    Groups100kTo200k: '148px'
                    Groups200kTo300k: '150px'
                    LessThan1k: '106px'
                    From1kTo5k: '114px'
                    From5kTo10k: '117px'
                    From10kTo25k: '124px'
                    From25kTo50k: '125px'
                    From50kTo75k: '124px'
                    From75kTo100k: '129px'
                    From100kTo200k: '135px'
                    From200kTo300k: '139px'
                    From300kTo400k: '137px'
                    MoreThan400k: '126px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),    \n    Result = tostring(customDimensions["Result"]),\n    MemberCount = toint(customDimensions["ProjectedMemberCount"])\n| where Result == "Success"\n| summarize MaxMemberCount = max(MemberCount) by Destination\n| summarize LessThan1k = countif(MaxMemberCount < 1000), From1kTo5k = countif(MaxMemberCount >= 1000 and MaxMemberCount < 5000), From5kTo10k = countif(MaxMemberCount >= 5000 and MaxMemberCount < 10000), From10kTo25k = countif(MaxMemberCount >= 10000 and MaxMemberCount < 25000), From25kTo50k = countif(MaxMemberCount >= 25000 and MaxMemberCount < 50000), From50kTo75k = countif(MaxMemberCount >= 50000 and MaxMemberCount < 75000), From75kTo100k = countif(MaxMemberCount >= 75000 and MaxMemberCount < 100000), From100kTo200k = countif(MaxMemberCount >= 100000 and MaxMemberCount < 200000), From200kTo300k = countif(MaxMemberCount >= 200000 and MaxMemberCount < 300000), From300kTo400k = countif(MaxMemberCount >= 300000 and MaxMemberCount < 400000), MoreThan400k = countif(MaxMemberCount >= 400000)\n\n'
                  PartTitle: 'Group Counts based on Size Buckets'
                  Partsubtitle: resourceGroup
                }
              }
            }
          }
          '27': {
            position: {
              x: 1
              y: 26
              colSpan: 7
              rowSpan: 3
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    TargetOfficeGroupId: '248px'
                    SourceGroupObjectId: '259px'
                    NestedGroupCount: '167px'
                  }
                  Query: 'customEvents\n| where name == "NestedGroupCount"\n| extend DestinationJson = parse_json(tostring(customDimensions["Destination"]))\n| project timestamp,\n          ObjectId = tostring(DestinationJson[0]["value"]["objectId"]),\n          ChannelId = tostring(DestinationJson[0]["value"]["channelId"]),\n          SourceGroupObjectId = tostring(customDimensions["SourceGroupObjectId"]),\n          NestedGroupCount = toint(customDimensions["NestedGroupCount"])\n| summarize arg_max(timestamp, NestedGroupCount) by ObjectId, ChannelId\n| project ObjectId, ChannelId, NestedGroupCount\n| order by NestedGroupCount desc\n'
                  ControlType: 'AnalyticsGrid'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'NestedGroupCount'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'count_'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'Type'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
              partHeader: {
                title: 'NestedGroupCount'
                subtitle: resourceGroup
              }
            }
          }
          '28': {
            position: {
              x: 8
              y: 26
              colSpan: 7
              rowSpan: 3
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    timestamp: '101px'
                    GroupObjectId: '209px'
                    UsersInCache: '70px'
                    RunId: '209px'
                  }
                  Query: 'customEvents\n| where name == "UsersInCacheCount"\n| project timestamp,    \n    RunId = tostring(customDimensions["RunId"]),\n    GroupObjectId = tostring(customDimensions["GroupObjectId"]),\n    UsersInCache = toint(customDimensions["UsersInCache"])    \n| distinct timestamp, GroupObjectId, UsersInCache, RunId\n| order by timestamp desc\n\n'
                  ControlType: 'AnalyticsGrid'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'UsersInCacheCount'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'count_'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'Type'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
              partHeader: {
                title: 'UsersInCacheCount'
                subtitle: resourceGroup
              }
            }
          }
          '29': {
            position: {
              x: 15
              y: 26
              colSpan: 6
              rowSpan: 3
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    TargetOfficeGroupId: '274px'
                    Total: '150px'
                    Exclusionary: '150px'
                  }
                  Query: 'customEvents\n| where name == "ExclusionarySourcePartsCount"\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),\n    Exclusionary = toint(customDimensions["NumberOfExclusionarySourceParts"]),\n    Total = toint(customDimensions["TotalNumberOfSourceParts"]) \n| extend DestinationJson = parse_json(Destination)\n| project timestamp,\n          Exclusionary,\n          Total,\n          ObjectId = tostring(DestinationJson[0]["value"]["objectId"]),\n          ChannelId = tostring(DestinationJson[0]["value"]["channelId"])\n| summarize arg_max(timestamp, Exclusionary, Total) by ObjectId, ChannelId\n| project ObjectId, ChannelId, Exclusionary, Total\n| order by Exclusionary desc\n'
                  PartTitle: 'ExclusionarySourcePartsCount'
                  Partsubtitle: resourceGroup
                }
              }
            }
          }
          '30': {
            position: {
              x: 1
              y: 30
              colSpan: 7
              rowSpan: 2
            }
            metadata: {
              inputs: []
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              settings: {
                content: {
                  settings: {
                    content: '# <span style="color:green">Performance Monitor Dashboard</span>\r\n\r\n## <span style="color:green">Summary</span>\r\n\r\nUse this dashboard to view data associated with GMM\'s overall performance in terms of sync runtime.'
                    title: ''
                    subtitle: ''
                    markdownSource: 1
                    markdownUri: null
                  }
                }
              }
            }
          }
          '31': {
            position: {
              x: 1
              y: 32
              colSpan: 14
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '15bd1362-68dd-413e-a9fd-87c931d2c932'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\nResult = customDimensions["Result"],\nDryRun = customDimensions["IsDryRunEnabled"],\nType = customDimensions["Type"]\n| where Result == "Success" and DryRun == "False"\n| project Minutes, tostring(Type)\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100) by Type\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    percentile_Minutes_50: '169px'
                    percentile_Minutes_75: '173px'
                    percentile_Minutes_95: '175px'
                    percentile_Minutes_99: '169px'
                    percentile_Minutes_100: '183px'
                    Type: '105px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"])\n| where Result == "Success" and DryRun == false\n| project Minutes\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100)\n'
                  PartTitle: 'Overall Sync Job Run Durations'
                  Partsubtitle: resourceGroup
                }
              }
            }
          }
          '32': {
            position: {
              x: 1
              y: 34
              colSpan: 3
              rowSpan: 4
            }
            metadata: {
              inputs: []
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              settings: {
                content: {
                  settings: {
                    content: '### <span style="color:lightseagreen">Onboarding vs. Ongoing</span>\r\n\r\nThe two tiles on the right show percentiles for sync job run durations, separating onboarding sync jobs and ongoing sync jobs.'
                    title: ''
                    subtitle: ''
                    markdownSource: 1
                    markdownUri: null
                  }
                }
              }
            }
          }
          '33': {
            position: {
              x: 4
              y: 34
              colSpan: 11
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '15bd1362-68dd-413e-a9fd-87c931d2c932'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\nResult = customDimensions["Result"],\nDryRun = customDimensions["IsDryRunEnabled"],\nType = customDimensions["Type"]\n| where Result == "Success" and DryRun == "False"\n| project Minutes, tostring(Type)\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100) by Type\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    percentile_Minutes_50: '171px'
                    Type: '108px'
                    percentile_Minutes_75: '170px'
                    percentile_Minutes_95: '172px'
                    percentile_Minutes_99: '170px'
                    percentile_Minutes_100: '180px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"]),\n    Onboarding = tobool(customDimensions["IsInitialSync"])\n| where Result == "Success" and DryRun == false and Onboarding == true\n| project Minutes\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100)\n'
                  PartTitle: 'Onboarding Sync Job Run Durations Chart'
                }
              }
            }
          }
          '34': {
            position: {
              x: 4
              y: 36
              colSpan: 11
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '15bd1362-68dd-413e-a9fd-87c931d2c932'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\nResult = customDimensions["Result"],\nDryRun = customDimensions["IsDryRunEnabled"],\nType = customDimensions["Type"]\n| where Result == "Success" and DryRun == "False"\n| project Minutes, tostring(Type)\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100) by Type\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    percentile_Minutes_50: '172px'
                    Type: '108px'
                    percentile_Minutes_75: '172px'
                    percentile_Minutes_95: '172px'
                    percentile_Minutes_99: '171px'
                    percentile_Minutes_100: '176px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,    \n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"]),\n    Onboarding = tobool(customDimensions["IsInitialSync"])\n| where Result == "Success" and DryRun == false and Onboarding == false\n| project Minutes\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100)\n'
                  PartTitle: 'Ongoing Sync Job Run Durations Chart'
                }
              }
            }
          }
          '35': {
            position: {
              x: 1
              y: 38
              colSpan: 3
              rowSpan: 8
            }
            metadata: {
              inputs: []
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              settings: {
                content: {
                  settings: {
                    content: '### <span style="color:lightseagreen">By Destination Size Range</span>\r\n\r\nThe four tiles on the right show percentiles for sync job run durations, separating them by ranges between the sizes of destinations.'
                    title: ''
                    subtitle: ''
                    markdownSource: 1
                    markdownUri: null
                  }
                }
              }
            }
          }
          '36': {
            position: {
              x: 4
              y: 38
              colSpan: 11
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '15bd1362-68dd-413e-a9fd-87c931d2c932'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\nResult = customDimensions["Result"],\nDryRun = customDimensions["IsDryRunEnabled"],\nType = customDimensions["Type"]\n| where Result == "Success" and DryRun == "False"\n| project Minutes, tostring(Type)\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100) by Type\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    percentile_Minutes_50: '173px'
                    Type: '112px'
                    percentile_Minutes_75: '170px'
                    percentile_Minutes_95: '172px'
                    percentile_Minutes_99: '170px'
                    percentile_Minutes_100: '175px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"],   \n    Size = customDimensions["ProjectedMemberCount"]\n| where Result == "Success" and DryRun == "False" and Size < 10000\n| project Minutes\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100)\n'
                  PartTitle: 'Size <10k Sync Job Durations'
                }
              }
            }
          }
          '37': {
            position: {
              x: 4
              y: 40
              colSpan: 11
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '15bd1362-68dd-413e-a9fd-87c931d2c932'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\nResult = customDimensions["Result"],\nDryRun = customDimensions["IsDryRunEnabled"],\nType = customDimensions["Type"]\n| where Result == "Success" and DryRun == "False"\n| project Minutes, tostring(Type)\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100) by Type\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    percentile_Minutes_50: '173px'
                    Type: '111px'
                    percentile_Minutes_75: '170px'
                    percentile_Minutes_95: '172px'
                    percentile_Minutes_99: '170px'
                    percentile_Minutes_100: '175px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"],\n    Size = customDimensions["ProjectedMemberCount"]\n| where Result == "Success" and DryRun == "False" and Size >= 10000 and Size < 50000\n| project Minutes\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100)'
                  PartTitle: 'Size 10k - 50k Sync Job Durations'
                }
              }
            }
          }
          '38': {
            position: {
              x: 4
              y: 42
              colSpan: 11
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '15bd1362-68dd-413e-a9fd-87c931d2c932'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\nResult = customDimensions["Result"],\nDryRun = customDimensions["IsDryRunEnabled"],\nType = customDimensions["Type"]\n| where Result == "Success" and DryRun == "False"\n| project Minutes, tostring(Type)\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100) by Type\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    Type: '115px'
                    percentile_Minutes_50: '173px'
                    percentile_Minutes_75: '170px'
                    percentile_Minutes_95: '169px'
                    percentile_Minutes_99: '170px'
                    percentile_Minutes_100: '178px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"],\n    Size = customDimensions["ProjectedMemberCount"]\n| where Result == "Success" and DryRun == "False" and Size < 100000 and Size >= 50000\n| project Minutes\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100)'
                  PartTitle: 'Size 50k - 100k Sync Job Durations'
                }
              }
            }
          }
          '39': {
            position: {
              x: 4
              y: 44
              colSpan: 11
              rowSpan: 2
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '15bd1362-68dd-413e-a9fd-87c931d2c932'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\nResult = customDimensions["Result"],\nDryRun = customDimensions["IsDryRunEnabled"],\nType = customDimensions["Type"]\n| where Result == "Success" and DryRun == "False"\n| project Minutes, tostring(Type)\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100) by Type\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    percentile_Minutes_75: '171px'
                    percentile_Minutes_95: '169px'
                    percentile_Minutes_99: '169px'
                    percentile_Minutes_100: '177px'
                    Type: '117px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project Minutes = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) / 60 * 1m,\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"],  \n    Size = customDimensions["ProjectedMemberCount"]\n| where Result == "Success" and DryRun == "False" and Size >= 100000\n| project Minutes\n| summarize percentiles(Minutes, 50, 75, 95, 99, 100)\n'
                  PartTitle: 'Size 100k+ Sync Job Durations'
                }
              }
            }
          }
          '40': {
            position: {
              x: 1
              y: 46
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '12cc1eac-14d0-40b2-ad53-080950912b2f'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customMetrics\n| where name == "ResourceUnitsUsed"\n| project timestamp, valueSum, operation_Name\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customMetrics\n| where name == "ResourceUnitsUsed"\n| extend customMetric_valueSum = iif(itemType == \'customMetric\', valueSum, todouble(\'\'))\n| summarize [\'customMetrics/ResourceUnitsUsed_sum\'] = sum(customMetric_valueSum) by bin(timestamp, 10s)\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'ResourceUnitsUsed'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'customMetrics/ResourceUnitsUsed_sum'
                        type: 'real'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
              partHeader: {
                title: 'ResourceUnitsUsed'
                subtitle: ''
              }
            }
          }
          '41': {
            position: {
              x: 7
              y: 46
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '83401f0d-35f2-4e13-b135-6b19fc882574'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'let start = now(-7d);\nrequests\n| where timestamp > start\n| project-rename Location=operation_Name, FunctionName=name, DurationInMilliseconds=duration\n| project timestamp, FunctionName, Location, DurationInMilliseconds\n| order by DurationInMilliseconds desc \n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customMetrics\n| where name == "WritesUsed"\n| extend customMetric_valueSum = iif(itemType == \'customMetric\', valueSum, todouble(\'\'))\n| summarize [\'customMetrics/WritesUsed_sum\'] = sum(customMetric_valueSum) by bin(timestamp, 150s)\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'WritesUsed'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'customMetrics/WritesUsed_sum'
                        type: 'real'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
              partHeader: {
                title: 'WritesUsed'
                subtitle: ''
              }
            }
          }
          '42': {
            position: {
              x: 13
              y: 46
              colSpan: 10
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '12cc1eac-14d0-40b2-ad53-080950912b2f'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customMetrics\n| where name == "ResourceUnitsUsed"\n| project timestamp, valueSum, operation_Name\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "ResourceUnitsUsedByType"\n| order by timestamp desc\n| project timestamp,\n    QueryType = tostring(customDimensions["QueryType"]),\n    ResourceUnitsUsed = toint(customDimensions["ResourceUnitsUsed"])\n| summarize sum(ResourceUnitsUsed) by bin(timestamp, 1m), QueryType\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'ResourceUnitsUsedByType'
                  Dimensions: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'sum_ResourceUnitsUsed'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'QueryType'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
              partHeader: {
                title: 'ResourceUnitsUsedByType'
                subtitle: ''
              }
            }
          }
          '43': {
            position: {
              x: 1
              y: 50
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourcegroups/${resourceGroup}/providers/microsoft.operationalinsights/workspaces/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '125c29c5-1323-47cc-a6e3-76ebfa532381'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'ApplicationLog_CL \n| where Message contains "OrchestratorFunction number of jobs" and location_s == "JobTrigger"\n| extend JobCount = toint(extract("number of jobs in the syncJobs List: (\\\\d+)", 1, Message))\n| project timestamp = TimeGenerated, JobCount\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'FrameControlChart'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  value: 'StackedColumn'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: 'gmm-data-prodv2'
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  value: {
                    xAxis: {
                      name: 'timestamp'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'JobCount'
                        type: 'int'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  value: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  PartTitle: 'Sync Jobs Triggered Per 5 Minutes'
                }
              }
            }
          }
          '44': {
            position: {
              x: 1
              y: 54
              colSpan: 9
              rowSpan: 2
            }
            metadata: {
              inputs: []
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              settings: {
                content: {
                  settings: {
                    content: '# <span style="color:red">Troubleshooting Dashboard</span>\r\n\r\n## <span style="color:red">Summary</span>\r\n\r\nUse this dashboard to view any potential issues that may be occurring in GMM now or within the past X number of days.'
                    title: ''
                    subtitle: ''
                    markdownSource: 1
                    markdownUri: null
                  }
                }
              }
            }
          }
          '45': {
            position: {
              x: 1
              y: 56
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    Destination = tostring(customDimensions["Destination"]),\n    Type = tostring(customDimensions["Type"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"])\n| where (Result == "PartialSuccess" or Result == "Failure") and DryRun == false\n| summarize by Destination, Result, Bin = bin(timestamp, 1d)\n| summarize count() by Bin, Result\n\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Jobs with PartialSuccess or Failure'
                  Dimensions: {
                    xAxis: {
                      name: 'Bin'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'count_'
                        type: 'long'
                      }
                    ]
                    splitBy: [
                      {
                        name: 'Result'
                        type: 'string'
                      }
                    ]
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '46': {
            position: {
              x: 7
              y: 56
              colSpan: 9
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourcegroups/${resourceGroup}/providers/microsoft.operationalinsights/workspaces/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1592d33b-1422-45a4-92b6-b23302415882'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    Message: '379px'
                  }
                  Query: 'ApplicationLog_CL\n| where ((location_s != "ProfileSync") and (Message has "exception" or Message has "error") and Message !has "Response" and Message !has "Regex Expression:") or (Message has "Setting job status to" and Message !has "Idle" and Message !has "InProgress" and Message !has "StuckInProgress")\n| distinct TimeGenerated, location_s, Message, RunId_g\n| order by TimeGenerated desc\n'
                  PartTitle: 'Jobs marked as Error'
                }
              }
            }
          }
          '47': {
            position: {
              x: 1
              y: 60
              colSpan: 8
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourcegroups/${resourceGroup}/providers/microsoft.operationalinsights/workspaces/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '64de05b8-0ff7-46ae-9a21-4efac2046dd5'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'ApplicationLog_CL \n|  project TimeGenerated, Message, location_s, RunId_g, TargetOfficeGroupId_g \n|  where location_s in ("JobTrigger", "GraphUpdater") and not(Message has_any("Email", "FilePath")) and Message has "RunId" \n|  order by RunId_g desc, TimeGenerated asc \n|  where location_s == "JobTrigger" and RunId_g == next(RunId_g) and next(location_s) <> "GraphUpdater" \n|  project TimeGenerated, TargetOfficeGroupId_g, RunId_g \n|  where TimeGenerated > ago(30d) and TimeGenerated < ago(1d) and TargetOfficeGroupId_g  != RunId_g \n|  order by TimeGenerated desc'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'ApplicationLog_CL \n|  project TimeGenerated, Message, RunId_g, tostring(Destination_s)\n|  where Message has \'setting status to ErroredDueToStuckInProgress\'\n|  where TimeGenerated > ago(30d) and TimeGenerated < ago(1d)\n|  extend DestinationJson = parse_json(Destination_s)\n|  project TimeGenerated,\n           ObjectId = tostring(DestinationJson[0]["value"]["objectId"]),\n           ChannelId = tostring(DestinationJson[0]["value"]["channelId"]),\n           RunId_g\n|  order by TimeGenerated desc\n'
                  PartTitle: 'Jobs potentially stuck InProgress'
                  IsQueryContainTimeRange: true
                }
              }
            }
          }
          '48': {
            position: {
              x: 9
              y: 60
              colSpan: 7
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourcegroups/${resourceGroup}/providers/microsoft.operationalinsights/workspaces/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '61e7acff-f2c8-48ab-b116-39bf0f60e2f3'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'PT30M'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'ApplicationLog_CL\n| project TimeGenerated, Message, location_s, RunId_g, TargetOfficeGroupId_g\n| where Message has "Threshold Exceeded"\n| distinct TimeGenerated, TargetOfficeGroupId_g, RunId_g, Message\n| order by TimeGenerated desc\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'ApplicationLog_CL\n| project TimeGenerated, Message, location_s, RunId_g, Destination_s\n| where Message has "Threshold Exceeded"\n| extend DestinationJson = parse_json(Destination_s)\n| project TimeGenerated, Message, location_s, RunId_g,\n            ObjectId = tostring(DestinationJson[0]["value"]["objectId"]),\n            ChannelId = tostring(DestinationJson[0]["value"]["channelId"])\n| distinct ObjectId, ChannelId, RunId_g, TimeGenerated\n| order by TimeGenerated desc\n'
                  PartTitle: 'Threshold Exceeded Jobs'
                  PartSubTitle: 'ApplicationLog_CL'
                }
              }
            }
          }
          '49': {
            position: {
              x: 1
              y: 64
              colSpan: 8
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '10b3c2a8-28c3-4b74-a637-e5305b696ec4'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P7D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customMetrics\n| where name == "SyncJobTimeElapsedSeconds"\n| project Seconds = todouble(value) * 1s\n| summarize Count = count() by DurationBin = bin(Seconds + 1m, 1m)\n| order by DurationBin desc\n| project DurationBin = tostring(DurationBin), Jobs = toint(Count)\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'FrameControlChart'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  value: 'StackedColumn'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  value: {
                    xAxis: {
                      name: 'DurationBin'
                      type: 'string'
                    }
                    yAxis: [
                      {
                        name: 'Jobs'
                        type: 'int'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  value: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    Destination: '265px'
                    RunId: '279px'
                    TimeElapsed: '133px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project TimeElapsed = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) * 1s,\n    Destination = tostring(customDimensions["Destination"]),\n    RunId = customDimensions["RunId"],\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"]\n| where Result == "Success" and DryRun == "False"\n| extend DestinationJson = parse_json(Destination)\n| project TimeElapsed,\n          ObjectId = tostring(DestinationJson[0]["value"]["objectId"]),\n          ChannelId = tostring(DestinationJson[0]["value"]["channelId"]), \n          RunId\n| order by TimeElapsed desc\n\n'
                  ControlType: 'AnalyticsGrid'
                  PartTitle: 'Sync Job Run Durations List'
                  PartSubTitle: 'Descending order'
                }
              }
            }
          }
          '50': {
            position: {
              x: 9
              y: 64
              colSpan: 8
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '83401f0d-35f2-4e13-b135-6b19fc882574'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'requests\n| project name, operation_Name, duration=duration / 1000 / 60\n| summarize max_Duration=max(duration) by name, operation_Name\n| order by max_Duration desc\n\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    timestamp: '207px'
                    FunctionName: '246px'
                    DurationInMilliseconds: '206px'
                    Function: '365px'
                    TimeGenerated: '237px'
                    Location: '205px'
                  }
                  Query: 'requests\n| project name, operation_Name, duration=duration / 1000 / 60\n| summarize max_Duration=max(duration), timeouts=countif(duration >= 10) by name, operation_Name\n| order by max_Duration desc\n\n'
                  PartTitle: 'Duration of Durable Functions'
                  PartSubTitle: 'In minutes'
                }
              }
            }
          }
          '51': {
            position: {
              x: 1
              y: 68
              colSpan: 16
              rowSpan: 5
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: 'customEvents\n| where name == "TrackNullColumns"\n'
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    timestamp: '216.998px'
                    Column: '245.993px'
                    CurrentNullColumns: '210.998px'
                    PreviousNullColumns: '200.998px'
                    Delta: '126px'
                    CurrentNullPercent: '190px'
                    PreviousNullPercent: '219.009px'
                  }
                  Query: 'customEvents\n| where name == "TrackNullColumns"\n| project timestamp,    \n    Column = tostring(customDimensions["Column"]),\n    CurrentNulls = toint(customDimensions["CurrentNull"]),\n    PreviousNulls = toint(customDimensions["PreviousNull"]),\n    CurrentRows = toint(customDimensions["CurrentRows"]),\n    PreviousRows = toint(customDimensions["PreviousRows"])\n| summarize arg_max(timestamp, *) by Column\n| extend Delta = CurrentNulls - PreviousNulls,\n         CurrentNullPercent = (toreal(CurrentNulls) / CurrentRows) * 100,\n         PreviousNullPercent = (toreal(PreviousNulls) / PreviousRows) * 100\n| project timestamp, Column, CurrentNulls, PreviousNulls, Delta, CurrentNullPercent, PreviousNullPercent\n| order by Delta desc\n\n'
                  PartTitle: 'Null Calculator'
                  Partsubtitle: resourceGroup
                }
              }
            }
          }
          '52': {
            position: {
              x: 1
              y: 73
              colSpan: 10
              rowSpan: 2
            }
            metadata: {
              inputs: []
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              settings: {
                content: {
                  content: '# <span style="color:MediumPurple">Notification Dashboard</span>\r\n\r\n## <span style="color:MediumPurple">Summary</span>\r\n\r\nUse this dashboard to view insights about the notification system and threshold violations.'
                  title: ''
                  subtitle: ''
                  markdownSource: 1
                  markdownUri: null
                }
              }
            }
          }
          '53': {
            position: {
              x: 1
              y: 75
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "NotificationSent"\n| summarize Count = count() by Date = bin(timestamp, 1d) \n| order by Date desc'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Notifications Sent Per Day'
                  Dimensions: {
                    xAxis: {
                      name: 'Date'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'Count'
                        type: 'long'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '54': {
            position: {
              x: 7
              y: 75
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "NotificationResponseReceived"\n| summarize Count = count() by Date = bin(timestamp, 1d) \n| order by Date desc'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Notifications Responses Received Per Day'
                  Dimensions: {
                    xAxis: {
                      name: 'Date'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'Count'
                        type: 'long'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
          '55': {
            position: {
              x: 1
              y: 79
              colSpan: 6
              rowSpan: 4
            }
            metadata: {
              inputs: [
                {
                  name: 'resourceTypeMode'
                  isOptional: true
                }
                {
                  name: 'ComponentId'
                  isOptional: true
                }
                {
                  name: 'Scope'
                  value: {
                    resourceIds: [
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/microsoft.insights/components/${resourceGroup}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '1c38a923-16a8-4a6b-8f25-8eb90e14df70'
                  isOptional: true
                }
                {
                  name: 'Version'
                  value: '2.0'
                  isOptional: true
                }
                {
                  name: 'TimeRange'
                  value: 'P1D'
                  isOptional: true
                }
                {
                  name: 'DashboardId'
                  isOptional: true
                }
                {
                  name: 'DraftRequestParameters'
                  isOptional: true
                }
                {
                  name: 'Query'
                  value: ''
                  isOptional: true
                }
                {
                  name: 'ControlType'
                  value: 'AnalyticsGrid'
                  isOptional: true
                }
                {
                  name: 'SpecificChart'
                  isOptional: true
                }
                {
                  name: 'PartTitle'
                  value: 'Analytics'
                  isOptional: true
                }
                {
                  name: 'PartSubTitle'
                  value: resourceGroup
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  value: false
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'customEvents\n| where name == "ThresholdViolation"\n| extend Day = format_datetime(timestamp, \'yyyy-MM-dd\')\n| summarize NotificationsCount = count() by Day\n| order by Day asc'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Threshold Violations Created'
                  Dimensions: {
                    xAxis: {
                      name: 'Day'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'NotificationsCount'
                        type: 'long'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  LegendOptions: {
                    isEnabled: true
                    position: 'Bottom'
                  }
                }
              }
            }
          }
        }
      }
    }
    metadata: {
      model: {
        timeRange: {
          value: {
            relative: {
              duration: 24
              timeUnit: 1
            }
          }
          type: 'MsPortalFx.Composition.Configuration.ValueTypes.TimeRange'
        }
        filterLocale: {
          value: 'en-us'
        }
        filters: {
          value: {
            MsPortalFx_TimeRange: {
              model: {
                format: 'local'
                granularity: 'auto'
                relative: '7d'
              }
              displayCache: {
                name: 'Local Time'
                value: 'Past 7 days'
              }
              filteredPartIds: [
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bad2'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bad4'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bad6'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600badc'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bade'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bae0'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bae4'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bae6'
                'StartboardPart-MonitorChartPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bae8'
                'StartboardPart-MonitorChartPart-a2bd3c8b-8f16-4ac8-b4f4-15310600baea'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600baec'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600baee'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600baf0'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600baf2'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600baf4'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600baf6'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600baf8'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb00'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb02'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb04'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb06'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb08'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb0c'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb10'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb12'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb16'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb18'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb1a'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb1c'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb1e'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb20'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb22'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb26'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb28'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb2a'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb2c'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb2e'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb30'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb32'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb36'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb38'
                'StartboardPart-LogsDashboardPart-a2bd3c8b-8f16-4ac8-b4f4-15310600bb3a'
              ]
            }
          }
        }
      }
    }
  }
}
