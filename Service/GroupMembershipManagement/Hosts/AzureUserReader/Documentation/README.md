# AzureUserReader Durable Function

AzureUserReader is an optional durable function provided to help retrieving user's Azure ObjectId, this is a common task while synchronizing users to Azure Groups via custom data sources, given that some companies use the Azure User's onPremisesImmutableIds attribute as their employee id, this function can be integrated in a custom solution where it will translate the onPremisesImmutableIds to and Azure User ObjectId, the way it works is given a file with a list of onPremisesImmutableIds the function will retrieve the objectId's from Microsoft Graph API and cache those to minimize subsequent queries. Your custom solution should be able to use these Azure Object Ids to syncrhonize your groups.

## Prerequisites
1.	Azure Storage account where blobs are going to be stored.
2.	Azure KeyVault.
3.	Log Analytics account.
4.	Azure Application with permissions to read from Microsoft Graph API.

## Setup

Once GMM is setup you should have all the resources required by this function to run.

1. Azure Storage Account  
 This is the storage account that AzureUserReader function will use to pick up the source file and write to a destination file. Note that the source file needs to be generated from your custom data source and stored in this storage account.
 Once you have identified the storage account you would like to use, take a note of its connection string as we will need to add it to the keyvault in the next step.

2. Azure KeyVault  
 This is the keyvault containing the secret that holds the connection string of the Azure Storage Account. 
 The ARM templates creating this function are referencing `<SolutionAbbreviation>`-data-`<EnvironmentAbbreviation>` keyvault.
   - Locate the connection string from step one and create a new keyvault secret, the name of the secret can be anything of your choice (i.e. myNewConnectionString) and the value of the secret will be the connection string.   
   - Update the parameter file for your enviroment which can be located at AzureUserReader/Infrastructure/compute/parameters/parameter.`<environment>`.json, open the file and locate the parameter "storageAccountSecretName" replace its value’s placeholder with the secret's name you just created (i.e. myNewConnectionString).

3. Log Analytics  
 This resource is created as part of GMM setup, it should be already available for use.
 The function will read Log Analytics settings logAnalyticsCustomerId and logAnalyticsPrimarySharedKey from the `<SolutionAbbreviation>`-data-`<EnvironmentAbbreviation>` keyvault.

4. Azure Application with Microsoft API Graph API permissions  
 This application should be already created, see "Create <solutionAbbreviation>-Graph-<environmentAbbreviation> Azure Application" section in the main GMM README.md for more information.
 
 ### YAML Pipeline

You can add or remove this function to/from vsts-cicd.yml file. Make sure to add it or remove it from all the tasks, similar to the other functions the entry for this one will look like this:

\- name: 'AzureUserReader'

## How to use AzureUserReader

This is an HTTP triggered function accepting only POST requests.

In order to call the function you will need to have:
- Function Url
- Function Key
- Request Body

### Input
Url  
You can find the exact url in the Azure Portal, it will have this format:  
https://`<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`.azureuserreader.azurewebsites.net/api/StarterFunction

Header  
This header must be provided to authenticate with your function.  
`x-functions-key` : `<your-function-key>`

Request Body
```
{ 
    "ContainerName":"`<container-name>`",
    "BlobPath":"`<path/to/file.csv>`"
}
```

Request body is a JSON object with the container name that contains your blob and the path to the blob. The expected format of the file is a single column:

Header (must be provided)  
Content

i.e.

PersonnelNumbers  
1111  
2222  
3333  
4444  
5555  

### Output

Once the function has completed processing it will create a file called memberids.csv in the same location as the source file, it will have two columns:

PersonnelNumber,AzureObjectId  
1111,f3c8cd07-923c-4c22-89a8-594383b85ba7  
2222,212e7ac6-f0ff-4dd2-aa62-717958b16e32  
3333,ca55dcae-ae35-4b2f-bfaf-9093936524ca  

Note that the file will only have values for those PersonnelNumbers that have a match in Microsoft Graph API.