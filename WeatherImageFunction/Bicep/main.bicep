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
var storageAccountName = '${namePrefix}${environment}${uniqueSuffix}'
var functionAppName = '${namePrefix}-func-${environment}-${uniqueSuffix}'
var appInsightsName = '${namePrefix}-ai-${environment}-${uniqueSuffix}'
var appServicePlanName = '${namePrefix}-plan-${environment}-${uniqueSuffix}'

// Storage Account
module storage 'modules/storage.bicep' = {
  name: 'storageDeployment'
  params: {
    location: location
    storageAccountName: storageAccountName
  }
}

// Application Insights
module appInsights 'modules/appInsights.bicep' = {
  name: 'appInsightsDeployment'
  params: {
    location: location
    appInsightsName: appInsightsName
  }
}

// Function App
module functionApp 'modules/functionApp.bicep' = {
  name: 'functionAppDeployment'
  params: {
    location: location
    functionAppName: functionAppName
    appServicePlanName: appServicePlanName
    storageAccountName: storage.outputs.storageAccountName
    appInsightsInstrumentationKey: appInsights.outputs.instrumentationKey
    appInsightsConnectionString: appInsights.outputs.connectionString
    unsplashAccessKey: unsplashAccessKey
  }
}

// Outputs
output functionAppName string = functionApp.outputs.functionAppName
output functionAppUrl string = functionApp.outputs.functionAppUrl
output storageAccountName string = storage.outputs.storageAccountName
output appInsightsName string = appInsights.outputs.appInsightsName