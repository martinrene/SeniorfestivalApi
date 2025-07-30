using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

public class Guests
{
    private readonly ILogger<Guests> _logger;
    private readonly IGuestRepository guestRepository;

    public Guests(ILogger<Guests> logger, IGuestRepository guestRepository)
    {
        _logger = logger;
        this.guestRepository = guestRepository;
    }

    [Function("Guests")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
    {
        _logger.LogInformation("Guests triggered");

        var guests = await guestRepository.ReadAllGuests();

        return new OkObjectResult(guests);
    }
}