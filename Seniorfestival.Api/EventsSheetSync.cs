using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Repositories;

namespace Seniorfestival.Api;

public class EventsSheetSync
{
    private readonly ILogger<EventsSheetSync> _logger;
    private readonly IEventRepository eventRepository;

    public EventsSheetSync(ILogger<EventsSheetSync> logger, IEventRepository eventRepository)
    {
        _logger = logger;
        this.eventRepository = eventRepository;
    }

    [Function("EventsSheetSync")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        string? type = req.Query["type"];

        string partitionKey;
        if (string.Equals(type, "Program", StringComparison.OrdinalIgnoreCase))
        {
            partitionKey = "Program";
        }
        else if (string.Equals(type, "Aktivitet", StringComparison.OrdinalIgnoreCase))
        {
            partitionKey = "Aktivitet";
        }
        else
        {
            return new BadRequestObjectResult("Query parameter 'type' must be either 'Program' or 'Aktivitet'.");
        }

        string? spreadsheetId = Environment.GetEnvironmentVariable($"googleSheetsSpreadsheetId_{partitionKey}");
        if (string.IsNullOrEmpty(spreadsheetId))
        {
            return new BadRequestObjectResult($"No spreadsheet configured for type '{partitionKey}'. Set app setting 'googleSheetsSpreadsheetId_{partitionKey}'.");
        }

        string? tabName = Environment.GetEnvironmentVariable($"googleSheetsTabName_{partitionKey}");
        if (string.IsNullOrEmpty(tabName))
        {
            return new BadRequestObjectResult($"No sheet tab configured for type '{partitionKey}'. Set app setting 'googleSheetsTabName_{partitionKey}'.");
        }

        string? cellRange = Environment.GetEnvironmentVariable("googleSheetsCellRange");
        if (string.IsNullOrEmpty(cellRange))
        {
            return new BadRequestObjectResult("App setting 'googleSheetsCellRange' is not configured.");
        }

        string? credentialsSetting = Environment.GetEnvironmentVariable("googleServiceAccountCredentials");
        if (string.IsNullOrEmpty(credentialsSetting))
        {
            return new BadRequestObjectResult("App setting 'googleServiceAccountCredentials' is not configured.");
        }

        try
        {
            ServiceAccountCredential specificCredential = credentialsSetting.TrimStart().StartsWith('{')
                ? CredentialFactory.FromJson<ServiceAccountCredential>(credentialsSetting)
                : CredentialFactory.FromFile<ServiceAccountCredential>(credentialsSetting);

            GoogleCredential credential = specificCredential.ToGoogleCredential()
                .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);

            using var sheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "SeniorfestivalApi"
            });

            var valueRange = await sheetsService.Spreadsheets.Values.Get(spreadsheetId, $"{tabName}!{cellRange}").ExecuteAsync();
            var rows = valueRange.Values ?? [];

            // Read the table before writing anything: an upsert replaces the whole row, so
            // every field the sheet has no column for has to be carried over explicitly.
            var existingEvents = await eventRepository.ReadEventsByPartition(partitionKey);
            var storedByRowKey = existingEvents.ToDictionary(e => e.RowKey);

            var sheetEvents = new List<Event>();
            foreach (var row in rows)
            {
                string rowKey = GetCell(row, 9);
                if (string.IsNullOrWhiteSpace(rowKey))
                {
                    continue;
                }

                bool.TryParse(GetCell(row, 8), out bool isPublic);

                var sheetEvent = new Event
                {
                    PartitionKey = partitionKey,
                    RowKey = rowKey,
                    Day = GetCell(row, 0).ToLowerInvariant(),
                    Start = NormalizeTime(GetCell(row, 1)),
                    End = NormalizeTime(GetCell(row, 2)),
                    Title = GetCell(row, 3),
                    Description = GetCell(row, 4),
                    Location = GetCell(row, 5),
                    PictureUrl = GetCell(row, 6),
                    Links = GetCell(row, 7),
                    Public = isPublic
                };

                // The queue fields live outside the sheet: QrCode, MinutesPerPerson and
                // OpeningHours are edited on the admin site, and the service history is
                // written by the queue itself. Dropping them here would silently reset
                // every activity's queue setup on the next sync.
                if (storedByRowKey.TryGetValue(rowKey, out var stored))
                {
                    sheetEvent.QrCode = stored.QrCode;
                    sheetEvent.MinutesPerPerson = stored.MinutesPerPerson;
                    sheetEvent.OpeningHours = stored.OpeningHours;
                    sheetEvent.LastServedAt = stored.LastServedAt;
                    sheetEvent.RecentServiceMinutes = stored.RecentServiceMinutes;
                }

                sheetEvents.Add(sheetEvent);
            }

            if (sheetEvents.Count == 0)
            {
                _logger.LogWarning("Google Sheet returned no rows with a valid Id for type '{PartitionKey}'. Skipping sync to avoid deleting existing data.", partitionKey);
                return new ObjectResult(new
                {
                    partitionKey,
                    upserted = 0,
                    deleted = 0,
                    warning = "Sheet returned no rows with a valid Id column value; existing table rows were left untouched."
                })
                { StatusCode = 422 };
            }

            foreach (var evt in sheetEvents)
            {
                await eventRepository.UpsertEvent(evt);
            }

            var sheetRowKeys = sheetEvents.Select(e => e.RowKey).ToHashSet();
            var eventsToDelete = existingEvents.Where(e => !sheetRowKeys.Contains(e.RowKey));

            int deletedCount = 0;
            foreach (var evt in eventsToDelete)
            {
                await eventRepository.DeleteEvent(evt);
                deletedCount++;
            }

            return new OkObjectResult(new
            {
                partitionKey,
                upserted = sheetEvents.Count,
                deleted = deletedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync events from Google Sheet for type '{PartitionKey}'", partitionKey);
            return new ObjectResult(ex.Message) { StatusCode = 500 };
        }
    }

    private static string GetCell(IList<object> row, int index)
    {
        return index < row.Count && row[index] != null ? row[index].ToString() ?? "" : "";
    }

    private static string NormalizeTime(string value)
    {
        return value.Replace('.', ':');
    }
}
