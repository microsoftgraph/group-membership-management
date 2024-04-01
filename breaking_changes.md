# Breaking Changes

## 5/1/2024

Local auth has been disabled for App Configuration resource.
Going forward, the service connection used to deploy GMM resources must have "Azure App Configuration Data Owner" RBAC permission, before deploying this version.

To grant the permission run Set-ServicePrincipalManagedIdentityRoles script.
```
1. . .\Set-ServicePrincipalManagedIdentityRoles.ps1
2. Set-ServicePrincipalManagedIdentityRoles -solutionAbbreviation <solution> -environmentAbbreviation <env>
```
Reference:
https://github.com/Azure/AppConfiguration/issues/692#issuecomment-1991914653

## 2/26/2024
SyncJobs with SqlMembership source part need to be updated with a new JSON schema:

Old format
```
[
  {
    "type": "SqlMembership",
    "sources":
     [
       {
         "ids": [1,2,3],
         "depth": 2,
         "filter": "filter statement"
       }
     ]
  }
]
```

New format
```
[
  {
    "type": "SqlMembership",
    "source": {
      "manager": {
        "id": 1,
        "depth": 2
      },
      "filter": "filter statement"
    }
  },
  {
    "type": "SqlMembership",
    "source": {
      "manager": {
        "id": 2,
        "depth": 2
      },
      "filter": "filter statement"
    }
  },
  {
    "type": "SqlMembership",
    "source": {
      "manager": {
        "id": 3,
        "depth": 2
      },
      "filter": "filter statement"
    }
  }
]
```

With this new schema, only one manager id is supported per source part.

A powershell script has been added to help convert all existing SqlMembership jobs to the new format.
Script can be found here [Set-UpdateSqlMembershipQuery.ps1](
https://github.com/microsoftgraph/group-membership-management/tree/main/Service/GroupMembershipManagement/Hosts/SqlMembershipObtainer/Scripts/Set-UpdateSqlMembershipQuery.ps1).

    1. . ./Set-UpdateSqlMembershipQuery.ps1
    2. Set-UpdateSqlMembershipQuery -ConnectionString "<sqlDatabaseConnectionString>"

## 8/1/2023
SecurityGroup function has been renamed to GroupMembershipObtainer.
SecurityGroup service bus topic has been renamed to GroupMembership.

SyncJob must set the type to GroupMembership in their query.
```
[
    {
        "type": "GroupMembership",
        "source": "<guid-group-objet-id-1>"
    }
]
```

Existing jobs will need to be updated to use the new type.
To manually update the type, you can use the following T-SQL statement.
```
UPDATE SyncJobs SET Query = REPLACE(Query, 'SecurityGroup', 'GroupMembership') WHERE Query LIKE '%SecurityGroup%'
```
Once the renamed function is deployed, you can remove the old "SecurityGroup" function.

## 7/26/2023
### Create the jobs table in SQL database

* Go to https://`<solutionAbbreviation>`-compute-`<environmentAbbreviation>`-webapi.azurewebsites.net/swagger/index.html
* Hit the endpoint `admin/databaseMigration`. This will create the jobs table in `<solutionAbbreviation>`-data-`<environmentAbbreviation>`-jobs database
* A successful deployment to your environment will copy the jobs from storage account to sql database

## 11/23/2022

### Updated keyVaultReaders_nonprod and keyVaultReaders_prod JSON schema

See section [Create an Azure DevOps pipeline](README.md#create-an-azure-devops-pipeline) for more information.

Previously only one set of permissions were supported, these were assigned to KeyVault 'secrets'.  
With this change it now supports KeyVault 'secrets' and 'certificates' permissions.

Old schema:
```
[
    {
        "objectId": "<user-object-id>",
        "permissions": [ "get", "set", "list" ]
    }
]
```
New schema:
```
[
    {
        "objectId": "<user-object-id>",
        "secrets": [ "get", "set", "list" ],
        "certificates": [ "get", "set", "list" ]
    }
]
```

If no certificate permissions are needed, you can omit the 'certificates' property.
```
[
    {
        "objectId": "<user-object-id>",
        "secrets": [ "get", "set", "list" ]
    }
]
```

Existing GMM pipelines will need to update these variables:
- keyVaultReaders_nonprod
- keyVaultReaders_prod

## 09/22/2022

### - SecurityGroup query format has changed to JSON
SecurityGroup query format has been updated to provide a single way to specify hybrid sync jobs.

Previous query format sample.
```
[
    {
        "type": "SecurityGroup",
        "sources":
        [
            "a167b6c1-a2b3-4e16-aa8b-0ad0de5f44d9",
            "04a8c19e-96a4-4570-b946-befd5bedca0e"
        ]
    }
]
```
New query format sample:
```
[
    {
        "type": "SecurityGroup",
        "source": "a167b6c1-a2b3-4e16-aa8b-0ad0de5f44d9"
    },
    {
        "type": "SecurityGroup",
        "source": "04a8c19e-96a4-4570-b946-befd5bedca0e"
    }
]
```

A powershell script has been added to help convert all existing SecurityGroup jobs to the new format.
Script can be found here [Set-UpdateSecurityGroupQuery.ps1](
Service\GroupMembershipManagement\Hosts\SecurityGroup\Scripts\Set-UpdateSecurityGroupQuery.ps1).

    1. . ./Set-UpdateSecurityGroupQuery.ps1
    2. Set-UpdateSecurityGroupQuery	-SubscriptionName "<SubscriptionName>" `
                                    -SolutionAbbreviation "<SolutionAbbreviation>" `
							        -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
							        -Verbose


## 05/02/2022

### - SecurityGroup query format has changed to JSON
SecurityGroup query format has been updated in order to support hybrid sync jobs. List of semicolon separated group ids list has been replaced by a JSON query.

Previous query format sample, list of group ids separated by semicolon.  
```
a167b6c1-a2b3-4e16-aa8b-0ad0de5f44d9;04a8c19e-96a4-4570-b946-befd5bedca0e
```

New query format sample:
```
[
    {
        "type": "SecurityGroup",
        "sources":
        [
            "a167b6c1-a2b3-4e16-aa8b-0ad0de5f44d9",
            "04a8c19e-96a4-4570-b946-befd5bedca0e"
        ]
    }
]
```

A powershell script has been added to help convert all existing SecurityGroup jobs to the new format.
Script can be found here [Set-UpdateSecurityGroupQuery.ps1](
Service\GroupMembershipManagement\Hosts\SecurityGroup\Scripts\Set-UpdateSecurityGroupQuery.ps1).

    1. . ./Set-UpdateSecurityGroupQuery.ps1
    2. Set-UpdateSecurityGroupQuery	-SubscriptionName "<SubscriptionName>" `
                                    -SolutionAbbreviation "<SolutionAbbreviation>" `
							        -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
							        -Verbose

### - Type field has been removed.

## 3/28/2022
### Send group membership via blobs instead of queue

GMM has been updated to send group membership through blobs instead of queues. So the 'membership' queue has been removed from the ARM templates and is not longer used by the code.

See section [Grant SecurityGroup, GraphUpdater function access to storage account](README.md#grant-securitygroup-graphupdater-function-access-to-storage-account) for more information.

Once these changes are deployed successfully to your enviroment it will be safe to delete the 'membership' queue from your Azure Resources.

## 10/27/2021
### GMM now uses an application secret instead of a certificate 

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
