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
                                        -TenantId "<app-tenant-id>" `
                                        -Clean $false `
                                        -Verbose
Follow the instructions on the screen.

## Create an AAD Group for GMM Admins
In order to control access to the WebAPI, several roles are created when the WebAPI application is created by the script below, 'Reader', 'Admin'.

1. Create a new Azure Active Directory Group or use an existing one.
2. Add members to the group, members of this group will act as GMM administrators. Administrators will have access to all jobs present in GMM and will be able to perform any CRUD action on them.

## Add Admin role to you GMM Admins group

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

## Grant Permissions

This step needs to be completed after all the resources have beend deployed to your Azure tenant.

See [Post-Deployment tasks](../../../../../README.md#post-deployment-tasks)

Running the script mentioned in the Post-Deployment tasks section will grant the WebAPI system identity access to the resources it needs.