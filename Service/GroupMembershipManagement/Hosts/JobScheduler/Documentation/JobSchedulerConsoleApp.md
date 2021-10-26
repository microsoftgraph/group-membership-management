# Job Scheduler Console App
This guide will explain how to use the JobScheduler console app, found in JobScheduler/Console

## Prerequisites
* Visual Studio (tested in VS 2019, but likely works with other versions as well)
* Download the latest version of the public Github repository from: https://github.com/microsoftgraph/group-membership-management
* Ensure that GMM is already set up in your environment, as you will need some of the values from your gmm-data- keyvault for the console app

## Setup
1. Open the JobSchedulerConsoleApp.sln (Found in ../Hosts.Console/JobSchedulerConsoleApp.sln) in Visual Studio
2. Edit the Settings.json file within the Hosts.Console directory with the corresponding values:
```
    logAnalyticsCustomerId          | Found in data keyvault, same name
    logAnalyticsPrimarySharedKey    | Found in data keyvault, same name
    jobsTableConnectionString       | Found in data keyvault, under "jobsStorageAccountConnectionString"
    jobsTableName                   | Found in data keyvault, same name
    resetJobs                       | Custom input, true if you want to reset the times of jobs to a certain day, false otherwise
    daysToAddForReset               | Custom input, if resetJobs is on, this represents how many days in the future to reset job StartDates to (this can be a negative number)
    distributeJobs                  | Custom input, true if you want to distribute jobs evenly by their period, false otherwise
    includeFutureJobs               | Custom input, if resetJobs and / or distributeJobs is on, this is true if you want to include jobs with StartDate in the future
    defaultRuntime                  | Custom input, the approximate runtime in seconds of each job (the app will base its scheduling on this runtime so choose carefully)
    startTimeDelayMinutes           | Custom input, the delay in minutes to wait before running the first of the scheduled / distributed jobs
    delayBetweenSyncsSeconds        | Custom input, the delay in seconds to wait between each scheduled / distributed job
    APPINSIGHTS_INSTRUMENTATIONKEY  | Found in data keyvault, under "appInsightsInstrumentationKey"
```
<i>Note: If ResetJobs and DistributeJobs are both true, then jobs will be reset and then distributed</i>

## Running the app
1. Make sure that you complete all of the setup
2. Ensure that the JobSchedulerConsole app is set as the Startup Project for the solution
3. Hit the Run button in Visual Studio and you should be good to go!
