@description('Resource ID of the Log Analytics workspace.')
param sourceId string = ''

@description('Location for the alert. Must be the same location as the workspace.')
param location string = ''

@description('The ID of the action group that is triggered when the alert is activated.')
param actionGroupId string = ''

resource scheduledQueryRules 'Microsoft.Insights/scheduledQueryRules@2018-04-16' = {
  name: 'PII log query alert'
  location: location
  properties: {
    description: 'PII log query alert'
    enabled: 'true'
    source: {
      query: 'PIIApplicationLog_CL | summarize count()'
      dataSourceId: sourceId
      queryType: 'ResultCount'
    }
    schedule: {
      frequencyInMinutes: 15
      timeWindowInMinutes: 15
    }
    action: {
      'odata.type': 'Microsoft.WindowsAzure.Management.Monitoring.Alerts.Models.Microsoft.AppInsights.Nexus.DataContracts.Resources.ScheduledQueryRules.AlertingAction'
      severity: '4'
      aznsAction: {
        actionGroup: array(actionGroupId)
        emailSubject: 'PII Logs Alert Email'
      }
      trigger: {
        thresholdOperator: 'GreaterThan'
        threshold: 1
      }
    }
  }
}
