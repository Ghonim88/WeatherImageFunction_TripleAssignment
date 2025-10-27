# Azure Weather Image Function - Deployment Script

param(
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,

    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$false)]
    [string]$Location = "westeurope",
    
    [Parameter(Mandatory=$false)]
    [string]$NamePrefix = "weather",

    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev",

    [Parameter(Mandatory=$true)]
    [string]$UnsplashAccessKey
)

Write-Host "Starting deployment to Azure..." -ForegroundColor Green

# Login to Azure (if not already logged in)
$context = Get-AzContext
if (!$context) {
    Write-Host "Logging in to Azure..." -ForegroundColor Yellow
    Connect-AzAccount
}

# Create resource group if it doesn't exist
$rg = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
if (!$rg) {
    Write-Host "Creating resource group: $ResourceGroupName" -ForegroundColor Yellow
    New-AzResourceGroup -Name $ResourceGroupName -Location $Location
}

# Deploy Bicep template
Write-Host "Deploying infrastructure..." -ForegroundColor Yellow
$deployment = New-AzResourceGroupDeployment `
    -ResourceGroupName $ResourceGroupName `
    -TemplateFile "../bicep/main.bicep" `
    -TemplateParameterFile "../bicep/parameters.json" `
    -location $Location `
    -namePrefix $NamePrefix `
    -environment $Environment `
    -unsplashAccessKey $UnsplashAccessKey `
    -Verbose

if ($deployment.ProvisioningState -eq "Succeeded") {
    Write-Host "Infrastructure deployment completed successfully!" -ForegroundColor Green
    
    $functionAppName = $deployment.Outputs.functionAppName.Value
    Write-Host "Function App Name: $functionAppName" -ForegroundColor Cyan
    Write-Host "Function App URL: $($deployment.Outputs.functionAppUrl.Value)" -ForegroundColor Cyan
    
    # Build and publish the function app
    Write-Host "`nBuilding and publishing Function App..." -ForegroundColor Yellow
    
    $projectPath = "../WeatherImageFunction"
    Push-Location $projectPath
    
    dotnet build --configuration Release
    dotnet publish --configuration Release --output ./publish
    
    # Create zip package
    $publishFolder = "./publish"
    $zipPath = "./publish.zip"
    
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    
    Compress-Archive -Path "$publishFolder/*" -DestinationPath $zipPath
    
    # Deploy to Azure
    Write-Host "Deploying to Azure Function App..." -ForegroundColor Yellow
    Publish-AzWebApp `
        -ResourceGroupName $ResourceGroupName `
        -Name $functionAppName `
        -ArchivePath $zipPath `
        -Force
    
    Pop-Location
    
    Write-Host "`nDeployment completed successfully!" -ForegroundColor Green
    Write-Host "Function App URL: https://$functionAppName.azurewebsites.net" -ForegroundColor Cyan
} else {
    Write-Host "Deployment failed!" -ForegroundColor Red
    Write-Host $deployment
}