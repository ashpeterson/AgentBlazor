public sealed class CustomerService
{
    public Task FindAtRiskCustomersAsync()
        => Task.CompletedTask;

    public Task CreateFollowUpAsync(string customerId)
        => Task.CompletedTask;
}
