using Microsoft.EntityFrameworkCore;
using WebhookEngine.Api.Endpoints;
using WebhookEngine.Api.Workers;
using WebhookEngine.Infrastructure;
using WebhookEngine.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<DeliveryWorker>();
builder.Services.AddHostedService<SweeperService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WebhookEngineDbContext>();
    db.Database.Migrate();
}

app.Run();
