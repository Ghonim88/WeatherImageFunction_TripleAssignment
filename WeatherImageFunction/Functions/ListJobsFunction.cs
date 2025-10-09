using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace WeatherImageFunction.Functions;

public class ListJobsFunction
{
    private readonly ILogger<ListJobsFunction> _logger;

    public ListJobsFunction(ILogger<ListJobsFunction> logger)
    {
        _logger = logger;
    }

    [Function("ListJobsFunction")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}