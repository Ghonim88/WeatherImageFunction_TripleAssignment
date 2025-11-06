# Weather Image Function

An Azure Functions application that fetches weather station data from the Buienradar API, retrieves related images from Unsplash, overlays weather information on the images, and stores them in Azure Blob Storage.


## ✨ Features

- 🌦️ Fetches real-time weather data from Dutch weather stations (Buienradar API)
- 🖼️ Retrieves relevant images from Unsplash based on search keywords
- 🎨 Adds weather information overlays (station name, temperature) to images
- ☁️ Stores processed images in Azure Blob Storage
- 📊 Tracks job status and progress in Azure Table Storage
- ⚡ Queue-based asynchronous processing for scalability
- 🔄 Supports multiple concurrent jobs

## 🏗️ Architecture

### 📁 Project Structure

```
ImageGenFunctions/
│
├── Bicep/
│   ├── main.bicep
|       
├── DeplopymentTemplate/
│   ├── deploy.ps1       
│   ├── AzuriteConfig
|    
├── Functions/
│   ├── FetchWeatherStationsFunction.cs       
│   ├── GetResultsFunction.cs
|   ├── ProcessImageFunction.cs
|   ├── StartJobFunction.cs         
│
├── Http/
│   ├── Api.http
|
├── Models/
│   ├── BuienradarResponse.cs              
│   ├── JobRequest.cs         
│   ├── JobResponse.cs
│   ├── JobState.cs         
│   ├── JobStatus.cs         
│   ├── ProcessImageQueueMessage.cs         
│   ├── WeatherStation.cs         
│   ├── WeatherStationsQueueMessage.cs                  
│
├── Services/
│   ├── BlobStorageService.cs              
│   ├── IBlobStorageService.cs         
│   ├── IImageService.cs
│   ├── ImageService.cs         
│   ├── ITableStorageSerive.cs         
│   ├── IWeatherService.cs         
│   ├── TableSotrageService.cs         
│   ├── WeatherService.cs
├── local.settings.json               
├── host.json
├── Program.cs                                                  
└── README.md                         
```

