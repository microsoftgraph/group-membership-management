# Notifier Function App
This guide will explain how to use the Notifier function, found in Notifier/Function

## Prerequisites
* Visual Studio (tested in VS 2019, but likely works with other versions as well)
* Download the latest version of the public Github repository from: https://github.com/microsoftgraph/group-membership-management
* Ensure that GMM is already set up in your environment, as you will need some of the values from your gmm-data- keyvault for the console app

### Notifier Config
Format:
```
{
    WorkspaceId: string
}
```

### Grant "Log Analytic Reader" permission
* Navigate to your Log Analytics resource.
* Under "Access control (IAM)" click on "Add", then "Add role assignment".
* Select "Log Analytics Reader" role, then click "Next"
* Click on "Select members" and search for your `<solution>-compute-<environment>-Notifier` function, then click on "Select".

Alternatively you can grant the permission by running this PowerShell cmdlet

```
New-AzRoleAssignment -ObjectId <object-id> `
-RoleDefinitionName "Log Analytics Reader" `
-Scope /subscriptions/<subscriptionId>/resourcegroups/<resourceGroupName>/providers/<providerName>/<resourceType>/<resourceSubType>/<resourceName>
```

ObjectId: This is Notifier function identity id.  
Scope: This is Log Analytics Workspace resource id.