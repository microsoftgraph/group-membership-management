@description('Resource ID of the Service Bus namespace.')
param serviceBusNamespaceId string

@description('Name of the Service Bus queue.')
param serviceBusQueueName string

@description('Location for the alert. Must be the same location as the namespace.')
param location string

@description('The ID of the action group that is triggered when the alert is activated.')
param actionGroupId string = ''

@description('The threshold value that triggers the alert.')
param threshold int

resource serviceBusQueueAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${serviceBusQueueName}-Alert'
  location: 'global'
  properties: {
    severity: 3 
    enabled: true 
    scopes: [
      serviceBusNamespaceId 
    ]
    evaluationFrequency: 'PT1M' 
    windowSize: 'PT1M' 
    targetResourceType: 'Microsoft.ServiceBus/namespaces' 
    targetResourceRegion: location 
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria' 
      allOf: [
        {
          threshold: threshold
          metricName: 'ActiveMessages' 
          metricNamespace: 'Microsoft.ServiceBus/namespaces' 
          operator: 'GreaterThan'
          timeAggregation: 'Average'
          dimensions: [
            {
              name: 'EntityName' 
              operator: 'Include' 
              values: [
                serviceBusQueueName 
              ]
            }
          ]
          failingPeriods: {
            minFailingPeriodsToAlert: 1 
            numberOfEvaluationPeriods: 1 
          }
          criterionType:'StaticThresholdCriterion'
          name:'${serviceBusQueueName}-Alert'
        }
      ]
    }
  }
}
