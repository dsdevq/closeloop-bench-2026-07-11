using System.Net;
using System.Net.Http.Json;
using Api.Features.Deals;
using Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Features.Deals;

public sealed class DealsEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DealsEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;");

                builder.ConfigureTestServices(services =>
                {
                    var dbName = "DealsTest_" + Guid.NewGuid();

                    var optConfigType = typeof(IDbContextOptionsConfiguration<CrmDbContext>);
                    foreach (var d in services.Where(d => d.ServiceType == optConfigType).ToList())
                        services.Remove(d);

                    foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<CrmDbContext>)).ToList())
                        services.Remove(d);

                    services.AddDbContext<CrmDbContext>(options =>
                        options.UseInMemoryDatabase(dbName));
                });
            });
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private static CreateDealRequest ValidDealRequest(Guid pipelineId, Guid stageId) =>
        new("Enterprise License", 15_000m, Guid.NewGuid(), pipelineId, stageId);

    private async Task<(Guid pipelineId, Guid stageId)> SeedPipelineAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospecting", order: 0);
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync();

        var stage = pipeline.Stages[0];
        return (pipeline.Id, stage.Id);
    }

    private async Task<(Guid pipelineId, Guid stage1Id, Guid stage2Id)> SeedPipelineWithTwoStagesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var pipeline = Pipeline.Create("Two-Stage Pipeline");
        pipeline.AddStage("Prospecting", order: 0);
        pipeline.AddStage("Qualified", order: 1);
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync();

        return (pipeline.Id, pipeline.Stages[0].Id, pipeline.Stages[1].Id);
    }

    [Fact]
    public async Task GetDeals_EmptyDb_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/deals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DealResponse[]>();
        Assert.NotNull(body);
        Assert.Empty(body);
    }

    [Fact]
    public async Task PostDeal_ValidRequest_Returns201WithCreatedResource()
    {
        var (pipelineId, stageId) = await SeedPipelineAsync();
        var ownerId = Guid.NewGuid();
        var closeDate = new DateOnly(2026, 12, 31);
        var req = new CreateDealRequest("Big Deal", 25_000m, ownerId, pipelineId, stageId, closeDate);

        var response = await _client.PostAsJsonAsync("/deals", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("Big Deal", body.Title);
        Assert.Equal(25_000m, body.Amount);
        Assert.Equal(ownerId, body.OwnerId);
        Assert.Equal(pipelineId, body.PipelineId);
        Assert.Equal(stageId, body.PipelineStageId);
        Assert.Equal(closeDate, body.CloseDate);
        Assert.Null(body.CompanyId);
        Assert.Null(body.ContactId);
    }

    [Fact]
    public async Task PostDeal_NullOptionalFields_Returns201()
    {
        var (pipelineId, stageId) = await SeedPipelineAsync();
        var req = new CreateDealRequest("Simple Deal", 0m, Guid.NewGuid(), pipelineId, stageId);

        var response = await _client.PostAsJsonAsync("/deals", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(body);
        Assert.Null(body.CloseDate);
        Assert.Null(body.CompanyId);
        Assert.Null(body.ContactId);
    }

    [Fact]
    public async Task CreateThenGet_RoundTrip_ReturnsSameDeal()
    {
        var (pipelineId, stageId) = await SeedPipelineAsync();
        var req = new CreateDealRequest("Round-trip Deal", 5_000m, Guid.NewGuid(), pipelineId, stageId);
        var createResponse = await _client.PostAsJsonAsync("/deals", req);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(created);

        var getResponse = await _client.GetAsync($"/deals/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Round-trip Deal", fetched.Title);
        Assert.Equal(5_000m, fetched.Amount);
    }

    [Fact]
    public async Task GetDeal_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/deals/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostDeal_BlankTitle_Returns422ValidationProblem()
    {
        var (pipelineId, stageId) = await SeedPipelineAsync();
        var req = new CreateDealRequest("", 1000m, Guid.NewGuid(), pipelineId, stageId);

        var response = await _client.PostAsJsonAsync("/deals", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostDeal_NegativeAmount_Returns422ValidationProblem()
    {
        var (pipelineId, stageId) = await SeedPipelineAsync();
        var req = new CreateDealRequest("Bad Amount", -1m, Guid.NewGuid(), pipelineId, stageId);

        var response = await _client.PostAsJsonAsync("/deals", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostDeal_EmptyOwnerId_Returns422ValidationProblem()
    {
        var (pipelineId, stageId) = await SeedPipelineAsync();
        var req = new CreateDealRequest("Deal", 1000m, Guid.Empty, pipelineId, stageId);

        var response = await _client.PostAsJsonAsync("/deals", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetDeals_AfterCreatingTwo_ReturnsBoth()
    {
        var (pipelineId, stageId) = await SeedPipelineAsync();
        await _client.PostAsJsonAsync("/deals", new CreateDealRequest("Deal A", 1000m, Guid.NewGuid(), pipelineId, stageId));
        await _client.PostAsJsonAsync("/deals", new CreateDealRequest("Deal B", 2000m, Guid.NewGuid(), pipelineId, stageId));

        var response = await _client.GetAsync("/deals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DealResponse[]>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Length);
    }

    [Fact]
    public async Task PatchDealStage_ValidStageChange_Returns200AndFiresStageChangedNotification()
    {
        var (pipelineId, stage1Id, stage2Id) = await SeedPipelineWithTwoStagesAsync();
        var ownerId = Guid.NewGuid();
        var createResp = await _client.PostAsJsonAsync("/deals",
            new CreateDealRequest("Stage Test Deal", 5000m, ownerId, pipelineId, stage1Id));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var deal = await createResp.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(deal);

        var patchResp = await _client.PatchAsJsonAsync($"/deals/{deal.Id}/stage",
            new PatchDealStageRequest(stage2Id));

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var body = await patchResp.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(body);
        Assert.Equal(stage2Id, body.PipelineStageId);
        Assert.Equal(ownerId, body.OwnerId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var notifications = await db.Notifications.ToListAsync();
        Assert.Single(notifications);
        Assert.Equal(NotificationTrigger.DealStageChanged, notifications[0].Trigger);
        Assert.Equal(ownerId, notifications[0].RecipientUserId);
        Assert.Equal(deal.Id, notifications[0].RelatedEntityId);
    }

    [Fact]
    public async Task PatchDealStage_WithNewOwner_FiresBothStageChangedAndDealAssignedNotifications()
    {
        var (pipelineId, stage1Id, stage2Id) = await SeedPipelineWithTwoStagesAsync();
        var originalOwnerId = Guid.NewGuid();
        var newOwnerId = Guid.NewGuid();
        var createResp = await _client.PostAsJsonAsync("/deals",
            new CreateDealRequest("Owner Change Deal", 8000m, originalOwnerId, pipelineId, stage1Id));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var deal = await createResp.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(deal);

        var patchResp = await _client.PatchAsJsonAsync($"/deals/{deal.Id}/stage",
            new PatchDealStageRequest(stage2Id, newOwnerId));

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var body = await patchResp.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(body);
        Assert.Equal(stage2Id, body.PipelineStageId);
        Assert.Equal(newOwnerId, body.OwnerId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var notifications = await db.Notifications.ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n => n.Trigger == NotificationTrigger.DealStageChanged && n.RecipientUserId == newOwnerId);
        Assert.Contains(notifications, n => n.Trigger == NotificationTrigger.DealAssigned && n.RecipientUserId == newOwnerId);
    }

    [Fact]
    public async Task PatchDealStage_SameOwner_FiresOnlyStageChangedNotification()
    {
        var (pipelineId, stage1Id, stage2Id) = await SeedPipelineWithTwoStagesAsync();
        var ownerId = Guid.NewGuid();
        var createResp = await _client.PostAsJsonAsync("/deals",
            new CreateDealRequest("Same Owner Deal", 3000m, ownerId, pipelineId, stage1Id));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var deal = await createResp.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(deal);

        // Pass the same owner in the PATCH — should not fire DealAssigned
        var patchResp = await _client.PatchAsJsonAsync($"/deals/{deal.Id}/stage",
            new PatchDealStageRequest(stage2Id, ownerId));

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var notifications = await db.Notifications.ToListAsync();
        Assert.Single(notifications);
        Assert.Equal(NotificationTrigger.DealStageChanged, notifications[0].Trigger);
    }

    [Fact]
    public async Task PatchDealStage_UnknownDeal_Returns404()
    {
        var (pipelineId, stage1Id, stage2Id) = await SeedPipelineWithTwoStagesAsync();

        var patchResp = await _client.PatchAsJsonAsync($"/deals/{Guid.NewGuid()}/stage",
            new PatchDealStageRequest(stage2Id));

        Assert.Equal(HttpStatusCode.NotFound, patchResp.StatusCode);
    }

    [Fact]
    public async Task PatchDealStage_StageBelongsToDifferentPipeline_Returns422()
    {
        var (pipelineId, stage1Id, _) = await SeedPipelineWithTwoStagesAsync();
        var (otherPipelineId, otherStageId) = await SeedPipelineAsync();
        var ownerId = Guid.NewGuid();
        var createResp = await _client.PostAsJsonAsync("/deals",
            new CreateDealRequest("Cross-Pipeline Deal", 1000m, ownerId, pipelineId, stage1Id));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var deal = await createResp.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(deal);

        // Attempt to move to a stage from a different pipeline
        var patchResp = await _client.PatchAsJsonAsync($"/deals/{deal.Id}/stage",
            new PatchDealStageRequest(otherStageId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, patchResp.StatusCode);
    }

    [Fact]
    public async Task PostDeal_StageBelongsToDifferentPipeline_Returns422()
    {
        var (pipelineId, _) = await SeedPipelineAsync();
        var (_, otherStageId) = await SeedPipelineAsync();

        // PipelineStageId belongs to a different pipeline than PipelineId
        var req = new CreateDealRequest("Cross-Pipeline Deal", 1000m, Guid.NewGuid(), pipelineId, otherStageId);

        var response = await _client.PostAsJsonAsync("/deals", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PatchDealStage_ValidStageChange_InsertsStageChangeActivity()
    {
        var (pipelineId, stage1Id, stage2Id) = await SeedPipelineWithTwoStagesAsync();
        var ownerId = Guid.NewGuid();
        var createResp = await _client.PostAsJsonAsync("/deals",
            new CreateDealRequest("Activity Test Deal", 7500m, ownerId, pipelineId, stage1Id));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var deal = await createResp.Content.ReadFromJsonAsync<DealResponse>();
        Assert.NotNull(deal);

        var patchResp = await _client.PatchAsJsonAsync($"/deals/{deal.Id}/stage",
            new PatchDealStageRequest(stage2Id));

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var activities = await db.Activities.ToListAsync();
        Assert.Single(activities);
        Assert.Equal(ActivityType.StageChange, activities[0].Type);
        Assert.Equal(deal.Id, activities[0].DealId);
        Assert.Contains("Qualified", activities[0].Note);
    }
}
