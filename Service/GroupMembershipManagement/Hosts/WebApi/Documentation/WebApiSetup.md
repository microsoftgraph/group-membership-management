# WebAPI Setup

WebAPI is used by GMM UI and other GMM Durable Dunctions to get information about the groups that are managed by GMM as well as their respective job definition.

## Create the WebAPI application and populate prereqs keyvault

The following PowerShell script will create a new application, `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` and will also save these settings in the prereqs keyvault:

-   webApiClientId
-   webApiTenantId
-   webApiClientSecret
-   webApiCertificateName

Note that this script will create an new application and authentication will be done using a client id and client secret pair. If you prefer to use a certificate you need to provide the name of your certificate which must be present on your prereqs keyvault.

From your `PowerShell 7.x` command prompt navigate to the `Service\GroupMembershipManagement\Hosts\WebApi\Scripts\` folder of your `Public` repo and run these commands:

    1.    . ./Set-WebApiAzureADApplication.ps1
    2.    Set-WebApiAzureADApplication	-SubscriptionName "<subscription-name>" `
                                        -SolutionAbbreviation "<solution-abbreviation>" `
                                        -EnvironmentAbbreviation "<environment-abbreviation>" `
                                        -AppTenantId "<app-tenant-id>" `
                                        -KeyVaultTenantId "<keyvault-tenant-id>" `
                                        -Clean $false `
                                        -Verbose
Follow the instructions on the screen.

Note:
AppTenantId <app-tenant-id> - If the application is going to be installed in a different tenant, set that tenant id here.
KeyVaultTenantId <keyvault-tenant-id> - This is the tenant where your GMM resources are located, i.e. keyvaults, storage account.

If you only have one tenant, these will be set to the same tenant id.

## Create an AAD Group for GMM Admins

Login and follow these steps in the tenant that was set in "AppTenantId" to run the Set-WebApiAzureADApplication.ps1 script in the previous step.

In order to control access to the WebAPI, several roles are created when the WebAPI application is created by the script below, 'Reader', 'Admin'.

1. Create a new Azure Active Directory Group or use an existing one.
2. Add members to the group, members of this group will act as GMM administrators. Administrators will have access to all jobs present in GMM and will be able to perform any CRUD action on them.

## Add Admin role to you GMM Admins group

Login and follow these steps in the tenant that was set in "AppTenantId" to run the Set-WebApiAzureADApplication.ps1 script in the previous step.

1. From the Azure Portal locate and open "Azure Active Directory"
2. On the left menu select "Enterprise Applications"
3. Search for the webapi application, the name follows this convention `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` i.e. gmm-webapi-int.
You might need to change the "Application Type" filter to "All Applications".
4. Click on the name of your application.
5. On the left menu select "Users and groups".
6. Click on "Add user/group"
7. Under "Users and groups", click on "None selected" and search for group created in the previous step and select it.
8. Under "Select a role" click on "None selected", from the roles list select "Admin"
9. Click "Assign"

Note: Individual users can be added and granted the proper permission, if you decide not to use a group.

## Grant Permissions

This step needs to be completed after all the resources have been deployed to your Azure tenant.

See [Post-Deployment tasks](../../../../../README.md#post-deployment-tasks)

Running the script mentioned in the Post-Deployment tasks section will grant the WebAPI system identity access to the resources it needs.

To properly setup the WebAPI you will need to configure the parameters in the `WebApi/Infrastructure/compute/parameters` for your environment.
If you have a custom domain, follow the instructions [here](WebApiSetup.md/#setting-up-a-custom-domain). If not, skip on to the instructions [here](WebApiSetup.md/#using-the-default).

### Grant access to the SQL Server Database

WebAPI will access the database using its system identity to authenticate with the database to prevent the use of credentials.

Once the WebAPI is deployed (`<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-webapi`)and has been created we need to grant it access to the SQL Server DB.

Server name follows this naming convention `<SolutionAbbreviation>-data-<EnvironmentAbbreviation>` and `<SolutionAbbreviation>-data-<EnvironmentAbbreviation>-r` for the replica server.
Database name follows this naming convention `<SolutionAbbreviation>-data-<EnvironmentAbbreviation>-jobs` and `<SolutionAbbreviation>-data-<EnvironmentAbbreviation>-jobs-r` for the replica database.

1. Connect to your SQL Server Database using Sql Server Management Studio (SSMS) or Azure Data Studio.
- Server name : `<server-name>.database.windows.net`
- User name: Use your Azure account.
- Authentication: Azure Active Directory - Universal with MFA
- Database name: `<database-name>`

2. Run these SQL command

- This script needs to run only once per database.
- Make sure you are connected to right database. Sometimes SSMS will default to the master database.

```
IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = N'<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-webapi')
BEGIN
 CREATE USER [<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-webapi] FROM EXTERNAL PROVIDER;
 ALTER ROLE db_datareader ADD MEMBER [<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-webapi];
 ALTER ROLE db_datawriter ADD MEMBER [<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-webapi];
END
```

Verify it ran successufully by running:
```
SELECT * FROM sys.database_principals WHERE name = N'<SolutionAbbreviation>-compute-<EnvironmentAbbreviation>-webapi'
```
You should see one record for your webapi app.
Repeat the steps for both databases.

## Setting up a custom domain
If you have a custom domain ('contoso.com', for example) and want to use it, you will need to upgrade your App Service Plan. You can set the API custom domain in the `apiHostname` parameter as `api.contoso.com`.
This way, your parameter file will look like this 
```
    {
        "$schema": "https://schema.management.azure.com/schemas/2015-01-01/deploymentParameters.json#",
        "contentVersion": "1.0.0.0",
        "parameters": {
            "apiHostname": {
                "value": "api.contoso.com"
            }
        }
    }
```
Then, once the deployment finishes, follow these steps:

1. Go to your App Service resource and select `Custom domains`.
1. There, click on `+ Add custom domain` and enter your custom domain details. You can choose to include your own TLS/SSL certificate, or use an App Service Managed Certificate. 
1. Once you have completed and validated the custom domain. Click `Add`. 

Finally, you will need to update your App registration to include this custom domain. To do so, make sure you:
1. Login and follow these steps in the tenant where the application was created.
1. From the Azure Portal locate and open "Azure Active Directory"
1. On the left menu, select "App registrations"
1. Search for the webapi application, the name follows this convention `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` i.e. gmm-webapi-int.
1. Click on the name of your application.
1. On the left menu, select "Expose an API"
1. In the Application ID URI, set your custom domain here. i.e. `api://api.contoso.com`.

## Using the default domain
If you do not wish to set up a custom domain, you can leverage the one included in the F1 service plan, remove the `apiHostname` parameter file to use the default `<solutionAbbreviation>-<resourceGroupClassification>-<solutionAbbreviation>-webapi.azurewebsites.net`, for example