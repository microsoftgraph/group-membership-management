@description('Name of the dashboard')
param name string
param location string
param subscriptionId string
param jobsStorageAccountName string

resource name_resource 'Microsoft.Portal/dashboards@2015-08-01-preview' = {
  name: name
  location: location
  tags: {
    'hidden-title': 'GMM Dashboard (Metrics and Logs)'
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
              colSpan: 5
              rowSpan: 2
            }
            metadata: {
              inputs: []
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              settings: {
                content: {
                  settings: {
                    content: '# GMM Dashboard\n## Summary\n\n Use this dashboard to view live metrics, analyze usage of GMM, and look into potential issues with GMM'
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/microsoft.insights/components/${name}'
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
                  value: '${name}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),\n    Type = tostring(customDimensions["Type"]),\n    Result = tostring(customDimensions["Result"])\n| where Result == "Success"\n| distinct TargetOfficeGroupId\n| summarize Count = count()\n\n'
                  ControlType: 'AnalyticsGrid'
                  SpecificChart: 'StackedColumn'
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
              filters: {
                MsPortalFx_TimeRange: {
                  model: {
                    format: 'local'
                    granularity: 'auto'
                    relative: '3d'
                  }
                }
              }
              partHeader: {
                title: 'Sync Count'
                subtitle: 'Past 3 days'
              }
            }
          }
          '3': {
            position: {
              x: 10
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/microsoft.insights/components/${name}'
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
                  value: '${name}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),\n    Type = tostring(customDimensions["Type"]),\n    Result = tostring(customDimensions["Result"])\n| where Result == "Success" and TargetOfficeGroupId <> ""\n| distinct TargetOfficeGroupId, Type\n| summarize Count = count() by Type\n\n'
                  ControlType: 'AnalyticsGrid'
                  SpecificChart: 'StackedColumn'
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
              filters: {
                MsPortalFx_TimeRange: {
                  model: {
                    format: 'local'
                    granularity: 'auto'
                    relative: '3d'
                  }
                }
              }
              partHeader: {
                title: 'Syncs By Type'
                subtitle: 'Past 3 days'
              }
            }
          }
          '4': {
            position: {
              x: 0
              y: 1
              colSpan: 1
              rowSpan: 1
            }
            metadata: {
              inputs: []
              type: 'Extension/Microsoft_Azure_Storage/PartType/StorageExplorerPart'
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Storage/storageAccounts/${jobsStorageAccountName}/storageexplorer'
            }
          }
          '5': {
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
                        resourceId: '/subscriptions/${subscriptionId}/resourcegroups/${name}/providers/microsoft.operationalinsights/workspaces/${name}'
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
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/AnalyticsPart'
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.OperationalInsights/workspaces/${name}/logs'
            }
          }
          '6': {
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/microsoft.insights/components/${name}'
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
                  value: '${name}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),\n    Type = tostring(customDimensions["Type"]),\n    Result = tostring(customDimensions["Result"])\n| where Result == "Success"\n| summarize by TargetOfficeGroupId, Type, bin(timestamp, 1d)\n| summarize count() by bin(timestamp, 1d), Type\n\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
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
                title: 'Sync Jobs Successful'
                subtitle: ''
              }
            }
          }
          '7': {
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
                      '/subscriptions/${subscriptionId}/resourcegroups/${name}/providers/microsoft.operationalinsights/workspaces/${name}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '5f2cb71f-cb4f-4777-9110-e429380bb45f'
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
                  value: 'ApplicationLog_CL\n| distinct targetOfficeGroupId_g\n| where targetOfficeGroupId_g <> ""\n| summarize count()\n'
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
                  value: '${name}'
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
                  Query: 'ApplicationLog_CL\n| project TimeGenerated, Message, location_s, runId_g\n| where Message has "RunId : " and location_s == "JobTrigger"\n| extend LastRunTime = tostring(split(Message, \' \')[12]),\n    TargetOfficeGroupId = tostring(split(Message, \' \')[8]),\n    Type = tostring(split(split(Message, \' \')[6], \'\\n\')[0])\n| where LastRunTime has "1/1/1601"\n| order by TimeGenerated desc\n| summarize by TargetOfficeGroupId, Type, bin(TimeGenerated, 1d)\n| summarize count() by bin(TimeGenerated, 1d), Type\n\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Sync Count'
                  Dimensions: {
                    xAxis: {
                      name: 'TimeGenerated'
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
              filters: {
                MsPortalFx_TimeRange: {
                  model: {
                    format: 'local'
                    granularity: 'auto'
                    relative: '30d'
                  }
                }
              }
              partHeader: {
                title: 'Onboardings Per Day'
                subtitle: 'Past 30 days'
              }
            }
          }
          '8': {
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
                  value: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
                }
              ]
              type: 'Extension/AppInsightsExtension/PartType/CuratedBladeFailuresPinnedPart'
              isAdapter: true
              asset: {
                idInputName: 'ResourceId'
                type: 'ApplicationInsights'
              }
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}/failures'
            }
          }
          '9': {
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
                      '/subscriptions/${subscriptionId}/resourcegroups/${name}/providers/microsoft.operationalinsights/workspaces/${name}'
                    ]
                  }
                  isOptional: true
                }
                {
                  name: 'Dimensions'
                  value: {
                    xAxis: {
                      name: 'TimeGenerated'
                      type: 'datetime'
                    }
                    yAxis: [
                      {
                        name: 'sum_usersAdded'
                        type: 'long'
                      }
                      {
                        name: 'sum_usersRemoved'
                        type: 'long'
                      }
                    ]
                    splitBy: []
                    aggregation: 'Sum'
                  }
                  isOptional: true
                }
                {
                  name: 'PartId'
                  value: '20fae0fd-503b-45b3-9a11-bb6f11a9cf35'
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
                  value: 'ApplicationLog_CL\r\n| where Message contains (\'. Adding\')\r\n| extend MessageWords = array_reverse(split(Message, \' \'))\r\n| project usersAdded = toint(MessageWords[4]), usersRemoved = toint(split(MessageWords[0], \'.\')[0]), TimeGenerated\r\n| summarize sum(usersAdded), sum(usersRemoved) by bin(TimeGenerated, 1d)\r\n\n'
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
                  value: '${name}'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  Query: 'ApplicationLog_CL\n| where Message contains (\'. Adding\')\n| extend MessageWords = array_reverse(split(Message, \' \'))\n| project usersAdded = toint(MessageWords[4]), usersRemoved = toint(split(MessageWords[0], \'.\')[0]), TimeGenerated\n| summarize UsersAdded = sum(usersAdded), UsersRemoved = sum(usersRemoved) by bin(TimeGenerated, 1d)\n\n'
                  PartTitle: 'MS Graph API'
                  PartSubTitle: 'adds/remove calls per day'
                  Dimensions: {
                    xAxis: {
                      name: 'TimeGenerated'
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
              partHeader: {
                title: 'MS Graph API Calls for Add / Remove'
                subtitle: ''
              }
            }
          }
          '10': {
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
          '11': {
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
          '12': {
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                  value: '${name}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project MembersAdded = toint(customDimensions["MembersToAdd"]),\n    MembersRemoved = toint(customDimensions["MembersToRemove"]),\n    Destination = customDimensions["TargetOfficeGroupId"],\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"]\n| where Result == "Success" and DryRun == "False"\n| project MembersAdded, Destination\n| order by MembersAdded desc\n\n'
                  ControlType: 'AnalyticsGrid'
                }
              }
              partHeader: {
                title: 'Members Added to Destination'
                subtitle: 'Descending order'
              }
            }
          }
          '13': {
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                  value: '${name}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project MembersAdded = toint(customDimensions["MembersToAdd"]),\n    MembersRemoved = toint(customDimensions["MembersToRemove"]),\n    Destination = customDimensions["TargetOfficeGroupId"],\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"]\n| where Result == "Success" and DryRun == "False"\n| project MembersRemoved, Destination\n| order by MembersRemoved desc\n\n'
                  ControlType: 'AnalyticsGrid'
                }
              }
              partHeader: {
                title: 'Members Removed from Destination'
                subtitle: 'Descending order'
              }
            }
          }
          '14': {
            position: {
              x: 1
              y: 14
              colSpan: 5
              rowSpan: 2
            }
            metadata: {
              inputs: []
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              settings: {
                content: {
                  settings: {
                    content: '# TroubleShooting Dashboard\r\n\r\n## Summary\r\n\r\nUse this dashboard to view any potential issueas that may be occurring in GMM now or within the past 30 days.'
                    title: ''
                    subtitle: ''
                    markdownSource: 1
                    markdownUri: null
                  }
                }
              }
            }
          }
          '15': {
            position: {
              x: 1
              y: 16
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
                      '/subscriptions/${subscriptionId}/resourcegroups/${name}/providers/microsoft.operationalinsights/workspaces/${name}'
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
                  value: '122f4d44-1313-4d7c-80f2-322d8d47c9d1'
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
                  value: 'ApplicationLog_CL\r\n| where Message has \'. Adding\'\r\n| extend MessageWords = array_reverse(split(Message, \' \'))\r\n| project usersAdded = toint(MessageWords[4]), TimeGenerated, targetOfficeGroupId_g\r\n| top 5 by usersAdded desc\r\n\n'
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
                  value: '${name}'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    targetOfficeGroupId_g: '276px'
                    usersAdded: '127px'
                    DestinationGroupObjectId: '260px'
                  }
                  Query: 'ApplicationLog_CL\n| project TimeGenerated, Message, location_s, TargetOfficeGroupId_g, RunId_g\n| where location_s == "GraphUpdater" and (Message has "exception" or Message has "error") and not (Message has "Response")\n| project TimeGenerated, Message, TargetOfficeGroupId_g, RunId_g\n| order by TimeGenerated desc\n\n'
                  PartTitle: 'Users Added'
                  PartSubTitle: 'by Group'
                }
              }
              partHeader: {
                title: 'Jobs marked as Error'
                subtitle: ''
              }
            }
          }
          '16': {
            position: {
              x: 6
              y: 16
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
                      '/subscriptions/${subscriptionId}/resourcegroups/${name}/providers/microsoft.operationalinsights/workspaces/${name}'
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
                  value: '122f4d44-1313-4d7c-80f2-322d8d47c9d1'
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
                  value: 'ApplicationLog_CL\r\n| where Message has \'. Adding\'\r\n| extend MessageWords = array_reverse(split(Message, \' \'))\r\n| project usersAdded = toint(MessageWords[4]), TimeGenerated, targetOfficeGroupId_g\r\n| top 5 by usersAdded desc\r\n\n'
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
                  value: '${name}'
                  isOptional: true
                }
                {
                  name: 'LegendOptions'
                  isOptional: true
                }
                {
                  name: 'IsQueryContainTimeRange'
                  isOptional: true
                }
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/LogsDashboardPart'
              settings: {
                content: {
                  GridColumnsWidth: {
                    targetOfficeGroupId_g: '276px'
                    usersAdded: '127px'
                    DestinationGroupObjectId: '260px'
                  }
                  Query: 'ApplicationLog_CL\n| where TimeGenerated >= now(-30d)\n| project TimeGenerated, Message, location_s\n| where (location_s == "JobTrigger" and Message has "RunId") or (location_s == "GraphUpdater" and Message has "RunId")\n| extend RunId = tostring(split(Message, \' \')[2])\n| order by RunId desc, TimeGenerated asc\n| where location_s == "JobTrigger" and RunId == next(RunId) and next(location_s) <> "GraphUpdater"\n| project TimeGenerated,\n    TargetOfficeGroupId = tostring(split(Message, \' \')[8]),\n    RunId = split(RunId, \'\\n\')[0],\n    Type = split(split(Message, \' \')[6], \'\\n\')[0]\n| where TimeGenerated <= iff(Type == "SecurityGroup", now(-6h), now(-24h))\n| order by TimeGenerated desc\n\n'
                  PartTitle: 'Jobs stuck in progress'
                  PartSubTitle: 'by Group'
                  IsQueryContainTimeRange: true
                }
              }
              partHeader: {
                title: 'Jobs stuck as InProgress'
                subtitle: 'Past month'
              }
            }
          }
          '17': {
            position: {
              x: 11
              y: 16
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
                      '/subscriptions/${subscriptionId}/resourcegroups/${name}/providers/microsoft.operationalinsights/workspaces/${name}'
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
                  value: 'ApplicationLog_CL\n| project TimeGenerated, Message, location_s, runId_g, TargetOfficeGroupId_g\n| where Message has "Threshold Exceeded"\n| distinct TimeGenerated, TargetOfficeGroupId_g, runId_g, Message\n| order by TimeGenerated desc\n'
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
                  value: '${name}'
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
                  Query: 'ApplicationLog_CL\n| project TimeGenerated, Message, location_s, runId_g, TargetOfficeGroupId_g\n| where Message has "Threshold Exceeded"\n| distinct TargetOfficeGroupId_g, runId_g, TimeGenerated\n| order by TimeGenerated desc\n\n'
                }
              }
              partHeader: {
                title: 'Threshold Exceeded Jobs'
                subtitle: ''
              }
            }
          }
          '18': {
            position: {
              x: 1
              y: 20
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                  value: '${name}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project Seconds = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) * 1s,\n    Destination = customDimensions["TargetOfficeGroupId"],\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"]\n| where Result == "Success" and DryRun == "False"\n| project Seconds, Destination\n| order by Seconds desc\n| summarize Count = count() by DurationBin = bin(Seconds + 1m, 1m)\n| order by DurationBin desc\n| project DurationBin = tostring(DurationBin), Jobs = toint(Count)\n'
                }
              }
              partHeader: {
                title: 'Sync Job Run Durations Chart'
                subtitle: 'Rounded up to nearest minute'
              }
            }
          }
          '19': {
            position: {
              x: 7
              y: 20
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.Insights/components/${name}'
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
                  value: '${name}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project TimeElapsed = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) * 1s,\n    Destination = customDimensions["TargetOfficeGroupId"],\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"]\n| where Result == "Success" and DryRun == "False"\n| project TimeElapsed, Destination\n| order by TimeElapsed desc\n\n'
                  ControlType: 'AnalyticsGrid'
                  PartTitle: 'Sync Job Run Durations List'
                  PartSubTitle: 'Descending order'
                }
              }
              partHeader: {
                title: 'Sync Job Run Durations List'
                subtitle: 'Descending order'
              }
            }
          }
          '20': {
            position: {
              x: 1
              y: 24
              colSpan: 6
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.ServiceBus/namespaces/${name}'
                          }
                          name: 'IncomingMessages'
                          aggregationType: 1
                          namespace: 'microsoft.servicebus/namespaces'
                          metricVisualization: {
                            displayName: 'Incoming Messages'
                            resourceDisplayName: '${name}'
                          }
                        }
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.ServiceBus/namespaces/${name}'
                          }
                          name: 'OutgoingMessages'
                          aggregationType: 1
                          namespace: 'microsoft.servicebus/namespaces'
                          metricVisualization: {
                            displayName: 'Outgoing Messages'
                            resourceDisplayName: '${name}'
                          }
                        }
                      ]
                      title: 'Incoming and Outgoing Messages in membership queue'
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
                      filterCollection: {
                        filters: [
                          {
                            key: 'EntityName'
                            operator: 0
                            values: [
                              'membership'
                            ]
                          }
                        ]
                      }
                      timespan: {
                        relative: {
                          duration: 2592000000
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.ServiceBus/namespaces/${name}'
                          }
                          name: 'IncomingMessages'
                          aggregationType: 1
                          namespace: 'microsoft.servicebus/namespaces'
                          metricVisualization: {
                            displayName: 'Incoming Messages'
                            resourceDisplayName: '${name}'
                          }
                        }
                        {
                          resourceMetadata: {
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/Microsoft.ServiceBus/namespaces/${name}'
                          }
                          name: 'OutgoingMessages'
                          aggregationType: 1
                          namespace: 'microsoft.servicebus/namespaces'
                          metricVisualization: {
                            displayName: 'Outgoing Messages'
                            resourceDisplayName: '${name}'
                          }
                        }
                      ]
                      title: 'Incoming and Outgoing Messages in membership queue'
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
              filters: {
                EntityName: {
                  model: {
                    operator: 'equals'
                    values: [
                      'membership'
                    ]
                  }
                }
              }
            }
          }
          '21': {
            position: {
              x: 7
              y: 24
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/microsoft.insights/components/${name}'
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
                  value: '${name}'
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
          '22': {
            position: {
              x: 1
              y: 28
              colSpan: 11
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/microsoft.insights/components/${name}'
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
                  value: '${name}'
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
                  value: true
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
                  Query: 'requests\n| project name, operation_Name, duration=duration / 1000 / 60\n| summarize max_Duration=max(duration) by name, operation_Name\n| order by max_Duration desc\n\n'
                  PartTitle: 'Duration of Durable Functions'
                  PartSubTitle: 'In minutes'
                }
              }
              partHeader: {
                title: 'Duration of Durable Functions'
                subtitle: 'In minutes'
              }
            }
          }
          '23': {
            position: {
              x: 1
              y: 33
              colSpan: 13
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/microsoft.insights/components/${name}'
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
                  value: '${name}'
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
                    percentile_Minutes_50: '196px'
                    percentile_Minutes_75: '186px'
                    percentile_Minutes_95: '175px'
                    percentile_Minutes_99: '169px'
                    percentile_Minutes_100: '191px'
                  }
                  PartTitle: 'Sync Job Run Durations Chart'
                  PartSubTitle: 'Rounded up to nearest minute'
                }
              }
              partHeader: {
                title: 'Sync Job Run Durations Chart'
                subtitle: 'Rounded up to nearest minute'
              }
            }
          }
          '24': {
            position: {
              x: 1
              y: 35
              colSpan: 13
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${name}/providers/microsoft.insights/components/${name}'
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
                  value: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\nTargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),\nType = tostring(customDimensions["Type"]),\nResult = tostring(customDimensions["Result"]),\nMemberCount = toint(customDimensions["ProjectedMemberCount"])\n| where Result == "Success"\n| summarize MaxMemberCount = max(MemberCount) by TargetOfficeGroupId, Type\n| summarize Groups10kPlus = countif(MaxMemberCount > 10000), Groups25kPlus = countif(MaxMemberCount > 25000), Groups50kPlus = countif(MaxMemberCount > 50000), Groups75kPlus = countif(MaxMemberCount > 75000), Groups100kPlus = countif(MaxMemberCount > 100000) by Type\n'
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
                  value: '${name}'
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
                  PartTitle: 'Groups Above 10k and 50k Members'
                  PartSubTitle: 'Past 3 days'
                }
              }
              partHeader: {
                title: 'Groups Above 10k and 50k Members'
                subtitle: 'Past 3 days'
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
                relative: '30d'
              }
              displayCache: {
                name: 'Local Time'
                value: 'Past 30 days'
              }
              filteredPartIds: [
                'StartboardPart-LogsDashboardPart-27700b87-9bff-4f04-85e0-fe0e914e9331'
                'StartboardPart-LogsDashboardPart-27700b87-9bff-4f04-85e0-fe0e914e9408'
                'StartboardPart-LogsDashboardPart-f4f7fc2f-690d-4e74-93da-f77f636c5175'
                'StartboardPart-LogsDashboardPart-3704e864-0d3e-4e24-9554-8b2978bfa107'
                'StartboardPart-LogsDashboardPart-27700b87-9bff-4f04-85e0-fe0e914e9490'
                'StartboardPart-LogsDashboardPart-5d0c76de-9567-4c4b-91e0-f3211abd8081'
                'StartboardPart-MonitorChartPart-a8d2e14f-372f-48c0-a9ce-b1045f6510e7'
                'StartboardPart-MonitorChartPart-a8d2e14f-372f-48c0-a9ce-b1045f6510d1'
                'StartboardPart-LogsDashboardPart-c48c2a4d-1a73-4da6-a3d6-c210ff8fe19d'
                'StartboardPart-LogsDashboardPart-c48c2a4d-1a73-4da6-a3d6-c210ff8fe0a2'
                'StartboardPart-LogsDashboardPart-f4f7fc2f-690d-4e74-93da-f77f636c5358'
                'StartboardPart-LogsDashboardPart-f4f7fc2f-690d-4e74-93da-f77f636c542d'
                'StartboardPart-LogsDashboardPart-78dc0aa9-fb91-4a57-b6e8-62c81e8010c5'
                'StartboardPart-LogsDashboardPart-a8d2e14f-372f-48c0-a9ce-b1045f65110f'
                'StartboardPart-LogsDashboardPart-877b955b-5666-49c2-8b6b-c67c978c514f'
                'StartboardPart-MonitorChartPart-6246e4d3-5742-42f4-87e0-c6f2666a9044'
              ]
            }
          }
        }
      }
    }
  }
}
