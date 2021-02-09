# Set sender address for email notification in non prod environments

* Add Mail.Send Delegated Permission in `gmm-Graph-<env>` application
* Enable 'Allow public client flows' in `gmm-Graph-<env>` application -> Authentication
* Create a test user in your demo tenant. This test user will be set as sender for email notifications. Please note username & password of the test user.
* Run [Set-SenderRecipientCredentials.ps1](/Scripts/Set-SenderRecipientCredentials.ps1) to store sender and secondary recipient information in prereqs keyvault

    * Please make sure that a prereqs keyvault exists in your environment
    * Open the script in Windows PowerShell ISE
    * Add the following lines to the the end before running the script:
        ```
        Set-SenderRecipientCredentials	-SubscriptionName "<SubscriptionName>" `
                                        -SolutionAbbreviation "gmm" `
                                        -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                        -SenderUsername "<username-of-the-test-user>" `
                                        -SenderPassword "<password-of-the-test-user>" `
                                        -SyncCompletedCCEmailAddresses "<cc-email-address-when-sync-is-completed>" `
                                        -SyncDisabledCCEmailAddresses "<cc-email-address-when-sync-is-disabled>" `
                                        -Verbose
        ```
5) Assign the following two licenses to this user by going to [this](https://admin.microsoft.com/AdminPortal/Home#/licenses) link from your demo tenant page :

- Enterprise Mobility + Security E5
- Office 365 E5

>Note: Make sure that the test user you created has a usage location (you can verify this by going to User Profile). Otherwise, you will not be able to assign the above licenses to this user.