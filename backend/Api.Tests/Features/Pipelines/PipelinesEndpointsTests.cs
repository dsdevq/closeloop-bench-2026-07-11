using System.Net;
using System.Net.Http.Json;
using Api.Features.Pipelines;
using Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Features.Pipelines;

public sealed class PipelinesEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PipelinesEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;");

                builder.ConfigureTestServices(services =>
                {
                    var dbName = "PipelinesTest_" + Guid.NewGuid();

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

    [Fact]
    public async Task GetPipelines_EmptyDb_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/pipelines");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PipelineResponse[]>();
        Assert.NotNull(body);
        Assert.Empty(body);
    }

    [Fact]
    public async Task PostPipeline_ValidRequest_Returns201WithCreatedResource()
    {
        var req = new CreatePipelineRequest("Sales Pipeline");

        var response = await _client.PostAsJsonAsync("/pipelines", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PipelineResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("Sales Pipeline", body.Name);
        Assert.Empty(body.Stages);
    }

    [Fact]
    public async Task PostPipeline_WithStages_Returns201WithStages()
    {
        var req = new CreatePipelineRequest("Renewal Pipeline", new[]
        {
            new CreatePipelineStageRequest("Prospect", 0, 10),
            new CreatePipelineStageRequest("Closed Won", 1, 100),
        });

        var response = await _client.PostAsJsonAsync("/pipelines", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PipelineResponse>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Stages.Count);
        Assert.Equal("Prospect", body.Stages[0].Name);
        Assert.Equal(0, body.Stages[0].Order);
        Assert.Equal(10, body.Stages[0].WinProbability);
        Assert.Equal("Closed Won", body.Stages[1].Name);
        Assert.Equal(100, body.Stages[1].WinProbability);
    }

    [Fact]
    public async Task CreateThenGet_RoundTrip_ReturnsSamePipeline()
    {
        var req = new CreatePipelineRequest("Enterprise Pipeline", new[]
        {
            new CreatePipelineStageRequest("Discovery", 0, 20),
            new CreatePipelineStageRequest("Proposal", 1, 60),
        });
        var createResponse = await _client.PostAsJsonAsync("/pipelines", req);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<PipelineResponse>();
        Assert.NotNull(created);

        var getResponse = await _client.GetAsync($"/pipelines/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<PipelineResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Enterprise Pipeline", fetched.Name);
        Assert.Equal(2, fetched.Stages.Count);
        Assert.Equal("Discovery", fetched.Stages[0].Name);
    }

    [Fact]
    public async Task GetPipeline_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/pipelines/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostPipeline_BlankName_Returns422ValidationProblem()
    {
        var req = new CreatePipelineRequest("");

        var response = await _client.PostAsJsonAsync("/pipelines", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostPipeline_DuplicateStageOrder_Returns422ValidationProblem()
    {
        var req = new CreatePipelineRequest("Broken Pipeline", new[]
        {
            new CreatePipelineStageRequest("Stage A", 0),
            new CreatePipelineStageRequest("Stage B", 0),
        });

        var response = await _client.PostAsJsonAsync("/pipelines", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetPipelines_AfterCreatingTwo_ReturnsBoth()
    {
        await _client.PostAsJsonAsync("/pipelines", new CreatePipelineRequest("Alpha Pipeline"));
        await _client.PostAsJsonAsync("/pipelines", new CreatePipelineRequest("Beta Pipeline"));

        var response = await _client.GetAsync("/pipelines");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PipelineResponse[]>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Length);
    }
}
