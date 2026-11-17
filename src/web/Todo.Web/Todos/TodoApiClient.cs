using System.Net.Http.Json;
using Todo.Contracts.Todos;

namespace Todo.Web.Todos;

public sealed class TodoApiClient(HttpClient http)
{
    public async Task<List<TodoResponse>> GetAll(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<TodoResponse>>("/api/todos", cancellationToken) ?? [];

    public async Task<TodoResponse> Create(string title, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("/api/todos", new CreateTodoRequest(title), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TodoResponse>(cancellationToken))!;
    }

    public async Task<TodoResponse> Update(TodoResponse todo, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync($"/api/todos/{todo.Id}",
            new UpdateTodoRequest(todo.Title, todo.IsCompleted), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TodoResponse>(cancellationToken))!;
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"/api/todos/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
