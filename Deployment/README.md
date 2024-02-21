Deploy GMM resources using Deploy-Resources.ps1 script.  
This script will deploy all resources in the specified environment.

The script and the parameters.json files are located in the Deployment folder.  
Before running the script, make sure to update the parameters.json file with the correct values.

Once the parameters.json file is updated, run the following command to deploy the resources.

PowerDhell scripts might be blocked in your environment.  
To unblock the scripts, run the following command in PowerShell from the root directory.  

```
Get-ChildItem -Recurse | Unblock-File
```

Run the following command to deploy the resources.

```
. .\Deploy-Resources.ps1

Deploy-Resources `
-SolutionAbbreviation "<solution-abbreviation>" `
-EnvironmentAbbreviation "<en>" `
-Location "" `
-TemplateFilePath "localTemplate.json" `
-ParameterFilePath "parameters.json" `
-Verbose
```