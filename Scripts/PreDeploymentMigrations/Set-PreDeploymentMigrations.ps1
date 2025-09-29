function Set-PreDeploymentMigrations {
	[CmdletBinding()]
	param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $false)]
        [string]$SyncJobsDBConnectionString,
        [Parameter(Mandatory = $false)]
        [string]$ADFDBConnectionString,
        [Parameter(Mandatory = $false)]
        [string]$ComputeResourcesArmTemplatePath
	)

	Write-Verbose "Set-PreDeploymentMigrations starting..."

    $ScriptsDirectory = Split-Path $PSScriptRoot -Parent

	. ($ScriptsDirectory + '\PreDeploymentMigrations\Remove-MultiLaneResources.ps1')
    . ($ScriptsDirectory + '\PreDeploymentMigrations\Start-FlexConsumptionMigration.ps1')

    Remove-MultiLaneResources `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SkipConfirmation

    if ([string]::IsNullOrEmpty($ComputeResourcesArmTemplatePath)) {
        $FunctionTemplatesPath = (Split-Path $ScriptsDirectory -Parent) + "\functions_arm_templates"
        Start-FlexConsumptionMigration `
            -FunctionTemplatesPath $FunctionTemplatesPath `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SyncJobsDBConnectionString $SyncJobsDBConnectionString `
            -ADFDBConnectionString $ADFDBConnectionString `
            -SkipConfirmation
    } else {
        Start-FlexConsumptionMigration `
            -ComputeResourcesArmTemplatePath $ComputeResourcesArmTemplatePath `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SyncJobsDBConnectionString $SyncJobsDBConnectionString `
            -ADFDBConnectionString $ADFDBConnectionString `
            -SkipConfirmation
    }


	Write-Verbose "Set-PreDeploymentMigrations completed."
}