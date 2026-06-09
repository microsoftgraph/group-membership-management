@description('Name of the Virtual Machine.')
param name string

@description('Azure region for the VM.')
param location string

@description('Size of the VM.')
param vmSize string = 'Standard_B2ms'

@secure()
@description('Admin username for the VM.')
param adminUsername string

@secure()
@description('Admin password for the VM.')
param adminPassword string

@description('Windows computer name (max 15 characters). Defaults to the VM resource name truncated to 15 chars.')
param computerName string = take(name, 15)

@description('Resource ID of the NIC to attach to the VM.')
param nicId string

@description('Daily auto-shutdown time in UTC (24-hour format, e.g. 0200 = 2:00 AM UTC). Default is 0200 which equals 6:00 PM PST.')
param dailyAutoShutdownTimeUTC string = '0200'

type imageReferenceType = {
  @description('Image publisher.')
  publisher: string
  @description('Image offer.')
  offer: string
  @description('Image SKU.')
  sku: string
  @description('Image version.')
  version: string
}

@description('OS image reference for the VM.')
param imageReference imageReferenceType = {
  publisher: 'MicrosoftWindowsDesktop'
  offer: 'windows-11'
  sku: 'win11-25h2-pro'
  version: 'latest'
}

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: name
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    osProfile: {
      computerName: computerName
      adminUsername: adminUsername
      adminPassword: adminPassword
    }
    storageProfile: {
      imageReference: imageReference
      osDisk: {
        createOption: 'FromImage'
        managedDisk: {
          storageAccountType: 'Standard_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nicId
        }
      ]
    }
  }
}

resource aadLogin 'Microsoft.Compute/virtualMachines/extensions@2024-07-01' = {
  parent: vm
  name: 'AADLoginForWindows'
  location: location
  properties: {
    publisher: 'Microsoft.Azure.ActiveDirectory'
    type: 'AADLoginForWindows'
    typeHandlerVersion: '2.0'
    autoUpgradeMinorVersion: true
  }
}

resource azureMonitorAgent 'Microsoft.Compute/virtualMachines/extensions@2024-07-01' = {
  parent: vm
  name: 'AzureMonitorWindowsAgent'
  location: location
  properties: {
    publisher: 'Microsoft.Azure.Monitor'
    type: 'AzureMonitorWindowsAgent'
    typeHandlerVersion: '1.0'
    autoUpgradeMinorVersion: true
    enableAutomaticUpgrade: true
  }
}

resource guestConfigExtension 'Microsoft.Compute/virtualMachines/extensions@2024-07-01' = {
  parent: vm
  name: 'AzurePolicyforWindows'
  location: location
  properties: {
    publisher: 'Microsoft.GuestConfiguration'
    type: 'ConfigurationforWindows'
    typeHandlerVersion: '1.0'
    autoUpgradeMinorVersion: true
    enableAutomaticUpgrade: true
  }
}

resource autoShutdown 'Microsoft.DevTestLab/schedules@2018-09-15' = {
  name: 'shutdown-computevm-${name}'
  location: location
  properties: {
    status: 'Enabled'
    taskType: 'ComputeVmShutdownTask'
    dailyRecurrence: {
      time: dailyAutoShutdownTimeUTC
    }
    timeZoneId: 'UTC'
    targetResourceId: vm.id
    notificationSettings: {
      status: 'Disabled'
    }
  }
}

output id string = vm.id
output name string = vm.name
