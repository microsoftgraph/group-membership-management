@description('Name of the Network Security Group.')
param name string

@description('Azure region for the NSG.')
param location string

type securityRuleType = {
  @description('Name of the security rule.')
  name: string
  @description('Properties of the security rule.')
  properties: {
    @description('Priority of the rule (100-4096).')
    priority: int
    @description('Direction of the rule.')
    direction: 'Inbound' | 'Outbound'
    @description('Whether to allow or deny traffic.')
    access: 'Allow' | 'Deny'
    @description('Protocol the rule applies to.')
    protocol: 'Tcp' | 'Udp' | 'Icmp' | '*'
    @description('Source port range.')
    sourcePortRange: string?
    @description('Source port ranges.')
    sourcePortRanges: string[]?
    @description('Destination port range.')
    destinationPortRange: string?
    @description('Destination port ranges.')
    destinationPortRanges: string[]?
    @description('Source address prefix.')
    sourceAddressPrefix: string?
    @description('Destination address prefix.')
    destinationAddressPrefix: string?
  }
}

@description('Security rules to apply to the NSG.')
param securityRules securityRuleType[]

resource nsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: name
  location: location
  properties: {
    securityRules: securityRules
  }
}

output id string = nsg.id
output name string = nsg.name
