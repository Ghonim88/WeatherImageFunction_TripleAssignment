Weather Image Function 

# Weather Image Function

An Azure Functions application that fetches weather station data from the Buienradar API, retrieves related images from Unsplash, overlays weather information on the images, and stores them in Azure Blob Storage.

## 📋 Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [Configuration](#configuration)
- [API Endpoints](#api-endpoints)
- [Usage](#usage)
- [Troubleshooting](#troubleshooting)
- [Project Structure](#project-structure)
- [Technologies](#technologies)

## ✨ Features

- 🌦️ Fetches real-time weather data from Dutch weather stations (Buienradar API)
- 🖼️ Retrieves relevant images from Unsplash based on search keywords
- 🎨 Adds weather information overlays (station name, temperature) to images
- ☁️ Stores processed images in Azure Blob Storage
- 📊 Tracks job status and progress in Azure Table Storage
- ⚡ Queue-based asynchronous processing for scalability
- 🔄 Supports multiple concurrent jobs

## 🏗️ Architecture

sequenceDiagram
    participant Client
    participant StartJob as StartJobFunction
    participant Queue1 as weather-stations-queue
    participant FetchStations as FetchWeatherStationsFunction
    participant Queue2 as process-image-queue
    participant ProcessImage as ProcessImageFunction
    participant Table as Table Storage
    participant Blob as Blob Storage

    Client->>StartJob: POST /api/jobs
    StartJob->>Table: Create JobStatus (Pending)
    StartJob->>Queue1: Queue message
    StartJob-->>Client: 202 Accepted (JobId)
    
    Queue1->>FetchStations: Trigger
    FetchStations->>Table: Update status (Processing)
    FetchStations->>FetchStations: Fetch 50 stations from Buienradar
    loop For each station
        FetchStations->>Queue2: Queue station message
    end
    
    loop For each queued station
        Queue2->>ProcessImage: Trigger
        ProcessImage->>ProcessImage: Fetch image from Unsplash
        ProcessImage->>ProcessImage: Add weather overlay
        ProcessImage->>Blob: Upload image
        Blob-->>ProcessImage: SAS URL
        ProcessImage->>Table: Add image URL, increment counter
    end
    
    ProcessImage->>Table: Update status (Completed)
    
    Client->>StartJob: GET /api/jobs/{jobId}
    StartJob->>Table: Get JobStatus
    StartJob-->>Client: 200 OK (status + image URLs)

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
    "AzureWebJobsDashboard": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet",
    "BUIENRADAR_API_KEY": "<your_buienradar_api_key>",
    "AZURE_STORAGE_CONNECTION_STRING": "DefaultEndpointsProtocol=https;AccountName=<account_name>;AccountKey=<account_key>;EndpointSuffix=core.windows.net",
    "TABLE_CONNECTION_STRING": "DefaultEndpointsProtocol=https;AccountName=<account_name>;AccountKey=<account_key>;TableEndpoint=https://<account_name>.table.core.windows.net/",
    "CONTAINER_NAME": "weatherimages",
    "QUEUE_NAME": "weather-stations-queue",
    "PROCESS_IMAGE_QUEUE": "process-image-queue",
    "AZURE_FUNCTIONS_JOB_HOST__LOGGING__LOG_LEVEL": "Debug",
    "WEATHER_API_URL": "https://api.buienradar.nl/data/public/2.0/json-feed-static",
    "WEATHER_IMAGE_SEARCH_KEYWORDS": "weather,landscape",
    "WEATHER_IMAGE_DOWNLOAD_PATH": "images/temperature_{rand}.jpg",
    "WEATHER_IMAGE_UPLOAD_PATH": "weatherimages/{rand}.jpg",
    "WEATHER_IMAGE_PROCESSOR_OPTIONS": "{\"resize\":{\"width\":800,\"height\":600},\"quality\":80}",
    "IMAGE_OPTIMIZATION_LEVEL": "medium",
    "UNSPLASH_ACCESS_KEY": "YOUR_UNSPLASH_ACCESS_KEY_HERE"
  }
}
```

### 6. Get Your Unsplash API Key

1. Sign up at [Unsplash Developers](https://unsplash.com/developers)
2. Create a new application
3. Copy the **Access Key**
4. Replace `YOUR_UNSPLASH_ACCESS_KEY_HERE` in `local.settings.json`

### 7. Build the Project

### 8. Run the Function App

## ⚙️ Configuration

### host.json Settings

Key configuration in `host.json`:

```json
{
  "version": "2.0",
  "extensions": {
    "queues": {
      "messageEncoding": "none"
    }
  }
}
```

**Important:** `"messageEncoding": "none"` is required for queue triggers to work properly with `BinaryData.FromString()` in .NET 8 isolated worker mode.

This is crucial for .NET 8 isolated worker mode to properly deserialize queue messages.

### Blob Storage Permissions

The application automatically sets container permissions based on environment:

- **Local Development (Azurite):** `PublicAccessType.Blob` - Direct URL access
- **Production (Azure):** `PublicAccessType.None` - SAS URL required

The `SetAccessPolicyAsync()` call ensures existing containers get updated permissions.

### Image Processing

Images are processed using SixLabors.ImageSharp:
1. Download random image from Unsplash
2. Load image into memory
3. Draw semi-transparent background
4. Add weather station name and temperature text
5. Save as JPEG
6. Upload to Blob Storage

## 🔐 Security Notes

- Never commit `local.settings.json` to source control
- Keep your Unsplash Access Key private
- In production, use Azure Key Vault for secrets
- Use managed identities for Azure resource access

## 📄 License

[Your License Here]

## 👥 Contributors

[Your Name/Team]

## 🤝 Contributing

[Contribution guidelines if applicable]

---

**Made with ☁️ and ❤️ using Azure Functions**

# Usage

### Starting a New Job

To process weather data and generate images:

1. **Open Postman or any API client**
2. **Create a POST request** to `http://localhost:7071/api/jobs`
3. **Send** the request
4. **Check the response** for `JobId` and status

### Checking Job Status

To check the status of a job and retrieve image URLs:

6. **Send** → Copy the `jobId` from the response
7. **Create a GET request** to `http://localhost:7071/api/jobs/{jobId}`
8. **Send** to check job status and get image URLs

### Viewing Images with Microsoft Azure Storage Explorer

**Microsoft Azure Storage Explorer** is the primary tool used for viewing and managing the generated weather images:

1. **Open Microsoft Azure Storage Explorer**
2. **Navigate to:** 
- **Local & Attached** → **Emulator - Default Ports** → **Blob Containers** → **weather-images**
3. **View Images:**
- You'll see all uploaded images listed (e.g., `6370_20251009100000.jpg`)
- **Double-click** any image to preview it with the weather overlay
- **Right-click** → **Download** to save locally
4. **Check Image Properties:**
- Right-click on an image → **Properties**
- View metadata, size, content type, etc.

### Alternative: View Images in Browser

Once the job is complete, you can also **click on the image URLs directly in your browser** (if container permissions are set to public):

## 🔧 Troubleshooting

### Issue: Images Don't Load in Browser (403 Forbidden)

**Problem:** Container has private access, preventing direct URL access.

**Solution: Using Microsoft Azure Storage Explorer**

1. **Open Microsoft Azure Storage Explorer**
2. **Navigate to:** 
   - **Local & Attached** → **Emulator - Default Ports** → **Blob Containers** → **weather-images**
3. **Right-click on `weather-images` container**
4. **Select:** **Set Public Access Level**
5. **Choose:** "Public read access for blobs only"
6. **Click OK**

**Verification:**
- The container icon should now show a globe symbol 🌐
- Test an image URL in your browser - it should now work!

**Alternative Solution: Delete and Recreate Container (Using Azure Storage Explorer)**

1. **Stop the function** (Ctrl+C in terminal)
2. **In Microsoft Azure Storage Explorer:**
   - Navigate to **weather-images** container
   - **Right-click** → **Delete**
   - Confirm deletion
3. **Restart function:** `func start`
4. **Trigger a new job** - container will be created with correct public permissions automatically

### Issue: Queue Triggers Not Firing

**Problem:** Messages fail to decode and are moved to poison queue.

**Diagnosis Using Microsoft Azure Storage Explorer:**

1. **Open Microsoft Azure Storage Explorer**
2. **Navigate to:** **Local & Attached** → **Emulator** → **Queues**
3. **Check for poison queues:**
   - `weather-stations-queue-poison`
   - `process-image-queue-poison`
4. **View messages** in poison queues to see what failed

**Solution:**

1. **Verify** `"messageEncoding": "none"` is set in `host.json`
2. **In Microsoft Azure Storage Explorer:**
   - Right-click on `weather-stations-queue-poison` → **Delete Queue**
   - Right-click on `process-image-queue-poison` → **Delete Queue**
3. **Restart the function**
4. **Trigger a new job**

### Issue: Job Stuck in "Pending" Status

**Problem:** Queue messages aren't being processed.

**Diagnosis Using Microsoft Azure Storage Explorer:**

1. **Check queue message counts:**
   - Navigate to **Queues** in Azure Storage Explorer
   - Check `weather-stations-queue` - should show messages if job is pending
   - Check `process-image-queue` - should show messages if stations are being processed

2. **Inspect a queue message:**
   - Right-click on a queue → **View Messages**
   - Check if message format is correct JSON

**Solution:**
- If messages are stuck, clear the queues using Azure Storage Explorer
- Restart the function app
- Trigger a new job

### Issue: Unsplash API Errors

**Problem:** "No image URL found in Unsplash response"

**Possible causes:**
- Invalid or missing Unsplash Access Key
- Rate limit exceeded (50 requests/hour for free tier)
- Network connectivity issues

**Solution:**
- Verify your Unsplash Access Key in `local.settings.json`
- Wait if rate limit is exceeded
- Check logs for detailed error messages

### Issue: Package Vulnerabilities

**Problem:** Build warnings about `SixLabors.ImageSharp` vulnerabilities.

**Solution:**
Check [NuGet](https://www.nuget.org/packages/SixLabors.ImageSharp) for the latest secure version.

## 📁 Project Structure

```
WeatherImageFunction/ ├── Functions/ │   ├── StartJobFunction.cs          # HTTP trigger to start a new job │   ├── GetResultsFunction.cs        # HTTP trigger to get job results │   ├── ListJobsFunction.cs          # HTTP trigger to list all jobs │   ├── FetchWeatherStationsFunction.cs  # Queue trigger to fetch weather data │   └── ProcessImageFunction.cs      # Queue trigger to process images ├── Services/ │   ├── IWeatherService.cs           # Weather service interface │   ├── WeatherService.cs            # Buienradar API integration │   ├── IImageService.cs             # Image service interface │   ├── ImageService.cs              # Unsplash API + image processing │   ├── IBlobStorageService.cs       # Blob storage interface │   ├── BlobStorageService.cs        # Azure Blob Storage operations │   ├── ITableStorageService.cs      # Table storage interface │   └── TableStorageService.cs       # Azure Table Storage operations ├── Models/ │   ├── JobStatus.cs                 # Job status entity │   ├── JobRequest.cs                # Job request model │   ├── JobResponse.cs               # Job response model │   ├── WeatherStation.cs            # Weather station model │   ├── WeatherStationsQueueMessage.cs │   └── ProcessImageQueueMessage.cs ├── Program.cs                       # DI container & app configuration ├── host.json                        # Function app settings ├── local.settings.json              # Local configuration (not in source control) └── README.md                        # This file

```

