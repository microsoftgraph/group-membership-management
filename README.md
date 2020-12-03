## GMM Setup Test
### This document will provide guidance on how to setup GMM.

## Prerequisites

* Azure Subscription - [Try Azure](https://azure.microsoft.com/en-us/free/).
* Visual Studio - [Download](https://visualstudio.microsoft.com/downloads/).

   To follow these steps you will need to sign in to [Azure Portal](https://portal.azure.com/) and [Azure DevOps Portal](https://dev.azure.com/.) with an account that has permissions to create new Azure resources.

## Download GMM source code from GitHub

Navigate to GMM repository [here](https://github.com/microsoftgraph/group-membership-management) to download the source code.

# Create Resource Groups and prereqs keyvault

## Resource groups
GMM logically separates the resources it uses into three groups.
- prereqs
- data
- compute

The naming convention for the resource groups is `<SolutionAbbreviation>`-`<ResourceGroupName>`-`<EnvironmentAbbreviation>`, i.e gmm-data-ua, gmm-data-prod, gmm-compute-prod. 

A PowerShell script has been provided to create the resource groups, see section `'Resource Groups and prereqs keyvault creation script'`.

We create these resource groups in order for the ARM templates to be able to create additional resources and deploy the code.

You will need to replace `<SolutionAbbreviation>` and `<EnvironmentAbbreviation>` with the values you would like to use.

### Note: 
Currently `<SolutionAbbreviation>` default value is 'gmm' to change this see `solutionAbbreviation` variable in vsts-cicd.yml file. You can make this change as part of 'Getting GMM code ready' step.

`<SolutionAbbreviation>` currently support names of 2 or 3 characters long. `<EnvironmentAbbreviation>` currently support names from 2 to 4 characters long. This can be changed in the ARM templates (template.json) by updating the `minLength` and `maxLength` settings for `solutionAbbreviation` and `environmentAbbreviation` parameters.

We recommend trying to use a unique `<SolutionAbbreviation>` name, some resources in Azure require to have unique names globally so it is possible to have name collisions.

## Prereqs keyvault

Each resource group will have a corresponding keyvault; The naming convention for the keyvault is the same as the resource groups.
In this step we are going to create only the `<SolutionAbbreviation>`-prereqs-`<EnvironmentAbbreviation>` keyvault since it needs to be populated before deploying the ARM templates. The keyvault must be created under the corresponding resource group, in this case `<SolutionAbbreviation>`-prereqs-`<EnvironmentAbbreviation>` resource group.

These two keyvaults are created by the ARM templates, so no action is needed for these two.
- `<SolutionAbbreviation>`-data-`<EnvironmentAbbreviation>`  
- `<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`

## Resource Groups and prereqs keyvault creation script

This script is going to create 
From your PowerShell command prompt navigate to the Scripts folder then type these commands:

    1. . ./Set-Environment.ps1
    2. Set-Environment  -solutionAbbreviation "<solutionAbbreviation>" `
                        -environmentAbbreviation "<environmentAbbreviation>" `
                        -objectId "<objectId>" `
                        -resourceGroupLocation "<resourceGroupLocation>" 

`<objectId>` is the Azure Object Id of the user, group or service principal to which access to the prereqs keyvault is going to be granted.  
`<resourceGroupLocation>` is an optional parameter to specify the Azure location where the resources are going to be created. The default location in the script is "West US 2".

## Populate prereqs keyvault
### Creating the certificate
We need to create a certificate that is going to be used for authentication, we are going to use the prereqs keyvault to create and store the certificate. Take note of the certicate name since we need to provide it in the next step.
See [Quickstart: Set and retrieve a certificate from Azure Key Vault using the Azure portal](https://docs.microsoft.com/en-us/azure/key-vault/certificates/quick-create-portal) documentation.

You can also use and existing certificate an upload it to the prereqs keyvault, you will need to provide a friendly certificate name that we will need in the next step.

### Create `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` Azure Application
Once the certificate is created, we need to run this PowerShell script in order to create a new application that is going to enable GMM to access Microsoft Graph API, it will also save these settings in the prereqs keyvault.  

- graphAppClientId  
- graphAppCertificateName
- graphAppTenantId

From your PowerShell command prompt navigate to the Scripts folder then type these commands:

    1. . ./Set-GraphCredentialsAzureADApplication.ps1
    2. Set-GraphCredentialsAzureADApplication	-SubscriptionName "<SubscriptionName>" `
                                                -SolutionAbbreviation "<SolutionAbbreviation>" `
                                                -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                                -TenantIdToCreateAppIn "<TenantId>" `
                                                -TenantIdWithKeyVault "<TenantId>" ` 
                                                -CertificateName "<CertificateName> `
                                                -Verbose
    	    									
    Follow the instrunctions on the screen.

Once your application is created we need to grant the requested permissions to use Microsoft Graph API.

1. In the Azure Portal navigate to your 'Azure Active Directory'. If you don't see it on your screen you can use the top search bar to locate it.
2. Navigate to 'App registrations' blade on the left menu.
3. Click on 'All applications" to locate and open your `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.
4. On your application screen click on 'API permissions' blade on the left menu.
5. Click on the 'Grant admin consent for `<YourOrganizationName>`' button.
6. You might need to refresh the page to see the permissions status updated.

### Upload the certificate to your `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.
We need to upload the certificate to the <solutionAbbreviation>-Graph-<environmentAbbreviation> application, in order to do that we need to export it from the prerqs keyvault. 

Exporting the certificate:

1. In the Azure Portal navigate to your prereqs keyvault, it will be named following this convention <solutionAbbreviation>-prereqs-<environmentAbbreviation>.
2. Locate and click on the Certificates blade on the left menu.
3. Click on your certificate from the list.
4. Click on the latest version.
5. On the top menu click on 'Download in CER format' button to download the certificate.

If you need more details on how to export the certificate please see [Quickstart: Set and retrieve a certificate from Azure Key Vault using the Azure portal](https://docs.microsoft.com/en-us/azure/key-vault/certificates/quick-create-portal) documentation.

Uploading the certificate:

1. In the Azure Portal navigate to your 'Azure Active Directory'. If you don't see it on your screen you can use the top search bar to locate it.
2. Navigate to 'App registrations' blade on the left menu.
3. Click on 'All applications" to locate and open your `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.
4. On your application screen click on 'Certificates and secrets' blade on the left menu.
5. Click on the 'Upload certificate' button.
6. Locate and add your certificate.


### Note:
This script can also be used to create the application on a different tenant that the one that has your keyvaults for testing purposes.
If you are creating the application on the same tenant where your keyvaults are located `<TenantId>` should be the same for both arguments:

- TenantIdToCreateAppIn
- TenantIdWithKeyVault

You can locate your Tenant Id in the [Azure Portal](http://portal.azure.com/), from the top search bar type 'Azure Active Directory' and click on it on the search results, your Tenant Id is located in the 'Overview' blade.

The script may ask you to login to your account via a popup window, this popup sometimes is displayed behind other windows that might be open at the time, if you don't see it, minimize your other open windows.

## Configure Azure Devops
* ### Sign in to [Azure DevOps](https://azure.microsoft.com/en-us/services/devops/)
* ### Create a project
    * You can use an existing project in your organzation.
    * To create a new project see [Create a project in AzureDevOps](https://docs.microsoft.com/en-us/azure/devops/organizations/projects/create-project?view=azure-devops&tabs=preview-page) documentation.

* ### Create a repository
    * Once your project is created in the previous step, it will have an empty repository, we are going to need a repository in the nexts steps, you can use this one or if you prefer to create a new one see [Create a new Git repo in your project](https://docs.microsoft.com/en-us/azure/devops/repos/git/create-new-repo?toc=%2Fazure%2Fdevops%2Forganizations%2Ftoc.json&bc=%2Fazure%2Fdevops%2Forganizations%2Fbreadcrumb%2Ftoc.json&view=azure-devops) documentation.

* ### Getting GMM code ready
     GMM uses ARM templates to create all the resources it needs. It requires you to provide information specific to you Azure Subscription in order to create these resources.

     Before being able to deploy GMM code to your environment you will need to provide several parameters to the ARM templates responsible of creating the resources.

     Locate GMM code, it has the following structure.

     * Documentation  
     * Infrastructure  
       * compute  
            * parameters  
       * data  
            * parameters  
     * Scripts  
     * Service  
        *  Hosts
            * JobTrigger
     * yaml  
    
    Infrastructure folder contains all the ARM templates, it has separate folders for data and compute resources, which in turn have a parameters folder.
    Please review the parameter files in these folders, they require values specific to your own environment.

    For compute you need to provide:
    - tenantId - This is your Azure Active Directory Tenant Id

    For data you need to provide:
    - tenantId - This is your Azure Active Directory Tenant Id
    - keyVaultReaders - This is a list of service principals that will have access to the keyvaults. i.e. your own Azure user id, an Azure group id.
    
    Under Service folder, locate Hosts folder, this folder may contain one or more folders each representing a function, all of them will follow the same folder structure, open a function folder (i.e. JobTrigger) and locate the Infrastructure folder, this folder might contain a compute and data folder, similar to what we just did, review the parameters files on both compute and data folders, and provide the required values specific to your environment. This needs to be done to all the functions that may be present under Hosts folder.

    Notice vsts-cicd.yml file is the main pipeline file and is located in the root folder, currenly for each stage a condition was set that determines when that specific stage would run, one of those conditions is to run only for an specific user `startsWith(variables['Build.SourceBranch'], 'refs/heads/users/<User>/')`,  following a branch naming convention 'users/<User>/<BranchName>'. Update this to reflect your own naming convention, alternatively you could remove or add conditions to meet your requirements.

    Once you make these changes commit and push your code to your repository. If you need help cloning and pushing your code see [Azure Repos Git tutorial](https://docs.microsoft.com/en-us/azure/devops/repos/git/gitworkflow?view=azure-devops) documentation.
    
* ### Create an "Azure Resource Manager" Service Connection
    
    * In order be able to deploy GMM resources through a pipeline we need to create a [Service Connection](https://docs.microsoft.com/en-us/azure/devops/pipelines/library/service-endpoints?view=azure-devops&tabs=yaml) and grant permissions to it.

    GMM provides a PowerShell script to accomplish this.
    
    1. Set-ServicePrincipalAndServiceConnection.ps1
    
        This script will create a new service principal and a service connection.  
        It takes these arguments `<SolutionAbbreviation>`,  `<EnvironmentAbbreviation>`, `<OrganizationName>`, "`<ProjectName>`.

        `<OrganizationName>` - This is the name of your organization used in Azure DevOps.  
        `<ProjectName>` - This is the name of the project in Azure DevOps we just created in a previous step.

        From your PowerShell command prompt navigate to the Scripts folder then type these commands:
            
            1. . ./Set-ServicePrincipalAndServiceConnection.ps1
            2. Set-ServicePrincipalAndServiceConnection -SolutionAbbreviation "<SolutionAbbreviation>"  `
						                              -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
						                              -OrganizationName "<OrganizationName>" `
						                              -ProjectName "<ProjectName>" `
						                              -Verbose
            
            Follow the instructions on the screen.                                          

        Locate the service connection name on the screen. It follows this naming convention: `<SolutionAbbreviation>`-serviceconnection-`<EnvironmentAbbreviation>`.
        
* ### Create a pipeline
    
    In Azure DevOps we need to create a pipeline that will create your resources and deploy your code.

    * See [Create your first pipeline](https://docs.microsoft.com/en-us/azure/devops/pipelines/create-first-pipeline?view=azure-devops&tabs=java%2Cyaml%2Cbrowser%2Ctfs-2018-2) documentation.
        1. On Azure DevOps left menu locate and clock on Pipelines.
        2. Click on 'Create Pipeline' or 'New Pipeline' depending on which one is presented to you.
        3. Select Azure Repos Git as your code location.
        4. Select the repository created in the previous step.
        5. From the list of options select 'Select an existing YAML file'    
        6. Select your branch
        7. Select '/vsts-cicd.yml' in the Path field.
        8. Click continue
        9. Run your pipeline.  

# Post-Deployment Tasks

 Once the pipeline has completed building and deploying GMM code and resources to your Azure resource groups, we need to make some final configuration changes.

 ### Grant SecurityGroup function access to the Queue and Topic

 Once your Function App `<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-SecurityGroup has been created we need to grant it access to the Queue and Topic.

    QueueName: membership
    TopicName: syncjobs
    FunctionAppName: <SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-SecurityGroup

    1. . ./Set-ServiceBusManagedIdentityRoles.ps1
    2. Set-ServiceBusManagedIdentityRoles  -SolutionAbbreviation "<SolutionAbbreviation>" `
                                        -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                        -FunctionAppName "<FunctionAppName>" `
                                        -QueueName "membership" `
                                        -TopicName "syncjobs" `
                                        -Verbose

## Generate demo data

If you are testing GMM in a demo tenant, you may want to generate demo data (users) in order to test GMM capabilities, we have provided a console application that will generate sample users for you.

* [Create a demo tenant for GMM](/Documentation/CreateDemoTenant/CreateDemoTenant.md)

Once your demo tenant is created, we suggest saving your demo tenant details in the prereqs keyvault.

We have provided a powershell script you could use to save your demo tenant details, Set-GmmDemoEnvironmentKeyVaultSecrets.ps1 which is located in the Scripts folder.

    1. . ./Set-GmmDemoEnvironmentKeyVaultSecrets.ps1
    2. Set-GmmDemoEnvironmentKeyVaultSecrets   -solutionAbbreviation "<SolutionAbbreviation>" `
                                        -environmentAbbreviation "<EnvironmentAbbreviation>" `
                                        -tenantName "<TenantName>" `
                                        -tenantAdminPassword "<TenantAdminPassword>" `
                                        -tenantAdminUsername  "<TenantAdminUsername>" 


The script will create these secrets in the prereqs keyvault:
* tenantName  
* tenantAdminUsername  
* tenantAdminPassword  

Note:  
If you decide to add these secret using the Azure Portal make sure to use the same secret names described above.

### Prerequisites

* Azure Account with permissions to register new applications and grant permissions.  
* An Azure application with permissions to create new users.

### Register a new Azure application

We need to register a new application capable of creating new users in your tenant to generate demo data.  

### Prerequisites

* Azure Account with permissions to register new applications and grant permissions.  
* An Azure application with permissions to create new users.

### Register a new Azure application

We need to register a new application capable of creating new users in your tenant to generate demo data.  
You could also leverage an existing one which has this permission Microsoft.Graph -> User.ReadWrite.All.

1. Register a new application using the Azure Portal see [Register an application with the Microsoft identity platform](
https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app) documentation.
2. Grant Delegated Microsoft.Graph -> User.ReadWrite.All permission. If you need more information on how to grant API permissions to your application see [Add permissions to access web APIs](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-configure-app-access-web-apis#add-permissions-to-access-web-apis) documentation.

### Setting up the console application

1. Using the file explorer or a command prompt navigate to where GMM code is located, from the root folder navigate to Service -> GroupMembershipManagement -> Repositories.Users.
2. Open Repositories.Users.csproj in Visual Studio.
3. Locate Settings.json, we need to make some changes to this file before running the program.  
    Notice it has several settings: ClientId, TenantId, TenantName, UserCount
    - ClientId - This the Application (client) Id of the application we created in the previous step. You will find this id under the Overview blade of your application on the Azure Portal.
    - TenandId - This is your tenant id, you can find this value under the Overview blade for Azure 'Active Directory'.
    - TenantName - This is the domain used on your users email addressses. i.e. "contoso.com, <MyDomain>.onmicrosoft.com"
    - UserCount - This is the number of users that will be generated. This also represent the number of users that will be read from the data.csv file.
4. Save your changes and run the application (You might need to set Repositories.Users as your startup project).
5. It will open a browser window asking you to login to they tenant where you want to generate the demo data.

### Note:
There is a data.csv file containing sample ids and email addresses (without the domain @domain.com), you can change this file to provide your own custom data set.
If a demo tenant is being used please be aware of the number of objects (Users, Groups, Applications, etc.) that can be created in a demo tenant's Active Directory which is approximately 37000 objects. When using the console application to create sample users make sure to generate only the users you need to not exceed this limit.

## Steps to debug and troubleshoot a failing sync

1. Find Logs in the Log analytics workspace following the instructions [here](/Documentation/FindLogEntriesInLogAnalyticsForASync.md)