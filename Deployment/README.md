## Single-Script deployment for GMM resources
Deploy GMM resources using Deploy-Resources.ps1 script.  
This script will deploy all resources in the specified environment with minimal user input.

### Prerequisites
- [Powershell 7.0](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-windows?view=powershell-7.4) or later is required to run the script.
- [Node 14.19.0](https://nodejs.org/dist/v14.19.0/)  
    - [Windows x32](https://nodejs.org/dist/v14.19.0/node-v14.19.0-x32.msi)
    - [Windows x64](https://nodejs.org/dist/v14.19.0/node-v14.19.0-x64.msi)    
    - [Others](https://nodejs.org/dist/v14.19.0/)
- [pnpm](https://pnpm.io/)
  - Once Node is installed, install pnpm using the following command.  
    ```npm install -g pnpm --global install```
- [Static Wb Apps CLI](https://azure.github.io/static-web-apps-cli/)
  - Once Node is installed, install SWA CLI using the following command.  
    ```npm install -g @azure/static-web-apps-cli   --global install```


### Deployment
The script [Deploy-Resources.ps1](Deploy-Resources.ps1) and the [parameters.json](parameters.json) files are located in the Deployment folder.  
Before running the script, make sure to update the parameters.json file with the required values.  
Once the parameters.json file is updated, run the following command to deploy the resources.
Powershell scripts might be blocked in your environment, to unblock the scripts run the following command in PowerShell 7.x from the root directory.  

```
Get-ChildItem -Recurse | Unblock-File
```

Run the following commands to deploy the resources with a Tenant Admin account.

```
    Connect-AzAccount

    Set-AzContext -SubscriptionId "<subscription-id>"

    . .\Deploy-Resources.ps1

    Deploy-Resources `
    -SolutionAbbreviation "<solution-abbreviation>" `
    -EnvironmentAbbreviation "<environment-abbreviation>" `
    -Location "<azure-region>" `
    -SubscriptionId "<subscription-id>" `
    -TemplateFilesDirectory "<absolute-directory-path-to-Deployment-directory>" `
    -ParameterFilePath "<absolute-path-to-parameters.json" `
    -Verbose
```

While most of the deployment is automated, you will be prompted to login again to your Azure account.

## Post Deployment
Once the deployment is complete, you will need to gran Admin consent to the deployed applications.
- Navigate to the Azure Portal and go to the Microsoft Entra ID.
- Click on App Registrations.
- Search for the deployed applications.
  - `<solution-abbreviation>-ui-<environment-abbreviation>`
  - `<solution-abbreviation>-webapi-<environment-abbreviation>`
  - `<solution-abbreviation>-Graph-<environment-abbreviation>-app`
- For each application, under Manage click on API Permissions then on 'Grant Admin consent for `<your-tenant>`'.

The WebApi provides roles that can be assigned to users. See these relevant sections:  
- [Roles as policy to gate functionality](https://github.com/microsoftgraph/group-membership-management/blob/main/Service/GroupMembershipManagement/Hosts/WebApi/Documentation/WebApiSetup.md#roles-as-policy-to-gate-functionality)
- [Add a role to a group](https://github.com/microsoftgraph/group-membership-management/blob/main/Service/GroupMembershipManagement/Hosts/WebApi/Documentation/WebApiSetup.md#add-a-role-to-a-group)

```
Deploy-Resources    -SolutionAbbreviation "<solution-abbreviation>" `
                    -EnvironmentAbbreviation "<environment-abbreviation>" `
                    -Location "<location>" `
                    -TemplateFilesDirectory "<template-file-path>" `
                    -ParameterFilePath "<parameter-file-path>" `
                    -SubscriptionId "<subscription-id>" `
                    -Verbose
```

### Creating and uploading the certificate

If you opted to use a certificate for the Microsoft Graph API `<solutionAbbreviation>-Graph-<environmentAbbreviation>`, follow these steps to complete the configuration.

1. Create a self-signed certificate. See [Quickstart: Set and retrieve a certificate from Azure Key Vault using the Azure portal](https://docs.microsoft.com/en-us/azure/key-vault/certificates/quick-create-portal)
2. Upload the certificate to your `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.

    We need to upload the certificate to the `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application, in order to do that, we need to export it from the prereqs keyvault.

    Exporting the certificate:

    1. In the Azure Portal navigate to your prereqs keyvault, it will be named following this convention `<solutionAbbreviation>`-prereqs-`<environmentAbbreviation>`.
    2. Locate and click on the Certificates blade on the left menu.
    3. Click on your certificate from the list.
    4. Click on the latest version.
    5. On the top menu click on 'Download in CER format' button to download the certificate.

    If you need more details on how to export the certificate please see [Quickstart: Set and retrieve a certificate from Azure Key Vault using the Azure portal](https://docs.microsoft.com/en-us/azure/key-vault/certificates/quick-create-portal) documentation.

    Uploading the certificate:

    1. In the Azure Portal navigate to Microsoft Entra ID. If you don't see it on your screen you can use the top search bar to locate it.
    2. Navigate to 'App registrations' blade on the left menu.
    3. Click on 'All applications" to locate and open your `<solutionAbbreviation>`-Graph-`<environmentAbbreviation>` application.
    4. On your application screen click on 'Certificates and secrets' blade on the left menu.
    5. Click on the 'Upload certificate' button.
    6. Locate and add your certificate.
