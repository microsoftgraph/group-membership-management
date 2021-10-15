# Migrating Existing Application Insights to use Workspaces

1. Find the Application Insights template that you're using in your repo, ie: [applicationInsights.json](../Infrastructure/data/applicationInsights.json)

2. Update the properties of the App Insights resource "Microsoft.Insights/components" to include the following:

<b>JSON ARM Template</b>
```
"resources": [
{
  "type": "Microsoft.Insights/components",
  ...
  "properties": {
    ...
    "IngestionMode": "LogAnalytics",
    "WorkspaceResourceId": "<your-workspace-id>"
  }
}
```

<b>BICEP Template</b>
```
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  ...
  properties: {
    ...
    Application_Type: kind
    IngestionMode: "LogAnalytics"
    WorkspaceResourceId: <your-workspace-id>
  }
}
```

3. If you have an existing LogAnalytics resource, you can just use the Workspace Id from that resource because LogAnalytics already
leverages Workspaces, its resource name is "Microsoft.OperationalInsights/workspaces". Otherwise, you will need to create a similar
resource and then leverage the WorkspaceId from that.