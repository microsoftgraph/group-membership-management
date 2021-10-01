@minLength(1)
param serviceBusName string

@minLength(1)
param topicName string

@metadata({
  description: 'Topic\'s subscriptions'
  sample: '\\[{\'name\': \'subscriptionOne\', \'ruleName\': \'ruleOne\', \'ruleSqlExpression\': \'Property = \\\'value\\\'\'}]\\]'
})
param topicSubscriptions array

resource serviceBusNameSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2017-04-01' = [for item in topicSubscriptions: {
  name: '${serviceBusName}/${topicName}/${item.name}'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT5M'
  }
}]

resource serviceBusNameSubscriptionRules 'Microsoft.ServiceBus/namespaces/topics/subscriptions/Rules@2017-04-01' = [for item in topicSubscriptions: {
  name: '${serviceBusName}/${topicName}/${item.name}/${item.ruleName}'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: item.ruleSqlExpression
      requiresPreprocessing: false
    }
    action: {}
  }
  dependsOn: [
    serviceBusNameSubscription
  ]
}]
