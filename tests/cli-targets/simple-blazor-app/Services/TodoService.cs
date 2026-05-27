public sealed class TodoService
{
    public Task GetOverdueTodosAsync()
        => Task.CompletedTask;

    public Task MarkTodoCompleteAsync(int todoId)
        => Task.CompletedTask;
}
