# Breaking Changes

## 10/27/2021
## GMM now uses an application secret instead of a certificate 

We have updated `Set-GraphCredentialsAzureADApplication.ps1` script to generate and store a client secret when creating `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.

`graphAppCertificateName` which held the name of the certificate has been removed.  
`graphAppClientSecret` has been added, the script will automatically create and populate it in the `prereqs` keyvault, and add it it to `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.

In order to update GMM to use the application secret we need to:

1. Create a new client secret for `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.
2. Add it to the prereqs keyvault.
3. Deploy GMM code.
4. Remove certificate.

There are a couple of ways to accomplish these tasks:

1. Running Set-GraphCredentialsAzureADApplication.ps1 script will take care of step 1 and 2 described above.
2. Or, manually creating the application secret and storing it in the prereqs keyvault.

### Running the script
From your PowerShell command prompt navigate to the Scripts folder then type these commands:
```
1. . ./Set-GraphCredentialsAzureADApplication.ps1
2. Set-GraphCredentialsAzureADApplication	-SubscriptionName "<SubscriptionName>" `
                                            -SolutionAbbreviation "<SolutionAbbreviation>" `
                                            -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                                            -TenantIdToCreateAppIn "<TenantId>" `
                                            -TenantIdWithKeyVault "<TenantId>" `
                                            -Verbose
```
Follow the instructions on the screen.

### Manual steps to create and store the application secret

Creating the application secret

1. In the Azure Portal navigate to your 'Azure Active Directory'. If you don't see it on your screen you can use the top search bar to locate it.
2. Navigate to 'App registrations' blade on the left menu.
3. Click on 'All applications" to locate and open your `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.
4. On your application screen click on 'Certificates & secrets' blade on the left menu.
5. Click on the 'Client secrets()' tabular menu.
6. Click on 'New client secret', provide a description, expiration finally click on 'Add.

Copy the new secret since this is the only time it will be available and we need ti store it in the prereqs keyvault.

Storing the application secret in the prereqs keyvault

1. In the Azure Portal navigate to your 'Key vaults'. If you don't see it on your screen you can use the top search bar to locate it.
2. Locate and open `<solutionAbbreviation>`-prereqs-`<environmentAbbreviation>` keyvault.
3. Click on 'Secrets' blade on the left menu.
4. Click on 'Generate/Import' button.
5. Provide `graphAppClientSecret` as 'Name', and the new secret created in the previous section as 'Value'.
6. Click on 'Create' button.

### Deploy latest GMM code
Once the new secret is generated and stored in the keyvault, you can proceed to deploy the latest GMM code to your environments.

### Delete application certificate
1. In the Azure Portal navigate to your 'Azure Active Directory'. If you don't see it on your screen you can use the top search bar to locate it.
2. Navigate to 'App registrations' blade on the left menu.
3. Click on 'All applications" to locate and open your `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.
4. On your application screen click on 'Certificates and secrets' blade on the left menu.
5. Click on the 'Delete' button. (blue icon next to Certificate ID).
6. Locate and add your certificate.

For more information about `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application see section [Create `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` Azure Application](README.md#populate-prereqs-keyvault)

## 3/28/2022
### Send group membership via blobs instead of queue

GMM has been updated to send group membership through blobs instead of queues. So the 'membership' queue has been removed from the ARM templates and is not longer used by the code.

See section [Grant SecurityGroup, GraphUpdater function access to storage account](README.md#grant-securitygroup-graphupdater-function-access-to-storage-account) for more information.

Once these changes are deployed successfully to your enviroment it will be safe to delete the 'membership' queue from your Azure Resources.