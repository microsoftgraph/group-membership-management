# Set sender address for email notification

Follow the steps below to set the sender's credentials that will be used to send email notifications. If you don't want to use email notifications see [Skip email notifications](#skip-email-notifications).

1) Open the Azure Portal from the tenant where the Graph App is created in
2) Add Mail.Send Delegated Permission in `<SolutionAbbreviation>-Graph-<EnvironmentAbbreviation>` application

    a. **If you are in an environment that requires MFA for all service accounts**, then add the Mail.Send Application Permission instead (Delegated Mail.Send does not work when MFA is required on the sender account).

3) Enable 'Allow public client flows' in `<SolutionAbbreviation>-Graph-<EnvironmentAbbreviation>` application -> Authentication
4) Create a new service account:

    * This can be done by creating a new user from the tenant where the Graph App is created in
    * Make sure the user has a usage location set
    * This account will be used as the sender address for email notifications sent out by the GMM application
    * Please note username & password of this user.

5) Run [Set-SenderRecipientCredentials.ps1](/Scripts/Set-SenderRecipientCredentials.ps1) to store sender and secondary recipient information in prereqs keyvault

    * Please make sure that a prereqs keyvault exists in your environment
    * If using Application permissions for Mail.Send, please make sure that an App Configuration resource exists in your environment
    * Open the script in Windows PowerShell ISE
    * Set the CC Email Address to whatever email you want to be notified at
    * Add the following lines to the the end before running the script:
        ```
        $secureSenderUsername = ConvertTo-SecureString -AsPlainText -Force "<username-of-the-user-created-in-the-previous-step>"
        $secureSecurePassword = ConvertTo-SecureString -AsPlainText -Force "<password-of-the-user-created-in-the-previous-step>"
        $secureSyncCompletedCCEmailAddresses = ConvertTo-SecureString -AsPlainText -Force "<cc email addresses when sync is completed>"
        $secureSyncDisabledCCEmailAddresses = ConvertTo-SecureString -AsPlainText -Force "<cc email addresses when sync is disabled>"
        $secureSupportEmailAddresses = ConvertTo-SecureString -AsPlainText -Force "<cc email addresses when sync is disabled>"

        Set-SenderRecipientCredentials	-SubscriptionName "<SubscriptionName>" `
                                        -SolutionAbbreviation "<SolutionAbbreviation>" `
                                        -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                        -SecureSenderUsername $secureSenderUsername `
                                        -SecureSenderPassword $secureSecurePassword `
                                        -SecureSyncCompletedCCEmailAddresses $secureSyncCompletedCCEmailAddresses `
                                        -SecureSyncDisabledCCEmailAddresses $secureSyncDisabledCCEmailAddresses `
                                        -SecureSupportEmailAddresses $secureSupportEmailAddresses `
                                        -GmmGraphAppHasMailApplicationPermissions $false `
                                        -Verbose
        ```

>Note: Make sure that **if you added Mail.Send Application permissions** to your GMM Graph App, that you set the GmmGraphAppHasMailApplicationPermissions parameter in the Set-SenderReceipientCredentials script to $true!

6) Assign the following two licenses to this user by going to [this](https://admin.microsoft.com/AdminPortal/Home#/licenses) link from your demo tenant page. You may have to unassign some licenses from other users to do this.

- Enterprise Mobility + Security E5
- Office 365 E5

>Note: Make sure that the user you created has a usage location (you can verify this by going to User Profile). Otherwise, you will not be able to assign the above licenses to this user.


## Checking Setup for MFA Sender Account

1) You should have added the Mail.Send Application Permission to the GMM Graph App from Step 2 above (Delegated Mail.Send does not work when MFA is required on the sender account).

2) Go to your App Configuration resource and make sure you now have this key-value pair:

    * key = "Mail:IsMailApplicationPermissionGranted"
    * value = "true"

3) If the key-value pair above does not exist (maybe the Set-SenderReceipientCredentials script failed at the end) then add it

## Skip email notifications

If you decide not to use email notifications follow these steps. From Powershell 7 command prompt run:

```
 . ./Set-SenderRecipientCredentials.ps1

 Skip-SenderRecipientCredentials `
    -SubscriptionName "<SubscriptionName>" `
    -SolutionAbbreviation "<SolutionAbbreviation>" `
    -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
```

The script will populate default values for sender's username and password required by GMM deployment to succeed, and it also indicates GMM to not send any email notifications by setting Mail:SkipMailNotifications to true in App Configuration settings.

If at a later time you decide to use email notifications you will need to follow steps from [Set sender address for email notification](#set-sender-address-for-email-notification). Keep in mind that re-running these scripts creates a new version of the senderUsername and senderPassword secrets in your prereqs keyvault. You will need to update the Notifier's function settings to use the latest version of the secrets.

Notifier's function sender credentials settings that need to be updated:
```
"name": "senderAddress",  
"value": "@Microsoft.KeyVault(SecretUri=https://<solutionAbbreviation>-prereqs-<environmentAbbreviation>.vault.azure.net/secrets/senderUsername/<version>)"
  
"name": "senderPassword",  
"value": "@Microsoft.KeyVault(SecretUri=https://<solutionAbbreviation>-prereqs-<environmentAbbreviation>.vault.azure.net/secrets/senderPassword/<version>)"
```

`<version>` - Needs to be replaced with the latest version for both secrets in the prereqs keyvault.