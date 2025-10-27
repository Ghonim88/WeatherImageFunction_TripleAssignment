@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Prefix for all resource names (letters/numbers only)')
param namePrefix string = 'weather'

@description('Environment tag (e.g., dev, test, prod)')
param environment string = 'dev'

@description('Unsplash Access Key (secure)')
@secure()
param unsplashAccessKey string

@description('Storage SKU')
param storageSku string = 'Standard_LRS'

var suffix = uniqueString(resourceGroup().id)
var saName = toLower(replace('${namePrefix}${environment}${suffix}', '-', ''))
var appPlanName = '${namePrefix}-${environment}-asp'
var funcName = '${namePrefix}-${environment}-func-${suffix}'
var aiName = '${namePrefix}-${environment}-ai'

resource sa 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: saName
  location: location
  sku: {
    name: storageSku
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: true // container itself will stay private
    accessTier: 'Hot'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  name: '${sa.name}/default'
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-01-01' = {
  name: '${sa.name}/default'
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-01-01' = {
  name: '${sa.name}/default'
}

@description('Blob container to store images')
resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${sa.name}/default/weather-images'
  properties: {
    publicAccess: 'None' // keep private; app returns SAS URLs
  }
  dependsOn: [
    blobService
  ]
}

@description('Weather stations queue')
resource stationsQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  name: '${sa.name}/default/weather-stations-queue'
  dependsOn: [
    queueService
  ]
}

@description('Process image queue')
resource processQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  name: '${sa.name}/default/process-image-queue'
  dependsOn: [
    queueService
  ]
}

@description('Job status table')
resource jobStatusTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  name: '${sa.name}/default/JobStatus'
  dependsOn: [
    tableService
  ]
}

resource ai 'Microsoft.Insights/components@2020-02-02' = {
  name: aiName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
    Request_Source: 'rest'
  }
}

@description('Linux Consumption plan for Functions')
resource plan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: appPlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'functionapp'
  properties: {
    reserved: true // Linux
  }
}

@description('Function App (.NET 8 isolated, Linux)')
resource func 'Microsoft.Web/sites@2023-01-01' = {
  name: funcName
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${sa.name};AccountKey=${listKeys(sa.id, \'2023-01-01\').keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        // App-specific settings
        {
          name: 'StorageConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${sa.name};AccountKey=${listKeys(sa.id, \'2023-01-01\').keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'BlobContainerName'
          value: 'weather-images'
        }
        {
          name: 'WeatherStationsQueueName'
          value: 'weather-stations-queue'
        }
        {
          name: 'ProcessImageQueueName'
          value: 'process-image-queue'
        }
        {
          name: 'TableName'
          value: 'JobStatus'
        }
        {
          name: 'UnsplashAccessKey'
          value: unsplashAccessKey
        }
        {
          name: 'BuienradarApiUrl'
          value: 'https://data.buienradar.nl/2.0/feed/json'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: ai.properties.ConnectionString
        }
      ]
    }
  }
}

output functionAppName string = func.name
output functionAppHostName string = func.properties.defaultHostName
output storageAccountName string = sa.name