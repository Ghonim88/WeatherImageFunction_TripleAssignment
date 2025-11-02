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
resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
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
  properties: {
    reserved: true
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2022-03-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: listKeys(storageAccount.id, storageAccount.apiVersion).keys[0].value
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
        }
        {
          name: 'UnsplashAccessKey'
          value: unsplashAccessKey
        }
      ]
    }
  }
  dependsOn: [
    storageAccount
    appInsights
    appServicePlan
  ]
}

// Outputs
output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}/api'
output storageAccountName string = storageAccount.name
output appInsightsName string = appInsights.name
