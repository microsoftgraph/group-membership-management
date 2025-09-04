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
        [string]$ADFDBConnectionString
	)

	Write-Verbose "Set-PreDeploymentMigrations starting..."

    $ScriptsDirectory = Split-Path $PSScriptRoot -Parent
    $FunctionTemplatesPath = (Split-Path $ScriptsDirectory -Parent) + "\functions_arm_templates"

	. ($ScriptsDirectory + '\PreDeploymentMigrations\Remove-MultiLaneResources.ps1')
    . ($ScriptsDirectory + '\PreDeploymentMigrations\Start-FlexConsumptionMigration.ps1')

    Remove-MultiLaneResources `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SkipConfirmation

    Start-FlexConsumptionMigration `
        -FunctionTemplatesPath $FunctionTemplatesPath `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SyncJobsDBConnectionString $SyncJobsDBConnectionString `
        -ADFDBConnectionString $ADFDBConnectionString `
        -SkipConfirmation

	Write-Verbose "Set-PreDeploymentMigrations completed."
}