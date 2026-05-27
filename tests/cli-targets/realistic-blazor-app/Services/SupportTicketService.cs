public sealed record SupportTicket(string TicketId, string Status, string Priority);

public sealed class SupportTicketService
{
    public Task<IReadOnlyList<SupportTicket>> ShowOpenTicketsAsync(int days)
        => Task.FromResult<IReadOnlyList<SupportTicket>>([]);

    public Task<string> DraftReplyAsync(string ticketId)
        => Task.FromResult($"Draft reply for {ticketId}");

    public Task EscalateTicketAsync(string ticketId)
        => Task.CompletedTask;
}
