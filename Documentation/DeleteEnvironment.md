## Tearing down the GMM environment
In some cases, you may want to reset your GMM environment. Here are the instructions on how to achieve this.

## Tear down using a script
Running this script will delete all resources from your resource groups. Make sure you are certain this is what you wish to do. 

From your `PowerShell 7.x` command prompt navigate to the `Scripts` folder of your `Public` repo and run these commands:

    1. . ./Delete-Environment.ps1
    2. Delete-Environment   -solutionAbbreviation "<solutionAbbreviation>" `
                            -environmentAbbreviation "<environmentAbbreviation>" `
                            -resourceGroupLocation "<resourceGroupLocation>" `

Where:
* `<resourceGroupLocation>` - the Azure location where the resources to be deleted are.


## Manual tear down 
1. Delete all resources from all your resource groups.
    - `<Solution-Abbreviation>`-data-`<Environment-abbreviation>`
    - `<Solution-Abbreviation>`-compute-`<Environment-abbreviation>`
    - `<Solution-Abbreviation>`-prereqs-`<Environment-abbreviation>`
1. Delete the previously mentioned resource groups.
1. Delete all keyvaults that are in soft delete state.
    ```
    Remove-AzKeyVault -VaultName <Solution-Abbreviation>-data-<Environment-abbreviation> -InRemovedState -Location <location>
    ```
    and
    ```
    Remove-AzKeyVault -VaultName <Solution-Abbreviation>-prereqs-<Environment-abbreviation> -InRemovedState -Location <location>

## Delete the app configuration store
_Note: The app configuration store resource cannot be purged from soft delete mode from a script._ 

It needs to be manually purged. Do this by following these steps:
1. Go to Azure Portal
1. Search for App Configuration
1. Click on 'Manage deleted stores'
1. Select the subscription where the app configuration store was
1. Select the desired app configuration store to remove and click on 'Purge'