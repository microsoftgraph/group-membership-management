@minLength(1)
param serviceBusName string

@minLength(1)
param queueName string
param requiresSession bool = true

resource serviceBusQueue 'Microsoft.ServiceBus/namespaces/queues@2017-04-01' = {
  name: '${serviceBusName}/${queueName}'
  properties: {
    requiresDuplicateDetection: true
    defaultMessageTimeToLive: 'P14D'
    requiresSession: requiresSession
    lockDuration: 'PT5M'
    maxDeliveryCount: 1
  }
}
