targetScope = 'resourceGroup'

@description('The location for all resources')
param location string = resourceGroup().location

@description('The name prefix for all resources')
param namePrefix string = 'weather'

@description('Environment name (dev, test, prod)')
param environment string = 'dev'

@description('Unsplash API Access Key')
@secure()
param unsplashAccessKey string

var uniqueSuffix = uniqueString(resourceGroup().id)
var storageAccountName = toLower('${namePrefix}${environment}${uniqueSuffix}')
var functionAppName = toLower('${namePrefix}-func-${environment}-${uniqueSuffix}')
var appInsightsName = toLower('${namePrefix}-ai-${environment}-${uniqueSuffix}')
var appServicePlanName = toLower('${namePrefix}-plan-${environment}-${uniqueSuffix}')

// Storage Account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
    publicNetworkAccess: 'Enabled'
  }
}

// Storage connection string variable
var storageAccountConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${az.environment().suffixes.storage}'

// Blob Service and Container
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2022-09-01' = {
  parent: storageAccount
  name: 'default'
}

resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-09-01' = {
  parent: blobService
  name: 'weather-images'
  properties: {
    publicAccess: 'None'
  }
}

// Queue Service and Queues
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2022-09-01' = {
  parent: storageAccount
  name: 'default'
}

resource jobQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2022-09-01' = {
  parent: queueService
  name: 'weather-stations-queue'
}

resource imageProcessQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2022-09-01' = {
  parent: queueService
  name: 'process-image-queue'
}

// Table Service and Table
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2022-09-01' = {
  parent: storageAccount
  name: 'default'
}

resource jobStatusTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2022-09-01' = {
  parent: tableService
  name: 'JobStatus'
}

// Application Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

// App Service Plan (Consumption for Function App)
resource appServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'elastic'
  properties: {
    reserved: true
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2022-03-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    reserved: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
    }
    httpsOnly: true
  }

  resource functionAppConfig 'config' = {
    name: 'appsettings'
    properties: {
      AzureWebJobsStorage: storageAccountConnectionString
      WEBSITE_CONTENTAZUREFILECONNECTIONSTRING: storageAccountConnectionString
      WEBSITE_CONTENTSHARE: toLower(functionAppName)
      FUNCTIONS_EXTENSION_VERSION: '~4'
      FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
      WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED: '1'
      APPINSIGHTS_INSTRUMENTATIONKEY: appInsights.properties.InstrumentationKey
      APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
      StorageConnectionString: storageAccountConnectionString
      UnsplashAccessKey: unsplashAccessKey
      BlobContainerName: 'weather-images'
      WeatherStationsQueueName: 'weather-stations-queue'
      ProcessImageQueueName: 'process-image-queue'
      TableName: 'JobStatus'
    }
  }
}

// Outputs
output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}/api'
output storageAccountName string = storageAccount.name
output appInsightsName string = appInsights.name