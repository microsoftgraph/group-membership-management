# Set sender address for email notification

* Add Mail.Send Delegated Permission in `<SolutionAbbreviation>-Graph-<EnvironmentAbbreviation>` application
* Enable 'Allow public client flows' in `<SolutionAbbreviation>-Graph-<EnvironmentAbbreviation>` application -> Authentication
* Create a new service account. This account will be used as the sender for emails sent out by the GMM application. Please note username & password of this user.
* Run [Set-SenderRecipientCredentials.ps1](/Scripts/Set-SenderRecipientCredentials.ps1) to store sender and secondary recipient information in prereqs keyvault

    * Please make sure that a prereqs keyvault exists in your environment
    * Open the script in Windows PowerShell ISE
    * Add the following lines to the the end before running the script:
        ```
        Set-SenderRecipientCredentials	-SubscriptionName "<SubscriptionName>" `
                                        -SolutionAbbreviation "<SolutionAbbreviation>" `
                                        -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                        -SenderUsername "<username-of-the-user-created-in-the-previous-step>" `
                                        -SenderPassword "<password-of-the-user-created-in-the-previous-step>" `
                                        -SyncCompletedCCEmailAddresses "<cc-email-address-when-sync-is-completed>" `
                                        -SyncDisabledCCEmailAddresses "<cc-email-address-when-sync-is-disabled>" `
                                        -Verbose
        ```
5) Assign the following two licenses to this user by going to [this](https://admin.microsoft.com/AdminPortal/Home#/licenses) link from your demo tenant page. You may have to unassign some licenses from other users to do this.

- Enterprise Mobility + Security E5
- Office 365 E5

>Note: Make sure that the user you created has a usage location (you can verify this by going to User Profile). Otherwise, you will not be able to assign the above licenses to this user.