## 📦 Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools v4](https://docs.microsoft.com/azure/azure-functions/functions-run-local)
- [Azurite](https://github.com/Azure/Azurite) (for local development)
- [Azure Storage Explorer](https://azure.microsoft.com/features/storage-explorer/) (optional, for debugging)
- [Unsplash API Access Key](https://unsplash.com/developers) (free)

## 🚀 Setup

### 1. Clone the Repository

### 2. Install Dependencies

### 3. Start Azurite (Local Storage Emulator)

- Using npm
- azurite --silent --location ./azurite --debug ./azurite/debug.log
- Or using Docker
- docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite

### 4. Install and Connect Microsoft Azure Storage Explorer

**Microsoft Azure Storage Explorer** is used throughout this project for:
- Managing blob containers and their permissions
- Viewing uploaded weather images
- Monitoring queue messages
- Inspecting table storage data
- Debugging storage-related issues

**Steps:**

1. **Download and Install:**
   - Go to [Azure Storage Explorer](https://azure.microsoft.com/features/storage-explorer/)
   - Download and install for your operating system

2. **Connect to Azurite (Local Emulator):**
   - Open Microsoft Azure Storage Explorer
   - It should automatically detect Azurite running on localhost
   - You'll see: **Local & Attached** → **Storage Accounts** → **Emulator - Default Ports (Key)**

3. **Verify Connection:**
   - Expand **Emulator - Default Ports**
   - You should see:
     - **Blob Containers**
     - **Queues**
     - **Tables**

### 5. Configure Application Settings

Create or update `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "StorageConnectionString": "UseDevelopmentStorage=true",
    "BlobContainerName": "weather-images",
    "WeatherStationsQueueName": "weather-stations-queue",
    "ProcessImageQueueName": "process-image-queue",
    "TableName": "JobStatus",
    "UnsplashAccessKey": "yxwZfofSf1xp7BIeYxTBkYIiQHYwVJr5tua_Njl4BE0", //v1 in case you don't have one
    //"UnsplashAccessKey": "EafEqJ20-E_0UFxGXc-JywG5BJk5hn_WTnc6xdMn6OE", v2 backup because of the limited request per hour
    "Storage:IsEmulator": "true",
    "Image:CacheControl": "no-cache, no-store, must-revalidate",
    "ImageOverlay:WatermarkText": "© AbdelrahmanGhonim_695857",
    "ImageOverlay:IncludeTimestamp": "true",
    "ImageOverlay:WatermarkFontSize": "20",
    "ImageOverlay:WatermarkOpacity": "0.75",
    "ImageOverlay:FontFamily": "DejaVu Sans",
    "ImageOverlay:FontSize": "48",
    "Image:MaxWidth": "1280",
    "Image:MaxHeight": "720",
    "Image:Quality": "85",
    "Image:UsePlaceholderOnError": "true",
    "Image:PlaceholderColor": "#2d2d2d",
    "Image:PlaceholderWidth": "1280",
    "Image:PlaceholderHeight": "720",
    "CityFilter:Strict": "false",
    "CityFilter:FallbackMaxStations": "5",
    "Unsplash:MaxRequestsPerJob": "45"
  },
  "Host": {
    "LocalHttpPort": 7071,
    "CORS": "*",
    "CORSCredentials": false
  }

}
```

## ✅ Features & Requirements (All Completed)
### ✅ 1. Build and Deploy Automatically from GitHub (CI/CD)

A full deployment pipeline is implemented using GitHub Actions.

Every push to the main branch triggers:

Restore & build (.NET 8 isolated Azure Function)

Publish the project

Deploy automatically to Azure Function App

Deployment uses the Azure Function publish profile stored securely as a GitHub secret.

### ✅ Requirement fully met.

### ✅ 2. Authentication on the API (Function Keys)

Each endpoint is protected using Azure Function API Keys:

Requests must include
?code=<FUNCTION_KEY>

Without the code → request is rejected.


### ✅ Function Key (API Key) — provided separately.
### ✅ Works with Postman, cURL, or browser.

### ✅ Requirement fully met.

## ✅ 3. Use SAS Token Instead of Public Blob URLs

Images are NOT publicly accessible in Blob Storage.
Instead, the system creates time-limited, read-only SAS URLs.
- Example returned by the API:
```
https://weatherXXXX.blob.core.windows.net/weather-images/6240.jpg?sv=2023-11-03&st=2025-11-06...&sp=r&sig=XXXXX
```
### This SAS URL:
- expires after hours
- allows only read access
- cannot be used to upload/modify data
- keeps blob storage private


## ✅ 4. Status Endpoint + Saving Status in Azure Table Storage

Every processing request becomes a Job, stored in Azure Table Storage (JobStatus table).
```
| Field              | Description                            |
| ------------------ | -------------------------------------- |
| JobId              | Unique job identifier                  |
| Status             | Pending / Running / Completed / Failed |
| ProgressPercentage | 0–100                                  |
| TotalStations      | How many stations are processed        |
| ProcessedStations  | How many finished                      |
| ImageUrls          | SAS-token URLs                         |
| CreatedAt          | Timestamp                              |
| CompletedAt        | Timestamp                              |
| ErrorMessage       | On failure                             |
```
### ✅ Status Endpoint
```
GET /api/jobs/{jobId}?code=<key>
```
- Returns job status + SAS URLs.

## ✅ 5. Job Creation Endpoint
```
POST /api/jobs?code=<key>
{
  "searchKeyword": "clouds",
  "city": "Amsterdam",
  "maxStations": 10
}
```
## ✅ How the System Works (Architecture Flow)

1.  POST /jobs
- Validates input

- Creates a Job record in Table Storage

- Sends a message to a queue

- Returns jobId to the client

2. Queue Trigger Function

- Fetches weather-station data

- Gets an Unsplash image

- Processes it (resize, overlay watermark/timestamp)

-Saves result to Blob Storage

Updates JobStatus in Table Storage

3. Client Polls Status

- Using GET /jobs/{jobId}

- Gets progress + SAS URLs

✅ Fully asynchronous & scalable.

## ✅ Azure Resources Used
```
| Resource                   | Purpose                        |
| -------------------------- | ------------------------------ |
| **Azure Function App**     | Hosts API and queue processing |
| **Azure Storage Account**  | Blob, Queue, and Table Storage |
| **Azure Table Storage**    | Stores job progress            |
| **Azure Queues**           | Async background processing    |
| **Blob Storage (Private)** | Stores generated images        |
| **SAS Tokens**             | Secure image access            |
| **GitHub Actions**         | CI/CD automated deployment     |
```
## ✅ CI/CD Pipeline Summary

File: .github/workflows/deploy-function-app.yml
- ✅ Automatically triggered on push to main
- ✅ Builds .NET 8 Azure Function
- ✅ Publishes to Azure Function output folder
- ✅ Deploys using Function Publish Profile
- ✅ No manual steps needed


