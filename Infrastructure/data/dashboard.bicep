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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"])\n| where Result == "Success" and DryRun == false\n| distinct TargetOfficeGroupId\n| summarize Count = count()\n\n'
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
              x: 13
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
                  Query: 'customMetrics \n| where name == "Endpoints"\n| project timestamp, Service=tostring(customDimensions["EndPointName"]), GroupId=tostring(customDimensions["GroupId"]), value\n| summarize Groups=dcount(GroupId) by Service\n| order by Groups\n\n'
                }
              }
              partHeader: {
                title: 'Services'
                subtitle: resourceGroup
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
              ]
              type: 'Extension/Microsoft_OperationsManagementSuite_Workspace/PartType/AnalyticsPart'
              deepLink: '#@microsoft.onmicrosoft.com/resource/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.OperationalInsights/workspaces/${resourceGroup}/logs'
            }
          }
          '6': {
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
                    Count: '81px'
                  }
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),\n    Type = tostring(customDimensions["Type"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"])\n| where Result == "Success" and DryRun == false\n| order by TargetOfficeGroupId, timestamp\n| where TargetOfficeGroupId != prev(TargetOfficeGroupId)\n| summarize Count = count() by Type\n'
                  ControlType: 'AnalyticsGrid'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Syncs By Type'
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
                title: 'Syncs By Type'
                subtitle: resourceGroup
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),\n    Type = tostring(customDimensions["Type"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"]),\n    Onboarding = tobool(customDimensions["IsInitialSync"])\n| where Result == "Success" and DryRun == false and Onboarding == true\n| summarize count() by bin(timestamp, 1d), Type\n'
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
                  value: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
          '9': {
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),\n    Type = tostring(customDimensions["Type"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"])\n| where Result == "Success" and DryRun == false\n| summarize by TargetOfficeGroupId, Type, Bin = bin(timestamp, 1d)\n| summarize count() by Bin, Type\n\n'
                  ControlType: 'FrameControlChart'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'Sync Jobs Successful'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                            id: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),\n    Result = tostring(customDimensions["Result"]),\n    DryRun = tobool(customDimensions["IsDryRunEnabled"]),\n    ToAdd = toint(customDimensions["MembersAdded"]),\n    ToRemove = toint(customDimensions["MembersRemoved"])\n| where Result == "Success" and DryRun == false\n| summarize UsersAdded = sum(ToAdd), UsersRemoved = sum(ToRemove) by bin(timestamp, 1d)\n\n'
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project MembersAdded = toint(customDimensions["MembersAdded"]),\n    MembersRemoved = toint(customDimensions["MembersRemoved"]),\n    Destination = toguid(customDimensions["TargetOfficeGroupId"]),\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"],\n    RunId = toguid(customDimensions["RunId"])\n\n| where Result == "Success" and DryRun == "False"\n| distinct MembersRemoved, Destination, RunId\n| order by MembersRemoved desc\n\n'
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
              x: 13
              y: 10
              colSpan: 4
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project MembersAdded = toint(customDimensions["MembersAdded"]),\n    MembersRemoved = toint(customDimensions["MembersRemoved"]),\n    Destination = toguid(customDimensions["TargetOfficeGroupId"]),\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"],\n    RunId = toguid(customDimensions["RunId"])\n| where Result == "Success" and DryRun == "False" and MembersAdded == 0 and MembersRemoved == 0\n| distinct RunId\n| summarize Count = count()'
                  ControlType: 'AnalyticsGrid'
                }
              }
              partHeader: {
                title: 'No-Op Syncs'
                subtitle: ''
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project MembersAdded = toint(customDimensions["MembersAdded"]),\n    MembersRemoved = toint(customDimensions["MembersRemoved"]),\n    Destination = toguid(customDimensions["TargetOfficeGroupId"]),\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"],\n    RunId = toguid(customDimensions["RunId"])\n| where Result == "Success" and DryRun == "False"\n| distinct MembersAdded, Destination, RunId\n| order by MembersAdded desc\n\n'
                  ControlType: 'AnalyticsGrid'
                }
              }
              partHeader: {
                title: 'Members Added to Destination'
                subtitle: 'Descending order'
              }
            }
          }
          '16': {
            position: {
              x: 1
              y: 14
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
          '17': {
            position: {
              x: 7
              y: 14
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
          '18': {
            position: {
              x: 13
              y: 14
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
          '19': {
            position: {
              x: 1
              y: 18
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),    \n    Result = tostring(customDimensions["Result"]),\n    MemberCount = toint(customDimensions["ProjectedMemberCount"])\n| where Result == "Success"\n| summarize MaxMemberCount = max(MemberCount) by TargetOfficeGroupId\n| summarize All = countif(MaxMemberCount >= 0), MoreThan1k = countif(MaxMemberCount >= 1000), MoreThan5k = countif(MaxMemberCount >= 5000), MoreThan10k = countif(MaxMemberCount >= 10000), MoreThan25k = countif(MaxMemberCount >= 25000), MoreThan50k = countif(MaxMemberCount >= 50000), MoreThan75k = countif(MaxMemberCount >= 75000), MoreThan100k = countif(MaxMemberCount >= 100000), MoreThan200k = countif(MaxMemberCount >= 200000), MoreThan300k = countif(MaxMemberCount >= 300000), MoreThan400k = countif(MaxMemberCount >= 400000)\n'
                  PartTitle: 'Group Counts based on Size Buckets'
                  PartSubTitle: resourceGroup
                }
              }
            }
          }
          '20': {
            position: {
              x: 1
              y: 20
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| order by timestamp desc\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["TargetOfficeGroupId"]),    \n    Result = tostring(customDimensions["Result"]),\n    MemberCount = toint(customDimensions["ProjectedMemberCount"])\n| where Result == "Success"\n| summarize MaxMemberCount = max(MemberCount) by TargetOfficeGroupId\n| summarize LessThan1k = countif(MaxMemberCount < 1000), From1kTo5k = countif(MaxMemberCount >= 1000 and MaxMemberCount < 5000), From5kTo10k = countif(MaxMemberCount >= 5000 and MaxMemberCount < 10000), From10kTo25k = countif(MaxMemberCount >= 10000 and MaxMemberCount < 25000), From25kTo50k = countif(MaxMemberCount >= 25000 and MaxMemberCount < 50000), From50kTo75k = countif(MaxMemberCount >= 50000 and MaxMemberCount < 75000), From75kTo100k = countif(MaxMemberCount >= 75000 and MaxMemberCount < 100000), From100kTo200k = countif(MaxMemberCount >= 100000 and MaxMemberCount < 200000), From200kTo300k = countif(MaxMemberCount >= 200000 and MaxMemberCount < 300000), From300kTo400k = countif(MaxMemberCount >= 300000 and MaxMemberCount < 400000), MoreThan400k = countif(MaxMemberCount >= 400000)\n'
                  PartTitle: 'Group Counts based on Size Buckets'
                  PartSubTitle: resourceGroup
                }
              }
            }
          }
          '21': {
            position: {
              x: 1
              y: 22
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
                  value: 'customEvents\n| where name == "NestedGroupCount"\n'
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
                  Query: 'customEvents\n| where name == "NestedGroupCount"\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["DestinationGroupObjectId"]),\n    SourceGroupObjectId = tostring(customDimensions["SourceGroupObjectId"]),\n    NestedGroupCount = toint(customDimensions["NestedGroupCount"])    \n| distinct TargetOfficeGroupId, SourceGroupObjectId, NestedGroupCount\n| order by NestedGroupCount desc'
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
          '22': {
            position: {
              x: 8
              y: 22
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
                  value: 'customEvents\n| where name == "UsersInCacheCount"\n'
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
          '23': {
            position: {
              x: 15
              y: 22
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
                  value: 'customEvents\n| where name == "ExclusionarySourcePartsCount"\n'
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
                    TotalNumberOfSourceParts: '295px'
                    NumberOfExclusionarySourceParts: '167px'
                  }
                  Query: 'customEvents\n| where name == "ExclusionarySourcePartsCount"\n| project timestamp,\n    TargetOfficeGroupId = tostring(customDimensions["DestinationGroupObjectId"]),\n    TotalNumberOfSourceParts = toint(customDimensions["TotalNumberOfSourceParts"]),\n    NumberOfExclusionarySourceParts = toint(customDimensions["NumberOfExclusionarySourceParts"])    \n| distinct TargetOfficeGroupId, TotalNumberOfSourceParts, NumberOfExclusionarySourceParts\n| order by TotalNumberOfSourceParts desc'
                  ControlType: 'AnalyticsGrid'
                  SpecificChart: 'StackedColumn'
                  PartTitle: 'ExclusionarySourcePartsCount'
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
                title: 'ExclusionarySourcePartsCount'
                subtitle: resourceGroup
              }
            }
          }
          '24': {
            position: {
              x: 1
              y: 26
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
          '25': {
            position: {
              x: 1
              y: 28
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
                  PartSubTitle: resourceGroup
                }
              }
            }
          }
          '26': {
            position: {
              x: 1
              y: 30
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
          '27': {
            position: {
              x: 4
              y: 30
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
          '28': {
            position: {
              x: 4
              y: 32
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
          '29': {
            position: {
              x: 1
              y: 34
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
          '30': {
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
          '31': {
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
          '32': {
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
          '33': {
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
          '34': {
            position: {
              x: 1
              y: 42
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
          '35': {
            position: {
              x: 7
              y: 42
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
          '36': {
            position: {
              x: 1
              y: 47
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
          '37': {
            position: {
              x: 1
              y: 49
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
                  value: 'ApplicationLog_CL\n| where (location_s == "GraphUpdater" and (Message has "exception" or Message has "error") and Message !has "Response" and Message !has "Regex Expression:") or (Message has "Setting job status to" and Message !has "Idle" and Message !has "InProgress")\n| project TimeGenerated, Message, RowKey_g, TargetOfficeGroupId_g, RunId_g\n| order by TimeGenerated desc\n\n'
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
                  PartTitle: 'Jobs marked as Error'
                }
              }
            }
          }
          '38': {
            position: {
              x: 6
              y: 49
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
                  value: 'ApplicationLog_CL\n| project TimeGenerated, Message, location_s\n| where location_s in ("JobTrigger", "GraphUpdater") and not(Message has_any("Email", "FilePath")) and Message has "RunId"\n| extend RunId = tostring(split(split(Message, " ")[6], "\n")[0])\n| extend TargetOfficeGroupId = tostring(split(split(Message, " ")[6], "\n")[0])\n| order by RunId desc, TimeGenerated asc\n| where location_s == "JobTrigger" and RunId == next(RunId) and next(location_s) <> "GraphUpdater"\n| project TimeGenerated, TargetOfficeGroupId, RunId\n| where TimeGenerated > ago(30d) and TimeGenerated < ago(1d) and TargetOfficeGroupId  != RunId\n| order by TimeGenerated desc'
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
                  PartTitle: 'Jobs stuck as InProgress'
                }
              }
            }
          }
          '39': {
            position: {
              x: 11
              y: 49
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
                  Query: 'ApplicationLog_CL\n| project TimeGenerated, Message, location_s, RunId_g, TargetOfficeGroupId_g\n| where Message has "Threshold Exceeded"\n| distinct TargetOfficeGroupId_g, RunId_g, TimeGenerated\n| order by TimeGenerated desc\n\n'
                  PartTitle: 'Threshold Exceeded Jobs'
                  PartSubTitle: 'ApplicationLog_CL'
                }
              }
            }
          }
          '40': {
            position: {
              x: 1
              y: 53
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
                      '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.Insights/components/${resourceGroup}'
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
                  Query: 'customEvents\n| where name == "SyncComplete"\n| project TimeElapsed = todouble(customDimensions["SyncJobTimeElapsedSeconds"]) * 1s,\n    Destination = customDimensions["TargetOfficeGroupId"],\n    RunId = customDimensions["RunId"],\n    Result = customDimensions["Result"],\n    DryRun = customDimensions["IsDryRunEnabled"]\n| where Result == "Success" and DryRun == "False"\n| project TimeElapsed, Destination, RunId\n| order by TimeElapsed desc\n\n'
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
          '41': {
            position: {
              x: 9
              y: 53
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
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae1681fc'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae1681fe'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168204'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168206'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae16820a'
                'StartboardPart-MonitorChartPart-9939d273-1847-4ad5-b2d0-e19dae16820c'
                'StartboardPart-MonitorChartPart-9939d273-1847-4ad5-b2d0-e19dae16820e'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168210'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168212'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae16821a'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae16821c'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae16821e'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168220'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168222'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168226'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae16822a'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae16822c'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168230'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168232'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168234'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168236'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168238'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae16823a'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae16823e'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168240'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168242'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168244'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae168246'
                'StartboardPart-LogsDashboardPart-9939d273-1847-4ad5-b2d0-e19dae1683fd'
              ]
            }
          }
        }
      }
    }
  }
}
