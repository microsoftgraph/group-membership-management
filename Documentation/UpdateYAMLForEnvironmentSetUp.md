## Build/Release changes required for adding a new function app
This document will guide you through the changes required in Build/Release for adding a new function app.

* Add the following tasks in Build: [azure-pipelines.yml](https://microsoftit.visualstudio.com/OneITVSO/_git/EUS-ST-STSol-GMM-Source?path=%2Fazure-pipelines.yml&version=GBmaster&_a=contents):

        - task: DotNetCoreCLI@2
          displayName: 'dotnet publish <function-app-name>'
          inputs:
            command: publish
            arguments: '--configuration Release --output publish_output'
            projects: 'Service/GroupMembershipManagement/<function-app-project-solution-name>/*.csproj'
            publishWebProjects: false
            modifyOutputPath: false
            zipAfterPublish: false

        - task: ArchiveFiles@2
          displayName: "archive <function-app-name> files"
          inputs:
            rootFolderOrFile: "$(System.DefaultWorkingDirectory)/publish_output"
            includeRootFolder: false
            archiveFile: "$(System.DefaultWorkingDirectory)/<function-app-project-solution-name>.zip"

        - task: PublishBuildArtifacts@1
          displayName: 'publish <function-app-name> files'
          inputs:
            PathtoPublish: '$(System.DefaultWorkingDirectory)/<function-app-project-solution-name>.zip'
            ArtifactName: '$(Build.BuildNumber)'

* Append the name of new function app for each environment in Release: [vsts-cicd.yml](https://microsoftit.visualstudio.com/OneITVSO/_git/EUS-ST-STSol-GMM-Source?path=%2Fvsts-cicd.yml&version=GBmaster&_a=contents):

      functionApps:
      - name: 'JobTrigger'
      - name: 'GraphUpdater'
      - name: '<function-app-name>'