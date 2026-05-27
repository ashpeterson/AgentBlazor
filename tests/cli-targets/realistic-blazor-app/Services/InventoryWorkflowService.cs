public sealed class InventoryWorkflowService
{
    public Task PrepareRestockPlanAsync(string region)
        => Task.CompletedTask;

    public Task ApproveSupplierTransferAsync(string transferId)
        => Task.CompletedTask;

    public Task ValidateStockRiskAsync(string sku)
        => Task.CompletedTask;
}
