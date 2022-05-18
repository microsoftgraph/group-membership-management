# Group Membership Management (GMM) tool Overview

This tool enables admins to sync the membership of Microsoft 365 Groups using one or more security groups that may or may not be nested, and keep the memberships current by syncing with the source groups at regular intervals.

Please read before proceeding

-   The tool is based on .Net, Azure Functions and Azure Table Storage. All of these are requirements and must be deployed by the customer onto their Azure subscription.
-   The tool interacts with Microsoft cloud using Graph APIs as data source. The app needs to be onboarded and granted permissions by the customer tenant admin.
-   The tool allows specifying the source security groups, destination Microsoft 365 Group, frequency of sync, start date of sync.
-   Microsoft is releasing the tool without support, other than answering questions about how we use it internally. Link to the demo video: [Making IT more efficient with improvements to Microsoft 365 Groups](https://aka.ms/Admin1011).

Limitations:
- Note that this tool can not use on-premise mastered SGs as destination groups since we are not able to add GMM Graph application (see "Create `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` Azure Application" section.) as owner to such groups as the owner does not sync to AAD.

## GMM Setup

### This document will provide guidance on how to setup GMM.

## Prerequisites

-   Azure Subscription - [Try Azure](https://azure.microsoft.com/en-us/free/).
-   Azure DevOps - [Try Azure DevOps Services](https://azure.microsoft.com/en-us/pricing/details/devops/azure-devops-services/)

    To follow these steps you will need to sign in to [Azure Portal](https://portal.azure.com/) and [Azure DevOps Portal](https://dev.azure.com/.) with an account that has permissions to create new Azure resources.

-   Powershell v5.1 or later [Download and install Windows PowerShell 5.1](https://docs.microsoft.com/en-us/skypeforbusiness/set-up-your-computer-for-windows-powershell/download-and-install-windows-powershell-5-1)

If you would like to customize GMM code, you could do so by using any of the following IDEs:

- Visual Studio Community, Professional or Enterprise Edition(s)
- Visual Studio Code

You can download Visual Studio or Visual Studio Code from here [Download](https://visualstudio.microsoft.com/downloads/).

Currently GMM is targeting .NET SDK version 3.1.417, this is being set in [global.json](/Service/GroupMembershipManagement/global.json), you can download this specific version from [Download .NET Core 3.1](https://dotnet.microsoft.com/download/dotnet-core/3.1) or alternatively download the latest version and update the global.json file.

To find out what .NET SDK versions you currently have installed run this command from the command line:

    dotnet --list-sdks

## Download GMM source code from GitHub

Navigate to GMM repository [here](https://github.com/microsoftgraph/group-membership-management) to download the source code.

You can download the code as a zip file or clone the repository using this command:

    git clone --bare https://github.com/microsoftgraph/group-membership-management.git

## GMM Environments

The code is provided with three sample environments:

 * int - integration
 * ua - user acceptance
 * prodv2 - production

These names must not be reused, see [`'Resource groups'`](#resource-groups) for more details.

The steps in this document will setup a single environment i.e. prodv2, if you would like to setup other environments i.e. int and ua, you will need to go through these steps again replacing `<EnvironmentAbbreviation>` accordingly.

Both `<SolutionAbbreviation>` and `<EnvironmentAbbreviation>` must be all numbers and lowercase letters! Using capital letters in either will cause problems later down the line!

### Add new environments
If you would like to add additional environments, follow these steps:

1. Locate and open file [vsts-cicd.yml](/vsts-cicd.yml)
2. Locate `int` environment `yaml/deploy-pipeline.yml` template.

        - template: yaml/deploy-pipeline.yml
        parameters:
            solutionAbbreviation: '$(SolutionAbbreviation)'
            environmentAbbreviation: 'int'
            location: 'westus2'
            serviceConnection: '$(SolutionAbbreviation)-serviceconnection-int'
            dependsOn: Build_Functions
            stageName: 'NonProd_int'
            functionApps:
            - name: 'GraphUpdater'
            - name: 'MembershipAggregator'
            - name: 'SecurityGroup'
            - name: 'AzureTableBackup'
            - name: 'AzureUserReader'
            - name: 'JobScheduler'
            - name: 'JobTrigger'
            condition: |
            and(
                succeeded('Build_Functions'),
                eq(variables['Build.SourceBranch'], 'refs/heads/develop'),
                in(variables['Build.Reason'], 'IndividualCI', 'Manual')
            )
3. Copy and paste the template located in step two, then replace the values for these settings accordingly using the name of your new environment.
   - environmentAbbreviation
   - serviceConnection
   - stageName

   Save your changes.
4. Search for the file `parameters.int.json`. Repeat the following steps for all the files:
        * Copy and paste the same file at the same location
        * Change the name to `parameters.<your-new-environment-name>.json`


Note: The order in which some of the functions are deployed is really important, make sure these functions are defined in your YAML in this order:
- GraphUpdater
- MembershipAggregator
- SecurityGroup


### Remove existing environments
If you would like to remove environments, follow these steps:

1. Locate and open file [vsts-cicd.yml](/vsts-cicd.yml)
2. Locate the `yaml/deploy-pipeline.yml` template for the environment you would like to delete.

        - template: yaml/deploy-pipeline.yml
        parameters:
            solutionAbbreviation: '$(SolutionAbbreviation)'
            environmentAbbreviation: 'int'
            location: 'westus2'
            serviceConnection: '$(SolutionAbbreviation)-serviceconnection-int'
            dependsOn: Build_Functions
            stageName: 'NonProd_int'
            functionApps:
            - name: 'GraphUpdater'
            - name: 'MembershipAggregator'
            - name: 'SecurityGroup'
            - name: 'AzureTableBackup'
            - name: 'AzureUserReader'
            - name: 'JobScheduler'
            - name: 'JobTrigger'
            condition: |
            and(
                succeeded('Build_Functions'),
                eq(variables['Build.SourceBranch'], 'refs/heads/develop'),
                in(variables['Build.Reason'], 'IndividualCI', 'Manual')
            )
3. Delete the template and save your changes. You might need to update any templates that had a dependency on the deleted template. For instance `dependsOn` and `condition` settings in `prodv2` template reference `ua`, so these would need to be updated in case `ua` was removed.
4. Search for the file `parameters.<environment-you-want-to-delete>.json` and delete the file.
# Create Resource Groups and prereqs keyvault

## Resource groups

GMM logically separates the resources it uses into three [resource groups](https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/manage-resource-groups-portal#what-is-a-resource-group).

-   prereqs
-   data
-   compute

Throughout this document we will use these tokens `<SolutionAbbreviation>`, `<ResourceGroupName>`, `<EnvironmentAbbreviation>`as place holders, when setting up GMM you will need to provide the value for each one of them as they will be used to name the Azure resources. Some Azure resources require to have a unique name across all tenants globally. So please avoid using the names used on this document as they are already in use.

- `<SolutionAbbreviation>` - This is a name prefix (2 to 3 characters long) the current default value is 'gmm'. To change this value see the Notes section below for more information on how to do that.
- `<ResourceGroupName>` - This is the name of the resource group, the current values supported are prereqs, data, and compute.
- `<EnvironmentAbbreviation>` - This the name of your environment (2 to 6 characters long), use a unique value here to prevent name collisions. See the Notes section below for more information on how to set the value for this setting.

The naming convention for the resource groups and other resources is `<SolutionAbbreviation>`-`<ResourceGroupName>`-`<EnvironmentAbbreviation>`, i.e gmm-data-ua, gmm-data-prod, gmm-compute-prod.

A PowerShell script has been provided to create the resource groups, see section [`'Resource Groups and prereqs keyvault creation script'`](#resource-groups-and-prereqs-keyvault-creation-script).

We create these resource groups in order for the ARM templates to be able to create additional resources and deploy the code.

You will need to replace `<SolutionAbbreviation>` and `<EnvironmentAbbreviation>` with the values you would like to use.

### Note:

Currently `<SolutionAbbreviation>` default value is 'gmm'. To change this value, update the `solutionAbbreviation` variable in vsts-cicd.yml file. You can make this change as part of 'Getting GMM code ready' step.

`<SolutionAbbreviation>` currently support names of 2 or 3 characters long. `<EnvironmentAbbreviation>` currently support names from 2 to 6 characters long. This can be changed in the ARM templates (template.bicep) by updating the `minLength` and `maxLength` settings for `solutionAbbreviation` and `environmentAbbreviation` parameters.

We recommend trying to use unique `<SolutionAbbreviation>` and `<EnvironmentAbbreviation>` names, since some resources in Azure require to have unique names globally so it is possible to have name collisions.

Both `<SolutionAbbreviation>` and `<EnvironmentAbbreviation>` must be all numbers and lowercase letters! Using capital letters in either will cause problems later down the line!

The changes required are:
- Rename the parameter files provided (parameters.int.json, parameters.ua.json and parameters.prodv2.json) updating the environment part. parameters.`<EnvironmentAbbreviation>`.json.
The files are located in these folders:
  - `Infrastructure\data\parameters`
  - `Service\GroupMembershipManagement\Hosts\*\Infrastructure\data\parameters`
  - `Service\GroupMembershipManagement\Hosts\*\Infrastructure\compute\parameters`
- Update vsts-cicd.yml settings:
   - environmentAbbreviation
   - serviceConnection
   - stageName
   - dependsOn (update for prodv2)
   - condition (update for prodv2)

## Prereqs keyvault

Each resource group will have a corresponding keyvault; The naming convention for the keyvault is the same as the resource groups.
In this step we are going to create only the `<SolutionAbbreviation>`-prereqs-`<EnvironmentAbbreviation>` keyvault since it needs to be populated before deploying the ARM templates. The keyvault must be created under the corresponding resource group, in this case `<SolutionAbbreviation>`-prereqs-`<EnvironmentAbbreviation>` resource group.

These two keyvaults are created by the ARM templates, so no action is needed for these two.

-   `<SolutionAbbreviation>`-data-`<EnvironmentAbbreviation>`
-   `<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`

## Resource Groups and prereqs keyvault creation script

This script is going to create the Azure resource groups required to setup GMM.
From your PowerShell command prompt navigate to the Scripts folder then type these commands:

    1. . ./Set-Environment.ps1
    2. Set-Environment  -solutionAbbreviation "<solutionAbbreviation>" `
                        -environmentAbbreviation "<environmentAbbreviation>" `
                        -objectId "<objectId>" `
                        -resourceGroupLocation "<resourceGroupLocation>" `
                        -overwrite $true

`<objectId>` is the Azure Object Id of the user, group or service principal to which access to the prereqs keyvault is going to be granted. This object Id must be located in the same Azure tenant where the keyvault is going to be created.
`<resourceGroupLocation>` is the Azure location where the resources are going to be created. Please refer to [this](https://docs.microsoft.com/en-us/azure/azure-resource-manager/templates/resource-location?tabs=azure-powershell) documentation to know the available resource locations.

If you get an error stating "script is not digitally signed" when running any of the provided PowerShell scripts, try running this cmdlet

    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

## Populate prereqs keyvault

### Create `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` Azure Application

Run this PowerShell script in order to create a new application that is going to enable GMM to access Microsoft Graph API, it will also save these settings in the prereqs keyvault.

-   graphAppClientId
-   graphAppTenantId
-   graphAppClientSecret

From your PowerShell command prompt navigate to the Scripts folder then type these commands:

    1. . ./Set-GraphCredentialsAzureADApplication.ps1
    2. Set-GraphCredentialsAzureADApplication	-SubscriptionName "<SubscriptionName>" `
                                                -SolutionAbbreviation "<SolutionAbbreviation>" `
                                                -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                                -TenantIdToCreateAppIn "<TenantId>" `
                                                -TenantIdWithKeyVault "<TenantId>" `
                                                -Clean $true `
                                                -Verbose

    Follow the instructions on the screen.

Once your application is created we need to grant the requested permissions to use Microsoft Graph API.

1. In the Azure Portal navigate to your 'Azure Active Directory'. If you don't see it on your screen you can use the top search bar to locate it.
2. Navigate to 'App registrations' blade on the left menu.
3. Click on 'All applications" to locate and open your `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.
4. On your application screen click on 'API permissions' blade on the left menu.
5. Click on the 'Grant admin consent for `<YourOrganizationName>`' button.
6. You might need to refresh the page to see the permissions status updated.

## Configure Azure Devops

-   ### Sign in to [Azure DevOps](https://azure.microsoft.com/en-us/services/devops/)

-   ### Create a private project

    -   You can use an existing project in your organization.
    -   To create a new project see [Create a project in AzureDevOps](https://docs.microsoft.com/en-us/azure/devops/organizations/projects/create-project?view=azure-devops&tabs=preview-page) documentation.

-   ### Create repositories

    -   Create two new repositories:
        - one repository (let's call it `public`) that mimics this GitHub repository.
            - see [Manually import a repo](https://docs.microsoft.com/en-us/azure/devops/repos/git/import-git-repository?view=azure-devops#manually-import-a-repo) documentation to push the code from this GitHub repo to `public` repo
            - keep the commit history of `public` repo in sync with this GitHub repo by running the following commands from `public` repo:
            ```
            git remote add upstream https://github.com/microsoftgraph/group-membership-management.git
            git fetch upstream
            git checkout upstream/master -b `<name-of-your-branch-in-public-repo>`
            git merge upstream/master
            git push --set-upstream origin <name-of-your-branch-in-public-repo> -f
            ```
        - another repository (let's call it `private`) that refers to `public`repository as a submodule:
            - copy the files from [Private](/Private) folder to your `private` repository
            - rename the file `parameters.env.json` to `parameters.<your-environment-abbreviation>.json`
            - replace `<ProjectName>/<RepositoryName>` in vsts-cicd.yml with your project name & repository name
            - replace `env` in vsts-cicd.yml with your environment abbreviation
            - create `public` submodule by running the following command:
            ```
            git submodule add <url-of-public-repo> <name-of-public-repo>
            ```
            - Let’s say a new commit is added to the main branch in `public` repository. To add that new commit to the submodule in `private` repository, run the following commands:
            ```
            git submodule update --remote --merge
            git add *
            git commit -m “updated public submodule”
            git push
            ```
     *Follow [Create a new Git repo in your project](https://docs.microsoft.com/en-us/azure/devops/repos/git/create-new-repo?toc=%2Fazure%2Fdevops%2Forganizations%2Ftoc.json&bc=%2Fazure%2Fdevops%2Forganizations%2Fbreadcrumb%2Ftoc.json&view=azure-devops) to create this repository.*
-   ### Getting GMM code ready

    GMM uses ARM templates to create all the resources it needs. It requires you to provide information specific to your Azure Subscription in order to create these resources.

    Before being able to deploy GMM code to your environment you will need to provide several parameters to the ARM templates responsible of creating the resources.

    Locate GMM code, it has the following structure.

    -   Documentation
    -   Infrastructure
        -   data
            -   parameters
    -   Scripts
    -   Service
        -   Hosts
            -   JobTrigger
    -   yaml

    Under Service folder, locate Hosts folder, this folder may contain one or more folders each representing a function, all of them will follow the same folder structure, open a function folder (i.e. JobTrigger) and locate the Infrastructure folder, this folder might contain a compute and data folder, similar to what we just did, review the parameters files on both compute and data folders, and provide the required values specific to your environment. This needs to be done to all the functions that may be present under Hosts folder.

    Infrastructure folder contains all the ARM templates, it has separate folders for data and compute resources, which in turn have a parameters folder.

    Note:
    Currently `<SolutionAbbreviation>` default value is 'gmm'. To change this value, update the `solutionAbbreviation` variable in vsts-cicd.yml file.
-   ### Create a Service Connection

    In order to deploy GMM resources through a pipeline we need to create a [Service Connection](https://docs.microsoft.com/en-us/azure/devops/pipelines/library/service-endpoints?view=azure-devops&tabs=yaml) and grant permissions to it.

    GMM provides a PowerShell script to accomplish this.

    1.  Set-ServicePrincipal.ps1

        This script will create a new service principal.
        It takes two arguments: `<SolutionAbbreviation>` and `<EnvironmentAbbreviation>`.

        From your PowerShell command prompt navigate to the Scripts folder then type these commands. This script must be run by someone with the Owner role on the subscription.

            1. . ./Set-ServicePrincipal.ps1
            2. Set-ServicePrincipal -SolutionAbbreviation "<SolutionAbbreviation>"  `
            		                              -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
            		                              -Verbose

        Follow the instructions on the screen.

        Locate the service connection name on the screen. It follows this naming convention: `<SolutionAbbreviation>`-serviceconnection-`<EnvironmentAbbreviation>`.

    2. Set-ServiceConnection

        This script sets up the service connection. Ensure that you're an owner of the service connection you created in the last step. Then, run the following command. `<SolutionAbbreviation>` and `<EnvironmentAbbreviation>` are as before, plus two new ones.

        `<OrganizationName>` - This is the name of your organization used in Azure DevOps.
        `<ProjectName>` - This is the name of the project in Azure DevOps we just created in a previous step.

            1. . ./Set-ServiceConnection.ps1
            2. Set-ServiceConnection -SolutionAbbreviation "<SolutionAbbreviation>"  `
            		                              -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                                 -OrganizationName "<OrganizationName>" `
                                                 -ProjectName "<ProjectName>" `
            		                              -Verbose

-   ### Email Notification

    Please follow the steps in this documentation, which will ensure that the requestor is notified regarding the synchronization job status:
    [SetSenderAddressForEmailNotification.md](/Service/GroupMembershipManagement/Repositories.Mail/Documentation/SetSenderAddressForEmailNotification.md)

-   ### Create a pipeline

    In Azure DevOps we need to create a pipeline that will create your resources and deploy your code.

    -   See [Create your first pipeline](https://docs.microsoft.com/en-us/azure/devops/pipelines/create-first-pipeline?view=azure-devops&tabs=java%2Cyaml%2Cbrowser%2Ctfs-2018-2) documentation.
        1. On Azure DevOps left menu locate and click on Pipelines.
        2. Click on 'Create Pipeline' or 'New Pipeline' depending on which one is presented to you.
        3. Select Azure Repos Git as your code location.
        4. Select the repository created in the previous step.
        5. From the list of options select 'Existing Azure Pipelines YAML file'.
        6. Select your branch.
        7. Select '/vsts-cicd.yml' in the Path field.
        8. Click continue.
        9. You will be presented with the "Review your pipeline YAML" screen. Locate and click on the "Variables" button on the top right side of your screen. We need to create the variables used by the pipeline.

               location - This is your Azure location where the resources are going to be created.

               tenantId - This is your Azure Active Directory tenant Id, where GMM Azure resources were created.

               keyVaultReaders_prod - This is a list of service principals that will have access to the keyvaults in production environment. i.e. your own Azure user id, an Azure group id.

               keyVaultReaders_nonprod - This is a list of service principals that will have access to the keyvaults in non-production environments. i.e. your own Azure user id, an Azure group id.

               This variable's value is a JSON string that represents an array, notice that each object in the array has two properties:

               objectId: This is the group or user object id.
               permissions: This is the list of permissions that will be set.

               You can add or remove objects from the json array as needed.

                [
                    {
                    "objectId": "<object-id-1>",
                    "permissions": [ "get", "set", "list" ]
                    },
                    {
                    "objectId": "<object-id-2>",
                    "permissions": [ "get", "set", "list" ]
                    }
                ]


            To find the  group or user id in Azure follow these steps:
            1. In the Azure Portal navigate to your 'Azure Active Directory'. If you don't see it on your screen you can use the top search bar to locate it.
            2. For users locate the 'Users' blade and for groups locate the 'Groups' blade on the left menu.
            3. Search for the name of the user or group and select it from the results list.
            4. Locate the Object ID field. This is the value that you will need to copy.

        10. Click on the "New variable" button. Provide the name and value, then click on the "OK" button. To add a new variable click on the button with the plus sign icon.
        11. Once all variables have been created click on the "Save" button.
        12. Run your pipeline.

        When running the pipeline for the first time you might be prompted to authorize resources, click on "Authorize resources" buttons.

        *Points to remember while running the pipeline:*
         * *If you see an error task `mspremier.BuildQualityChecks.QualityChecks-task.BuildQualityChecks` is missing, install it from [here](https://marketplace.visualstudio.com/items?itemName=mspremier.BuildQualityChecks&ssr=false&referrer=https%3A%2F%2Fapp.vssps.visualstudio.com%2F#overview)*
         * *If you see an error `no hosted parallelism has been purchased or granted`, please fill out [this](https://aka.ms/azpipelines-parallelism-request) form to request a free parallelism grant*
         * *If you see an error `MissingSubscriptionRegistration`, go to Subscription -> Resource Providers and register the missing provider*

# Post-Deployment Tasks

Once the pipeline has completed building and deploying GMM code and resources to your Azure resource groups, we need to make some final configuration changes.

### Grant SecurityGroup, MembershipAggregator, GraphUpdater function access to storage account

SecurityGroup and GraphUpdater need MSI access to the storage account where the membership blobs are going to be temporary stored.

Once your Function Apps:
- `<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-SecurityGroup`
- `<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-MembershipAggregator`
- `<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-GraphUpdater`

have been created we need to grant them access to the storage account containers.

    By default GMM uses the same account that was automatically created to store the sync job's information.
    The naming convention is jobs<environmentAbbreviation><randomId>, the exact name can be found in the data key vault secrets under 'jobsStorageAccountName'.

    StorageAccountName:  jobs<environmentAbbreviation><randomId>
    FunctionAppNames:
    <SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-SecurityGroup
    <SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-MembershipAggregator
    <SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-GraphUpdater

    1. . ./Set-StorageAccountContainerManagedIdentityRoles.ps1
    2. Set-StorageAccountContainerManagedIdentityRoles -SolutionAbbreviation "<SolutionAbbreviation>" `
                                                       -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                                       -StorageAccountName "<StorageAccountName>
                                                       -FunctionAppName "<FunctionAppName>" `
                                                       -Verbose

    Run the script for each function app name, SecurityGroup, MembershipAggregator and GraphUpdater.
### Access to App Configuration

Grant all the functions access to the AppConfiguration by running the following script:

    1. . ./Set-AppConfigurationManagedIdentityRoles.ps1
    2. Set-AppConfigurationManagedIdentityRoles  -SolutionAbbreviation "<SolutionAbbreviation>" `
                                                -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                                -FunctionAppName "<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-JobTrigger" `
                                                -AppConfigName "<SolutionAbbreviation>-appConfig-<EnvironmentAbbreviation>" `
                                                -Verbose

- The above is an example for `JobTrigger`. Please update FunctionAppName and run the script for other functions as well.

### Creating synchronization jobs for source groups

Once GMM is up and running you might want to start creating synchronization jobs for your groups.

A synchronization job must have the following properties populated:

- PartitionKey
- RowKey
- Requestor
- TargetOfficeGroupId
- Status
- LastRunTime
- Period
- Query
- StartDate
- ThresholdPercentageForAdditions
- ThresholdPercentageForRemovals
- ThresholdViolations
- IsDryRunEnabled
- DryRunTimeStamp

### PartitionKey
Partition key, the value added here represents the date the job was added to the table.
- DataType: string
- Format: YYYY-M-D

### RowKey
Unique key of the synchronization job.
- DataType: string
- Format: Guid

### Requestor
Email address of the person who requested the synchronization job.
- DataType: string
- Format: Email address

### TargetOfficeGroupId
Azure Object Id of destination group.
- DataType: Guid

### Status
Current synchronization job status; Set to Idle for new synchronization jobs.
- DataType: string
- Valid values: Idle, InProgress, Error

### LastRunTime
Last date time the synchronization job ran. Set to 1601-01-01T00:00:00.000Z for new synchronization jobs.
- DataType: DateTime
- Format: YYYY-MM-DDThh:mm:ss.zzzZ

### Period
Defines in hours, how often a synchronization job will run.
- DataType: int

### Query
Defines the Azure ObjectId of the security group that will be used as the source for the synchronization. One or multiple ids separated by semicolon ";" can be provided.
i.e. (single id) dffad54b-88fe-4459-9dd1-e2e2a415d586
i.e. (multiple ids) dffad54b-88fe-4459-9dd1-e2e2a415d586;065cfbc2-ad4f-47c8-8233-3cf55edd0509
- DataType: string
- Format: Guid

### StartDate
Defines the date and time when the synchronization job should start running, this allows to schedule jobs to run in the future.
i.e. 2021-01-01T00:00:00.000Z
- DataType: DateTime
- Format: YYYY-MM-DDThh:mm:ss.zzzZ

### ThresholdPercentageForAdditions
Threshold percentage for users being added.
If the threshold is exceeded GMM is not going to make any changes to the destination group and an email notification will be sent describing the issue.
The email notification will be sent to the recipients defined in the 'SyncDisabledEmailBody' setting located in the prereqs keyvault. Multiple email addresses can be specified separated by semicolon.
To continue processing the job increase the threshold value or disable the threshold check by setting it to 0 (zero).
- DataType: int

### ThresholdPercentageForRemovals
Threshold percentage for users being removed.
If the threshold is exceeded GMM is not going to make any changes to the destination group and an email notification will be sent describing the issue.
The email notification will be sent to the recipients defined in the 'SyncDisabledEmailBody' setting located in the prereqs keyvault. Multiple email addresses can be specified separated by semicolon.
To continue processing the job increase the threshold value or disable the threshold check by setting it to 0 (zero).
- DataType: int

### ThresholdViolations
Indicates how many times the threshold has been exceeded.
It gets reset to 0 once the job syncs successfully.

### IsDryRunEnabled
Indicates if the job will run in DryRun (read-only) mode making no changes to the destination group.
### DryRunTimeStamp
Last date time the synchronization job ran in DryRun mode. Set to 1601-01-01T00:00:00.000Z for new synchronization jobs.
- DataType: DateTime
- Format: YYYY-MM-DDThh:mm:ss.zzzZ

### Powershell script to create SecurityGroup jobs
A PowerShell script [New-GmmSecurityGroupSyncJob.ps1](/Service/GroupMembershipManagement/Hosts/SecurityGroup/Scripts/New-GmmSecurityGroupSyncJob.ps1) is provided to help you create the synchronization jobs.

The Query field requires a JSON object that must follow this format:

```
[
    {
        "type": "SecurityGroup",
        "sources":
        [
            "id 1",
            "id 2",
            "id n"
        ]
    }
]
```
The script can be found in \Service\GroupMembershipManagement\Hosts\SecurityGroup\Scripts folder.

    1. . ./New-GmmSecurityGroupSyncJob.ps1
    2. New-GmmSecurityGroupSyncJob	-SubscriptionName "<SubscriptionName>" `
                            -SolutionAbbreviation "<SolutionAbbreviation>" `
							-EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
							-Requestor "<RequestorEmailAddress>" `
							-TargetOfficeGroupId "<DestinationGroupObjectId>" `
							-Query "<JSON string>" `
							-Period <in hours, integer only> `
							-ThresholdPercentageForAdditions <integer only> `
							-ThresholdPercentageForRemovals <integer only> `
							-Verbose

You can also use Microsoft Azure Storage Explorer to add, edit or delete synchronization jobs. see [Get started with Storage Explorer](https://docs.microsoft.com/en-us/azure/vs-azure-tools-storage-manage-with-storage-explorer?tabs=windows).

### Adding Graph application as an owner to GMM managed destination group

`<solutionAbbreviation>-Graph-<environmentAbbreviation>` application must be added as an owner to any destination group that will be managed by GMM in order for GMM to have the right permissions to update the group.

In order to add the application as an owner of a group follow the next steps:
1. In the Azure Portal navigate to your 'Azure Active Directory'. If you don't see it on your screen, you can use the top search bar to locate it.
2. Navigate to 'Groups' blade on the left menu.
3. Locate and open the group you would like to use.
4. Take note of the group's `Object Id`.
5. Navigate back (out of the 'Groups' blade) to the `Azure Active Directory` section of the portal.
6. Navigate to the `Enterprise applications` blade on the left menu.
7. Locate and open the `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application and select it from the results list.
8. Take note of the enterprise application's `Object ID`.
9. Open a PowerShell terminal as an administrator.
10. If not already installed, install the [`AzureAD` module]( https://www.powershellgallery.com/packages/AzureAD) version `2.0.2.128` or higher.
`Install-Module -Name AzureAD -RequiredVersion 2.0.2.128`
11. Import the AzureAD PowerShell Module
`Import-Module -Name AzureAD -RequiredVersion 2.0.2.128`
12. Connect with an authenticated account to use Active Directory cmdlet requests:
`Connect-AzureAD`
13. Execute the following command:
`Add-AzureADGroupOwner -ObjectId [Group Id (from step 4)] -RefObjectId [Object Id (from step 8)]`

*Note: regarding steps 10 - 13:
A newer version of this cmdlet is under development.  It will be available in an entirely different PowerShell module, [`Az.Resources`](https://www.powershellgallery.com/packages/Az.Resources).  The cmdlet will be renamed to `Add-AzADGroupOwner`.*

### Dry Run Settings

Dry run settings are present in GMM to provide users the ability to test new changes without affecting the group membership. This configuration is present in the application configuration table.
If you would like to have the default setting to be false, then please update the settings in the app configuration to false for the GraphUpdater and SecurityGroup.

There are 3 Dry Run flags in GMM. If any of these Dry run flags are set, the sync will be completed but destination membership will not be affected.
1. IsDryRunEnabled: This is a property that is set on an individual sync. Setting this to true will run this sync in dry run.
2. IsSecurityGroupDryRunEnabled: This is a property that is set in the app configuration table. Setting this to true will run all Security Group syncs in dry run.
3. IsGraphUpdaterDryRunEnabled: This is a property that is set in the app configuration table. Setting this to true will run all syncs in dry run.

In order for the Function Apps SecurityGroup and GraphUpdater to read the dry run values assigned above, we need to grant them access to the AppConfiguration:

FunctionAppName: `<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-SecurityGroup`

    1. . ./Set-AppConfigurationManagedIdentityRoles.ps1
    2. Set-AppConfigurationManagedIdentityRoles  -SolutionAbbreviation "<SolutionAbbreviation>" `
                                    -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                    -FunctionAppName "<function app name>" `
                                    -AppConfigName "<SolutionAbbreviation>-appConfig-<EnvironmentAbbreviation>" `
                                    -Verbose

FunctionAppName: `<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-GraphUpdater`

    1. . ./Set-AppConfigurationManagedIdentityRoles.ps1
    2. Set-AppConfigurationManagedIdentityRoles  -SolutionAbbreviation "<SolutionAbbreviation>" `
                                    -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                    -FunctionAppName "<function app name>" `
                                    -AppConfigName "<SolutionAbbreviation>-appConfig-<EnvironmentAbbreviation>" `
                                    -Verbose

# Setting AzureTableBackup function
`<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-AzureTableBackup function can create backups for Azure Storage Tables and delete older backups automatically.
Out of the box, the AzureTableBackup function will backup the 'syncJobs' table; where all the groups' sync parameters are defined. The function is set to run every day at midnight and will delete backups older than 30 days.

The function reads the backup configuration settings from the data keyvault (`<SolutionAbbreviation>`-data-`<EnvironmentAbbreviation>`), specifically from a secret named 'tablesToBackup' which is a string that represents a json array of backup configurations.

    [
        {
            "SourceTableName": "syncJobs",
            "SourceConnectionString": "<storage-account-connectionstring>",
            "DestinationConnectionString": "<storage-account-connectionstring>",
            "DeleteAfterDays": 30
        }
    ]

The default configuration for the 'syncJobs' table is generated via an ARM template. For more details see the respective ARM template located under Service\GroupMembershipManagement\Hosts\AzureTableBackup\Infrastructure\data\template.bicep

The run frequency is set to every day at midnight, it is defined as a NCRONTAB expression in the application setting named 'backupTriggerSchedule' which can be updated on the Azure Portal, it's located under the Configuration blade for `<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-AzureTableBackup Function App, additionally it can be updated directly in the respective ARM template located under Service\GroupMembershipManagement\Hosts\AzureTableBackup\Infrastructure\compute\template.bicep

# Setting GMM in a demo tenant

In the event that you are setting up GMM in a demo tenant refer to [Setting GMM in a demo tenant](/Documentation/DemoTenant.md) for additional guidance.

# Steps to debug and troubleshoot a failing sync

To troubleshoot any issues that might occur we can use Log Analytics and Application Insights.

1. Find Logs in the Log analytics workspace following the instructions [here](/Documentation/FindLogEntriesInLogAnalyticsForASync.md).
2. Find failures and exceptions with Application Insights [here](/Documentation/TroubleshootWithApplicationInsights.md).

# Breaking changes
See [Breaking changes](breaking_changes.md)