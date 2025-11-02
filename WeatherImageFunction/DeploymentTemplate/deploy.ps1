# Azure Weather Image Function - Deployment Script

param(
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,

    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$false)]
    [string]$Location = "germanywestcentral",
    
    [Parameter(Mandatory=$false)]
    [string]$NamePrefix = "weather",

    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev",

    [Parameter(Mandatory=$true)]
    [string]$UnsplashAccessKey
)

$ErrorActionPreference = 'Stop'

Write-Host "Starting deployment to Azure..." -ForegroundColor Green

# Ensure NuGet provider
function Ensure-NuGetProvider {
    try {
        if (-not (Get-PackageProvider -Name NuGet -ErrorAction SilentlyContinue)) {
            Write-Host "Installing NuGet provider..." -ForegroundColor Yellow
            Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser | Out-Null
        }
    } catch {
        Write-Warning "Failed to install NuGet provider automatically. Try running PowerShell as Administrator."
        throw
    }
}

# Ensure required Az modules
function Ensure-AzModules {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $requiredModules = @('Az.Accounts','Az.Resources','Az.Websites','Az.Storage')
    $missing = @()
    foreach ($m in $requiredModules) { if (-not (Get-Module -ListAvailable -Name $m)) { $missing += $m } }
    if ($missing.Count -gt 0) {
        Ensure-NuGetProvider
        Write-Host "Installing Az modules: $($missing -join ', ')" -ForegroundColor Yellow
        Install-Module -Name Az -Scope CurrentUser -Repository PSGallery -Force -AllowClobber
    }
    Import-Module Az.Accounts -ErrorAction Stop
    Import-Module Az.Resources -ErrorAction Stop
    Import-Module Az.Websites -ErrorAction Stop
    Import-Module Az.Storage  -ErrorAction Stop
}

