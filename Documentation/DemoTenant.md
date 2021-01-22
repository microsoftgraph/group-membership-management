# Setting GMM in a demo tenant

## Create `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` Azure Application

Set-GraphCredentialsAzureADApplication script can also be used to create the application on a different tenant that the one that has your keyvaults for testing purposes.

If you are creating the application on a different tenant from where your keyvaults are located the value provided for this parameters will be different:

-   TenantIdToCreateAppIn - Provide the Tenant Id of you demo tenant.
-   TenantIdWithKeyVault - Provide the Tenant Id where your keyvaults are located.

You can locate your Tenant Id in the [Azure Portal](http://portal.azure.com/), from the top search bar type 'Azure Active Directory' and click on it on the search results, your Tenant Id is located in the 'Overview' blade.

The script may ask you to login to your account via a popup window, this popup sometimes is displayed behind other windows that might be open at the time, if you don't see it, minimize your other open windows.

# Generate demo data

If you are testing GMM in a demo tenant, you may want to generate demo data (users) in order to test GMM capabilities, we have provided a console application that will generate sample users for you.

-   [Create a demo tenant for GMM](/Documentation/CreateDemoTenant/CreateDemoTenant.md)

Once your demo tenant is created, we suggest saving your demo tenant details in the prereqs keyvault.

We have provided a powershell script you could use to save your demo tenant details, Set-GmmDemoEnvironmentKeyVaultSecrets.ps1 which is located in the Scripts folder.

    1. . ./Set-GmmDemoEnvironmentKeyVaultSecrets.ps1
    2. Set-GmmDemoEnvironmentKeyVaultSecrets   -solutionAbbreviation "<SolutionAbbreviation>" `
                                        -environmentAbbreviation "<EnvironmentAbbreviation>" `
                                        -tenantName "<TenantName>" `
                                        -tenantAdminPassword "<TenantAdminPassword>" `
                                        -tenantAdminUsername  "<TenantAdminUsername>"

The script will create these secrets in the prereqs keyvault:

-   tenantName
-   tenantAdminUsername
-   tenantAdminPassword

Note:  
If you decide to add these secret using the Azure Portal make sure to use the same secret names described above.

### Prerequisites

-   Azure Account with permissions to register new applications and grant permissions.
-   An Azure application with permissions to create new users.

### Register a new Azure application

We need to register a new application capable of creating new users in your tenant to generate demo data.  
You could also leverage an existing one which has this permission Microsoft.Graph -> User.ReadWrite.All.

1. Register a new application using the Azure Portal see [Register an application with the Microsoft identity platform](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app) documentation.
2. Grant Delegated Microsoft.Graph -> User.ReadWrite.All permission. If you need more information on how to grant API permissions to your application see [Add permissions to access web APIs](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-configure-app-access-web-apis#add-permissions-to-access-web-apis) documentation.

### Setting up the console application

1. Using the file explorer or a command prompt navigate to where GMM code is located, from the root folder navigate to Service\GroupMembershipManagement\Hosts\Console\DemoUserSetup.
2. Open DemoUserSetup.sln in Visual Studio.
3. Locate Settings.json, we need to make some changes to this file before running the program.  
   Notice it has several settings: ClientId, TenantId, TenantName, UserCount
    - ClientId - This the Application (client) Id of the application we created in the previous step. You will find this id under the Overview blade of your application on the Azure Portal.
    - TenandId - This is your tenant id, you can find this value under the Overview blade for Azure 'Active Directory'.
    - TenantName - This is the domain used on your users email addressses. i.e. "contoso.com, <MyDomain>.onmicrosoft.com"
    - UserCount - This is the number of users that will be generated. This also represent the number of users that will be read from the data.csv file.
4. Save your changes and run the application (You might need to set DemoUserSetup as your startup project).
5. It will open a browser window asking you to login to they tenant where you want to generate the demo data.

### Note:

There is a data.csv file containing sample ids and email addresses (without the domain @domain.com), you can change this file to provide your own custom data set.
If a demo tenant is being used please be aware of the number of objects (Users, Groups, Applications, etc.) that can be created in a demo tenant's Active Directory which is approximately 37000 objects. When using the console application to create sample users make sure to generate only the users you need to not exceed this limit.