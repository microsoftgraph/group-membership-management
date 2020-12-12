# FAQ
Q: What is the end user experience?  
A: The membership on the destination Microsoft 365 Group shows a flat list of members pulled in from all the source security groups.

Q: Can you tell by looking at a group that it is being managed by GMM?  
A: There is no indication to the end user that this group is being managed by GMM

Q: If a member were to be added or removed to the Microsoft 365 Group (say, via Outlook, Teams, Yammer, Azure portal etc), will these changes be overwritten the next time the GMM sync job runs?  
A: Yes, the updates will be overwritten by GMM. The changes have to me made to the source security groups for the changes to persist in the Microsoft 365 Group

Q: If my tenant has IB Policies, will this be taken into consideration by the GMM tool?  
A: GMM will continue to sync regardless of the IB policy. Admins can check if IB policy is enabled using cmdlet [Get-InformationBarrierPolicy](https://docs.microsoft.com/en-us/powershell/module/exchange/get-informationbarrierpolicy?view=exchange-ps)

Q: Is it possible to delete the owner of a group using this tool?  
A: No, this only maintains the members of the group and not owners. So, if John Doe is a member and owner of a group and we delete the member John Doe, John Doe will still be the owner of the group.

Q: Will the tool work with on-premise security groups?  
A: Yes if the sync between AAD security groups has been enabled.

Q: Is there any reporting to track what members were added or removed to groups?  
A: Log Analytics and AppInsights logs are available, but no separate reports are produced.

Q: Are there any restrictions in the number of groups to keep in sync?  
A: Typically, there is no strict restriction on how many groups to keep in sync. Recommend spreading the onboarding jobs if large groups need to be onboarded for the first time.

Q: Why I can't access my Key Vault secrets?  
A: Make sure that your user account was added to key vault access policies and it has the right permissions to see the key vault secrets.

1. In the [Azure Portal](https://portal.azure.com/) locate and open the key vault that you want to access.
2. In the key vault screen, click on the 'Access policies'
3. Locate and click on the 'Add Access Policy' button.
4. Select the permissions you would like to add to your user account (i.e. Secrets - Get, List, Set).
5. Select principal, this will be your user account.
6. Click on the 'Add' button.
7. Finally click on the 'Save' button on the top menu.

Q: Azure Functions are not able to access Key Vault secrets
A: Make sure the Azure Function was added to key vault access policies and it has the right permissions to see the key vault secrets.

1. In the [Azure Portal](https://portal.azure.com/) locate and open the key vault that you want to access.
2. In the key vault screen, click on the 'Access policies'
3. Locate and click on the 'Add Access Policy' button.
4. Select at least these Secrets permissions Get, List.
5. Select principal, search by the Azure Function name.
6. Click on the 'Add' button.
7. Finally click on the 'Save' button on the top menu.

Q: Azure Function `<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-SecurityGroup can not read/write from/to the service bus topic or queue
A: Make sure the Azure Function has the right role assigned 'Azure Service Bus Data Sender' for both the queue (membership) and the topic (syncjobs) it needs access to.

1. In the [Azure Portal](https://portal.azure.com/) locate and open the service bus, if will follow this naming convention `<SolutionAbbreviation>`-data-`<EnvironmentAbbreviation>`.
2. On the left menu navigate to:
    - Queues
    - Topics
3. Open the 'membership' queue or 'syncjobs' topic.
4. Click on 'Access control (IAM)' on the left menu.
5. Click on 'Role Assignments' button on the top menu.
6. If `<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-SecurityGroup is missing from role assignment list, it will need to be added.
7. To add `<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-SecurityGroup, click on the 'Add' button on the top menu, then 'Add role assignment'.
8. Fill in and save the form:
    - Role: Azure Service Bus Data Sender
    - Select: `<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-SecurityGroup

Note: remember to verify both the queue and the topic role assignments.

Q: GMM cannot read/write from/to the Microsoft Graph API
A: GMM creates an application `<SolutionAbbreviation>`-Graph-`<EnvironmentAbbreviation>` which requests the permissions to read and write from/to the Microsoft Graph API, those permissions need to be explicitely granted on the Azure Portal.

1. In the [Azure Portal](https://portal.azure.com/) locate and open 'Azure Active Directory'.
2. Click on 'App registrations' on the left menu.
3. Click on 'All applications'
4. Locate and open `<SolutionAbbreviation>`-Graph-`<EnvironmentAbbreviation>` application from the list.
5. Click on 'API Permissions'.
6. Make sure the application has the right Microsoft Graph permissions and that admin consent was granted.
    - Application permissions: GroupMember.Read.All, User.Read.All

In order to add permissions to the application:

-   On the 'API permissions' (step 5) screen, locate and click on 'Add a permission',
-   Click on 'Microsoft Graph'
-   Click on 'Application permissions'
-   Locate and check GroupMember.ReadWrite.All, User.Read.All permissions.
-   Click on 'Add permissions' button.
-   Click on 'Grant admin consent for `<organization-name>` button.

Q: GMM cannot update any onboarded group even with the right Microsoft Graph API permissions
A: Make sure to add `<SolutionAbbreviation>`-Graph-`<EnvironmentAbbreviation>` application as an owner to each group you want to manage.

1. In the [Azure Portal](https://portal.azure.com/) locate and open 'Azure Active Directory'.
2. Click on 'Groups' on the left menu.
3. From the list select the group you would like to manage.
4. Click on 'Owners' on the left menu.
5. Click on 'Add owners' button.
6. Search for `<SolutionAbbreviation>`-Graph-`<EnvironmentAbbreviation>` application.
7. Click on 'Select' button.

Q: How can I change the frequency of the JobTrigger function execution?
The JobTrigger function uses [NCRONTAB](https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-timer?tabs=csharp#ncrontab-expressions) expression to specify the running frequency, you can update this value directly in the Azure Portal in the Configuration blade of the Azure Function.

1. In the [Azure Portal](https://portal.azure.com/) locate and open the `<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-JobTrigger function. You can use the top search bar to locate it.
2. Click on the 'Configuration' blade on the left menu.
3. Under 'Application settings' locate the 'jobTriggerSchedule'
4. Edit and Save your changes.

This change will be overwritten next time a deployment occurs, in order to prevent this we need to change the ARM template that is creating the JobTrigger application settings.

To update the ARM template:

1. Locate the template.json file, you will find it under JobTrigger/Infrastructure/compute/ folder ![template.json](/Service/GroupMembershipManagement/Hosts/JobTrigger/Infrastructure/compute/template.json)
2. Locate and edit the 'jobTriggerSchedule' setting.
3. Save your changes.

Next time your code is deployed this change will be reflected on your JobTrigger function.
