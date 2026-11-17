using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("postgres-password", "Password123", secret: true);
var postgres = builder.AddPostgres("postgres", password: password)
    .WithImageTag("17-alpine");

// Keep local development data across restarts; tests disable persistence for a fresh database.
if (builder.Configuration.GetValue("UsePersistence", true))
{
    postgres.WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
}

var database = postgres.AddDatabase("todos");
var backend = builder.AddProject<Projects.Todo_Backend>("backend")
    .WithReference(database).WaitFor(database).WithHttpHealthCheck("/health");
builder.AddProject<Projects.Todo_Web>("web")
    .WithReference(backend).WaitFor(backend).WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
