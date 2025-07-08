using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

public class MyEvents
{
    private readonly ILogger<MyEvents> _logger;
    private readonly IMyEventRepository myEventRepository;

    public MyEvents(ILogger<MyEvents> logger, IMyEventRepository myEventRepository)
    {
        _logger = logger;
        this.myEventRepository = myEventRepository;
    }

    [Function("MyEvents")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", "delete")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        string? phoneId = req.Query["phoneId"];
        if (string.IsNullOrEmpty(phoneId))
        {
            return new OkResult();
        }



        switch (req.Method)
        {
            case "POST":
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                MyEventRequest? data = JsonSerializer.Deserialize<MyEventRequest>(requestBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (data != null && !string.IsNullOrEmpty(data.MyEventId))
                {
                    await myEventRepository.AddToMyEvents(phoneId, data.MyEventId);
                }
                return new AcceptedResult();

            case "DELETE":
                string? eventId = req.Query["eventId"];
                if (string.IsNullOrEmpty(eventId))
                {
                    return new OkResult();
                }

                await myEventRepository.RemoveFromMyEvents(phoneId, eventId);
                return new AcceptedResult();

            default:

                var myEvents = await myEventRepository.ReadAllMyEvents(phoneId);
                return new OkObjectResult(myEvents);
        }
    }

    private class MyEventRequest
    {
        public string MyEventId { get; set; } = "";
    }
}