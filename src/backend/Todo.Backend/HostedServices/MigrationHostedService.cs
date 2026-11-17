using Microsoft.EntityFrameworkCore;
using Todo.Backend.Data;

namespace Todo.Backend.HostedServices;

public sealed class MigrationHostedService(IServiceScopeFactory scopes) : IHostedService
{
    // Await migrations before the backend starts accepting requests.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TodoDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