# Ensure Bicep installed
function Ensure-Bicep {
    if (Get-Command bicep -ErrorAction SilentlyContinue) { return }
    $bin = Join-Path $HOME ".azure\bin"
    $az = Get-Command az -ErrorAction SilentlyContinue
    if ($az) {
        Write-Host "Installing Bicep via Azure CLI..." -ForegroundColor Yellow
        az bicep install | Out-Null
    } else {
        Write-Host "Azure CLI not found. Downloading Bicep CLI directly..." -ForegroundColor Yellow
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        if (-not (Test-Path $bin)) { New-Item -ItemType Directory -Path $bin -Force | Out-Null }
        $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        $asset = if ($arch -eq 'Arm64') { 'bicep-win-arm64.exe' } else { 'bicep-win-x64.exe' }
        $uri = "https://github.com/Azure/bicep/releases/latest/download/$asset"
        $bicepExe = Join-Path $bin 'bicep.exe'
        Invoke-WebRequest -Uri $uri -OutFile $bicepExe -UseBasicParsing
        & cmd /c "attrib +R `"$bicepExe`"" | Out-Null
    }
    $env:PATH = "$bin;$env:PATH"
    $ver = & bicep --version
    if (-not $ver) { throw "Bicep installation failed or not on PATH." }
    Write-Host "Bicep installed: $ver" -ForegroundColor Green
}

# Ensure resource provider registered
function Ensure-ProviderRegistered([string]$namespace) {
    $state = (Get-AzResourceProvider -ProviderNamespace $namespace).RegistrationState
    if ($state -ne 'Registered') {
        Write-Host "Registering provider $namespace ..." -ForegroundColor Yellow
        Register-AzResourceProvider -ProviderNamespace $namespace | Out-Null
        do {
            Start-Sleep -Seconds 3
            $state = (Get-AzResourceProvider -ProviderNamespace $namespace).RegistrationState
            Write-Host -NoNewline "."
        } while ($state -ne 'Registered')
        Write-Host " Registered" -ForegroundColor Green
    }
}

# Run initial checks
Ensure-AzModules
Ensure-Bicep

# Allowed regions per subscription policy
$allowedLocations = @('polandcentral','swedencentral','spaincentral','switzerlandnorth','germanywestcentral')
if ($allowedLocations -notcontains $Location.ToLowerInvariant()) {
    Write-Warning "Location '$Location' is not allowed by policy. Falling back to 'germanywestcentral'."
    $Location = 'germanywestcentral'
}

# Login and select subscription
$context = Get-AzContext -ErrorAction SilentlyContinue
if (!$context) {
    Write-Host "Logging in to Azure..." -ForegroundColor Yellow
    Connect-AzAccount | Out-Null
}
Write-Host "Selecting subscription $SubscriptionId..." -ForegroundColor Yellow
Set-AzContext -Subscription $SubscriptionId | Out-Null

# Ensure required resource providers
@('Microsoft.OperationalInsights','Microsoft.Insights','Microsoft.Web','Microsoft.Storage') | ForEach-Object { Ensure-ProviderRegistered $_ }

# Resolve paths
$scriptRoot    = $PSScriptRoot
$templateFile  = Join-Path $scriptRoot '..\Bicep\main.bicep'
$compiledTemplate = Join-Path ([System.IO.Path]::GetTempPath()) ("bicep-" + (Get-Date -Format "yyyyMMddHHmmss") + ".json")

# Find Function project
$projectRootDir = Resolve-Path (Join-Path $scriptRoot '..') | Select-Object -ExpandProperty Path
$projectFile = Get-ChildItem -Path $projectRootDir -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $projectFile) {
    $projectFile = Get-ChildItem -Path $projectRootDir -Recurse -Depth 1 -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $projectFile) { throw "No .csproj found under '$projectRootDir'." }
$projectDir = Split-Path -Parent $projectFile.FullName
Write-Host "Using project: $($projectFile.FullName)" -ForegroundColor Yellow

# Unique artifacts per run
$stamp         = Get-Date -Format "yyyyMMddHHmmss"
$artifactsDir  = Join-Path $projectDir 'artifacts'
if (-not (Test-Path $artifactsDir)) { New-Item -ItemType Directory -Path $artifactsDir | Out-Null }
$publishFolder = Join-Path $artifactsDir "publish-$stamp"
$zipPath       = Join-Path $artifactsDir "package-$stamp.zip"

# Compile Bicep
Write-Host "Compiling Bicep template..." -ForegroundColor Yellow
& bicep build $templateFile --outfile $compiledTemplate
if (-not (Test-Path $compiledTemplate)) { throw "Bicep compile failed." }

# Create or validate resource group
$rg = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
if (!$rg) {
    Write-Host "Creating resource group: $ResourceGroupName ($Location)" -ForegroundColor Yellow
    New-AzResourceGroup -Name $ResourceGroupName -Location $Location | Out-Null
} else {
    if ($rg.Location -ne $Location) {
        Write-Warning "Resource group exists in '$($rg.Location)'. Using that location for deployment."
        $Location = $rg.Location
    }
}

# Deploy compiled ARM template
Write-Host "Deploying infrastructure..." -ForegroundColor Yellow
$parameters = @{
    location          = $Location
    namePrefix        = $NamePrefix
    environment       = $Environment
    unsplashAccessKey = $UnsplashAccessKey
}

$deployment = New-AzResourceGroupDeployment `
    -Name "main" `
    -ResourceGroupName $ResourceGroupName `
    -TemplateFile $compiledTemplate `
    -TemplateParameterObject $parameters `
    -Verbose

if ($deployment.ProvisioningState -ne "Succeeded") {
    Write-Error "Infrastructure deployment failed. See output above for details."
    exit 1
}

Write-Host "Infrastructure deployment completed successfully!" -ForegroundColor Green

# Outputs
$functionAppName     = $deployment.Outputs.functionAppName.Value
$functionAppUrl      = $deployment.Outputs.functionAppUrl.Value
$storageAccountName  = $deployment.Outputs.storageAccountName.Value
Write-Host "Function App Name: $functionAppName" -ForegroundColor Cyan
Write-Host "Function App URL:  $functionAppUrl" -ForegroundColor Cyan

# Build and publish Function App
Write-Host "`nBuilding and packaging Function App..." -ForegroundColor Yellow
Push-Location $projectDir
dotnet build $projectFile.FullName --configuration Release
dotnet publish $projectFile.FullName --configuration Release --output $publishFolder
Pop-Location

if (-not (Test-Path $publishFolder)) { throw "Publish folder not found: $publishFolder" }

# Zip published app
function New-Zip([string]$sourceDir, [string]$destZip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    for ($i=1; $i -le 5; $i++) {
        try {
            if (Test-Path $destZip) { Remove-Item $destZip -Force }
            [System.IO.Compression.ZipFile]::CreateFromDirectory($sourceDir, $destZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
            return
        } catch {
            if ($i -eq 5) { throw }
            Start-Sleep -Seconds (2 * $i)
        }
    }
}
Write-Host "Creating zip package $zipPath ..." -ForegroundColor Yellow
New-Zip -sourceDir $publishFolder -destZip $zipPath

# Upload package and set WEBSITE_RUN_FROM_PACKAGE
Write-Host "Uploading package to Storage and updating WEBSITE_RUN_FROM_PACKAGE..." -ForegroundColor Yellow
$storageKey = (Get-AzStorageAccountKey -ResourceGroupName $ResourceGroupName -Name $storageAccountName)[0].Value
$ctx = New-AzStorageContext -StorageAccountName $storageAccountName -StorageAccountKey $storageKey
$containerName = "functionpackages"
$null = New-AzStorageContainer -Name $containerName -PublicAccess Off -Context $ctx -ErrorAction SilentlyContinue
$blobName = "app-$stamp.zip"

# Upload package
Set-AzStorageBlobContent -File $zipPath -Container $containerName -Blob $blobName -Context $ctx | Out-Null

# Create SAS URL
$sasUri = (New-AzStorageBlobSASToken `
    -Container $containerName `
    -Blob $blobName `
    -Permission r `
    -ExpiryTime (Get-Date).AddDays(1) `
    -FullUri `
    -Context $ctx
).ToString()

# Update Function App settings - preserve existing settings
Write-Host "Updating Function App settings..." -ForegroundColor Yellow
$currentSettings = (Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $functionAppName).SiteConfig.AppSettings
$settingsHash = @{}
foreach ($setting in $currentSettings) {
    $settingsHash[$setting.Name] = $setting.Value
}
$settingsHash['WEBSITE_RUN_FROM_PACKAGE'] = $sasUri

Set-AzWebApp -ResourceGroupName $ResourceGroupName -Name $functionAppName -AppSettings $settingsHash | Out-Null

# Restart Function App
Write-Host "Restarting Function App..." -ForegroundColor Yellow
Restart-AzWebApp -ResourceGroupName $ResourceGroupName -Name $functionAppName | Out-Null

Write-Host "`nDeployment completed successfully!" -ForegroundColor Green
Write-Host "Function App URL: $functionAppUrl" -ForegroundColor Cyan
Write-Host "`nQueue Names:" -ForegroundColor Cyan
Write-Host "  - job-queue (for starting jobs)" -ForegroundColor White
Write-Host "  - image-process-queue (for processing images)" -ForegroundColor White
Write-Host "`nBlob Container:" -ForegroundColor Cyan
Write-Host "  - weather-images (for storing generated images)" -ForegroundColor White