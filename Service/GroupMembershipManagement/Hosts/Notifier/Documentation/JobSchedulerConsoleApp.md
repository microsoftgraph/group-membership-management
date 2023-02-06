# Job Scheduler Console App
This guide will explain how to use the Notifier console app, found in Notifier/Console

## Prerequisites
* Visual Studio (tested in VS 2019, but likely works with other versions as well)
* Download the latest version of the public Github repository from: https://github.com/microsoftgraph/group-membership-management
* Ensure that GMM is already set up in your environment, as you will need some of the values from your gmm-data- keyvault for the console app

## Setup
1. Open the NotifierConsoleApp.sln (Found in ../Hosts/Notifier/Console/NotifierConsoleApp.sln) in Visual Studio
2. Edit the Settings.json file within the Hosts.Console directory with the corresponding values:
```
    logAnalyticsCustomerId          | Found in data keyvault, same name
    logAnalyticsPrimarySharedKey    | Found in data keyvault, same name
    workspaceId                     | Custom input, log analytics workspace id to query.
    APPINSIGHTS_INSTRUMENTATIONKEY  | Found in data keyvault, under "appInsightsInstrumentationKey"
```
<i>Note: If ResetJobs and DistributeJobs are both true, then jobs will be reset and then distributed</i>

## Running the app
1. Make sure that you complete all of the setup
2. Ensure that the NotifierConsole app is set as the Startup Project for the solution
3. Hit the Run button in Visual Studio and you should be good to go!
