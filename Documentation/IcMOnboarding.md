## Steps to onboard your Service to IcM

This document will guide you through the steps required to onboard your Service to IcM.

* Go to [Service Tree](http://aka.ms/servicetree) -> More -> Onboarding -> IcM
* Search for your Service -> Onboard New -> Provide the required information
    * Front End Service Category: End User Services Engineering
    * Datacenters or Regions: worldwide
    * Provide full email address for Triage Team, Incident Manager Team, Executive Incident Manager Team
* Click Submit and you should see status of your request changing to 'ProvisionedSuccess'

>Please note that you must be a member of the Admin Aliases role for your Service in order to perform the above steps.

* Go to [IcM Portal](https://portal.microsofticm.com/imp/v3/incidents/search/advanced)-> Administration -> Manage Teams
    * Provide the required details in 'Team Details' and make sure to set 'Allow Incident Assignment' to 'Everyone'
    * Add the alias of all the team members in 'Membership'. All IcM Teams must have atleast 2 members.
    * Set the Rotation, Schedule and On-Call Notifications in 'On-Call Config'. The rotation should have at least one on-call backup.
    * Make sure the Notification settings are set correctly in 'Notifications' tab.

* Auto Invite:
    * There are 2 Auto Invite Rules required for every service
    * Go to Administration -> Manage Services -> scroll to Auto Invite Management tab and add the following two rules:
    
    ```
        Severity Threshold: 2
        Environment: PROD
        Delay Before calling: 5 min
        Service Category: Core Platform Engineering
        Service: Response and Escalation Mgmt
        Team: EM MIM

        Severity Threshold: 2
        Environment: PROD
        Delay Before calling: 10 min
        Service Category: Core Platform Engineering
        Service: Response and Escalation Mgmt
        Team: EM DRI
    ```

    >Please note that you must be a member of the Administrator or Rule Manager role for your IcM tenant in order to add/modify/delete rules. You can check this by going to Administration -> Manage Services -> scroll to Role Members tab -> Role: TenantAdmin/RuleManagers

    ### References:        
    * [Onboarding to IcM](https://icmdocs.azurewebsites.net/onboarding/OnboardingSteps.html)
    * [IcM Compliance](https://microsoftit.visualstudio.com/OneITVSO/_wiki/wikis/OneITVSO.wiki/3040/IcM-Compliance)