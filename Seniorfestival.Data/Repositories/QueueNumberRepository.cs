using Azure;
using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class QueueNumberRepository : IQueueNumberRepository
    {
        private const int MaxTicketAssignAttempts = 5;

        private readonly ITableRepository<QueueNumber> repository;
        private readonly Random random = new Random();

        public QueueNumberRepository(ITableRepository<QueueNumber> repository)
        {
            this.repository = repository;
        }

        public async Task<QueueNumber?> FindActiveQueueNumber(string eventId, string phoneId)
        {
            var matches = await repository.GetFromQueryAsync(
                $"PartitionKey eq '{eventId}' and PhoneId eq '{phoneId}' and Done eq false");

            return matches.FirstOrDefault();
        }

        public async Task<QueueNumber> AddToQueue(string eventId, string phoneId, string name)
        {
            for (int attempt = 0; attempt < MaxTicketAssignAttempts; attempt++)
            {
                var queueNumber = new QueueNumber
                {
                    EventId = eventId,
                    Number = random.Next(100000, 1000000).ToString(),
                    PhoneId = phoneId,
                    Name = name,
                    Done = false,
                };

                try
                {
                    await repository.AddAsync(queueNumber);
                    return queueNumber;
                }
                catch (RequestFailedException ex) when (ex.Status == 409)
                {
                    // Ticket number already taken for this event, try another random number.
                }
            }

            throw new InvalidOperationException(
                $"Could not assign a queue number for event '{eventId}' after {MaxTicketAssignAttempts} attempts");
        }

        public async Task<QueueNumber?> MarkDone(string eventId, string number)
        {
            QueueNumber? ticket;
            try
            {
                ticket = await repository.GetAsync(eventId, number);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }

            if (ticket == null || ticket.Done)
            {
                return null;
            }

            ticket.Done = true;
            ticket.DoneAt = DateTimeOffset.UtcNow;
            await repository.UpdateAsync(ticket);

            return ticket;
        }

        public async Task<QueueNumber[]> ReadActiveQueueForEvent(string eventId)
        {
            return (await repository.GetFromQueryAsync($"PartitionKey eq '{eventId}' and Done eq false")).ToArray();
        }

        public async Task<QueueNumber[]> ReadActiveQueueForPhone(string phoneId)
        {
            return (await repository.GetFromQueryAsync($"PhoneId eq '{phoneId}' and Done eq false")).ToArray();
        }
    }
}
