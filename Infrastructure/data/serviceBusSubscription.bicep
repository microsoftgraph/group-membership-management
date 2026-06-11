type topicSubscription = {
  topicName: string
  subscriptionName: string
  ruleName: string
  ruleSqlExpression: string
  sessionEnabled: bool?
}

@minLength(1)
param serviceBusName string

@metadata({
  description: 'Topic\'s subscriptions'
})
param topicSubscriptions topicSubscription[]

resource serviceBusNameSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2017-04-01' = [for item in topicSubscriptions: {
  name: '${serviceBusName}/${item.topicName}/${item.subscriptionName}'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    requiresSession: item.?sessionEnabled  ?? false
  }
}]

resource serviceBusNameSubscriptionRules 'Microsoft.ServiceBus/namespaces/topics/subscriptions/Rules@2017-04-01' = [for item in topicSubscriptions: {
  name: '${serviceBusName}/${item.topicName}/${item.subscriptionName}/${item.ruleName}'
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
