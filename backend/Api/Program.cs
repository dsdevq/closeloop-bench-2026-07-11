using Api.Features.Companies;
using Api.Features.Contacts;
using Api.Features.Notifications;
using Domain.Interfaces;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not configured.");

builder.Services.AddDbContext<CrmDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

// DealRottingNotificationJob is implemented and tested but deliberately NOT registered here.
// It requires Deal.OwnerId to identify the notification recipient. See AGENTS.md.

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapContactsEndpoints();
app.MapCompaniesEndpoints();
app.MapNotificationsEndpoints();

app.Run();

public partial class Program { }
