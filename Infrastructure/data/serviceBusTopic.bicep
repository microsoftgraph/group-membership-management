@minLength(1)
param serviceBusName string

@minLength(1)
param topicName string

resource serviceBusTopic 'Microsoft.ServiceBus/namespaces/topics@2017-04-01' = {
  name: '${serviceBusName}/${topicName}'
  properties: {
    requiresDuplicateDetection: true
    defaultMessageTimeToLive: 'P14D'
    duplicateDetectionHistoryTimeWindow: 'PT01M'
  }
}
