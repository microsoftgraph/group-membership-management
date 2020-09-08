Param(
    [Parameter(Mandatory=$True)]    
    [string] $resourceGroupName,
    [Parameter(Mandatory=$True)]
    [string] $storageAccountName,
    [string] $storageAccountSku = "Standard_LRS",
    [string] $storageAccountLocation = "West US 2",
    [string] $storageAccountKind = "StorageV2",
    [Parameter(Mandatory=$True)]
    [string] $storageContainerName,
    [Parameter(Mandatory=$True)]
    [string] $sourceFolderPath,
    [Parameter(Mandatory=$False)]
    [string] $targetFolderPathPrefix
)

$storageAccount = Get-AzStorageAccount -ResourceGroupName $resourceGroupName -Name $storageAccountName -ErrorAction SilentlyContinue
if ($null -eq $storageAccount) {    
    $storageAccount = New-AzStorageAccount -ResourceGroupName $resourceGroupName `
                        -Name $storageAccountName `
                        -SkuName $storageAccountSku `
                        -Kind $storageAccountKind `
                        -Location $storageAccountLocation `
                        -EnableHttpsTrafficOnly $true
}

# Wait for the account to be ready
while ($null -eq ($storageAccount = Get-AzStorageAccount -ResourceGroupName $resourceGroupName -Name $storageAccountName -ErrorAction SilentlyContinue)) {
    Start-Sleep -Seconds 10
}

$container = Get-AzStorageContainer -Name $storageContainerName -Context $storageAccount.Context -ErrorAction SilentlyContinue
if ($null -eq $container) {
    $container = New-AzStorageContainer -Name $storageContainerName -Context $storageAccount.Context
}

# Wait for the container to be ready
while ($null -eq  ($container = Get-AzStorageContainer -Name $storageContainerName -Context $storageAccount.Context -ErrorAction SilentlyContinue)) {
    Start-Sleep -Seconds 10
}

if ($sourceFolderPath -notmatch '\\$')
{
    $sourceFolderPath = $sourceFolderPath + "\"
}

if ($targetFolderPathPrefix -and $targetFolderPathPrefix -notmatch '\\$')
{
    $targetFolderPathPrefix = $targetFolderPathPrefix + "\"
}

$templatePaths = Get-ChildItem $sourceFolderPath -Recurse -File -Filter "*.json" | ForEach-Object -Process {$_.FullName} | Where-Object {$_ -like "*Infrastructure*"}
foreach ($path in $templatePaths) {

    $targetPath = $targetFolderPathPrefix + $path.Substring($sourceFolderPath.Length)
    Write-Host "Processing $targetPath"
    
    Set-AzStorageBlobContent -File $path -Blob $targetPath -Container $StorageContainerName -Context $StorageAccount.Context -Confirm:$false -Force -ErrorAction SilentlyContinue
}

$containerEndPoint = $storageAccount.Context.BlobEndPoint + $storageContainerName + "/"
$containerSASToken = New-AzStorageContainerSASToken -Name $storageContainerName -Permission "r" -Context $storageAccount.Context -ExpiryTime (Get-Date).AddHours(1)

Write-Host "##vso[task.setvariable variable=containerEndPoint;isOutput=true]$containerEndPoint"
Write-Host "##vso[task.setvariable variable=containerSASToken;isOutput=true]$containerSASToken"