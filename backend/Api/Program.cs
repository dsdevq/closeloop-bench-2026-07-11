using Api.Features.Activities;
using Api.Features.Companies;
using Api.Features.Contacts;
using Api.Features.Deals;
using Api.Features.Notifications;
using Api.Features.Pipelines;
using Api.Features.Search;
using Domain.Interfaces;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

// DATABASE_URL is the canonical single-var deploy gesture (set it in docker run / Fly / Render).
// Falls back to ASP.NET Core's ConnectionStrings:DefaultConnection (env var form:
// ConnectionStrings__DefaultConnection) for local appsettings or docker-compose setups.
var connectionString =
    Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "No database connection configured. Set DATABASE_URL or ConnectionStrings__DefaultConnection.");

builder.Services.AddDbContext<CrmDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

var app = builder.Build();

// Apply pending EF Core migrations on startup (skipped for InMemory in tests).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
}

// Serve the Angular production bundle from wwwroot/ (copied in by the Dockerfile runtime stage).
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapContactsEndpoints();
app.MapCompaniesEndpoints();
app.MapDealsEndpoints();
app.MapPipelinesEndpoints();
app.MapActivitiesEndpoints();
app.MapNotificationsEndpoints();
app.MapSearchEndpoints();

app.Run();

public partial class Program { }
