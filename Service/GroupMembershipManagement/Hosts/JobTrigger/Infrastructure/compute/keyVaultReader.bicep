// Do not use this to return secrets like passwords or API keys
// This is only for returning plain values that are not sensitive
@description('Plain value')
@secure()
param value string
var plainValue = string(value)
output value string = plainValue
