## Setting up a "Demo" Tenant
This document will guide you through the process of configuring a demo tenant for use as a GMM development environment.

# Create a demo tenant via Microsoft 365 Dev Center

* Navigate to: [Microsoft 365 Dev Center](https://developer.microsoft.com/en-us/microsoft-365/dev-program) and click 'Join now'

* Fill the details required to join the Microsoft 365 Developer Program and click 'Save'

* Click 'Start' to set up a new Microsoft 365 developer sandbox

* Choose your Microsoft 365 E5 developer sandbox - 'Instant sandbox' or 'Configurable sandbox'

* Fill the details required to set up your Microsoft 365 E5 sandbox and click 'Save'

* Choose the Visual Studio subscription that you want to link to your Microsoft 365 developer sandbox. When you link your developer sandbox with Visual Studio, you qualify for a sandbox that renews automatically for as long as your Visual Studio subscription is active.

* Click 'Set up'

* You should see the Microsoft 365 Developer Program Dashboard and you can just setup a new Subscription.

# Create a demo tenant via Microsoft Customer Digital Experiences

* Navigate to: [Microsoft Demos](https://demos.microsoft.com) or [CDX](https://cdx.transform.microsoft.com/)

* Click "My Environments" & ensure you are signed in with your MSFT domain account

![MicrosoftDemoTenant](/Documentation/CreateDemoTenant/Images/MicrosoftDemosPage.png)

* Click "Create Tenant" as shown below to open the menu

![CreateNewTenantPage](/Documentation/CreateDemoTenant/Images/CreateNewTenantPage.png)

* Select "Quick Tenant", "1 Year" and "North America".

* Click "Create Tenant" on the "Microsoft 365 Enterprise with Users and No Content" card

![Office365TenantChoices](/Documentation/CreateDemoTenant/Images/O365TenantChoices.png)


* Click **Agree**

* Note: Since the tenant expires in 1 year, make sure you add a reminder in outlook to remind you to submit a request to ensure your tenant is eligible for extension


# Setting up users in your demo tenant

1. Using the file explorer or a command prompt navigate to where GMM code is located, from the root folder navigate to Service\GroupMembershipManagement\Hosts\Console\DemoUserSetup.
2. Open DemoUserSetup.sln in Visual Studio.
3. Locate Settings.json, we need to make some changes to this file before running the program.  
   Notice it has several settings: ClientId, TenantId, TenantName, UserCount
    - ClientId - This the Application (client) Id of the application we created in the previous step. You will find this id under the Overview blade of your application on the Azure Portal.
    - TenandId - This is your tenant id, you can find this value under the Overview blade for Azure 'Active Directory'.
    - TenantName - This is the domain used on your users' email addresses. i.e. "contoso.com, <MyDomain>.onmicrosoft.com"
    - UserCount - This is the number of users that will be generated. This also represent the number of users that will be read from the data.csv file.
4. Save your changes and run the application (You might need to set DemoUserSetup as your startup project).
5. It will open a browser window asking you to login to they tenant where you want to generate the demo data.

### Note:

There is a data.csv file containing sample ids and email addresses (without the domain @domain.com), you can change this file to provide your own custom data set.
If a demo tenant is being used please be aware of the number of objects (Users, Groups, Applications, etc.) that can be created in a demo tenant's Active Directory which is approximately 37000 objects. When using the console application to create sample users make sure to generate only the users you need to not exceed this limit.
