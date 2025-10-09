param location string
param storageAccountName string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }

  resource blobService 'blobServices' = {
    name: 'default'
    
    resource weatherImagesContainer 'containers' = {
      name: 'weather-images'
      properties: {
        publicAccess: 'None'
      }
    }
  }

  resource queueService 'queueServices' = {
    name: 'default'
    
    resource weatherStationsQueue 'queues' = {
      name: 'weather-stations-queue'
    }

    resource processImageQueue 'queues' = {
      name: 'process-image-queue'
    }
  }

  resource tableService 'tableServices' = {
    name: 'default'
    
    resource jobStatusTable 'tables' = {
      name: 'JobStatus'
    }
  }
}

output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output queueEndpoint string = storageAccount.properties.primaryEndpoints.queue
output tableEndpoint string = storageAccount.properties.primaryEndpoints.table