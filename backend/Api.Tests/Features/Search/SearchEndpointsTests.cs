using System.Net;
using System.Net.Http.Json;
using Api.Features.Search;
using Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Features.Search;

public sealed class SearchEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SearchEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;");

                builder.ConfigureTestServices(services =>
                {
                    var dbName = "SearchTest_" + Guid.NewGuid();

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
    public async Task Search_EmptyQuery_Returns422()
    {
        var response = await _client.GetAsync("/search?q=");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Search_MissingQuery_Returns422()
    {
        var response = await _client.GetAsync("/search");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmptyGroupsAndZeroTotalHits()
    {
        var response = await _client.GetAsync("/search?q=zzznomatch");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Empty(body.Contacts);
        Assert.Empty(body.Companies);
        Assert.Empty(body.Deals);
        Assert.Equal(0, body.TotalHits);
    }

    [Fact]
    public async Task Search_ContactByName_ReturnsMatchingContact()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        db.Contacts.Add(Contact.Create("Alice Wonderland", "alice@example.com", null, null, Guid.NewGuid()));
        db.Contacts.Add(Contact.Create("Bob Smith", "bob@example.com", null, null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/search?q=alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Single(body.Contacts);
        Assert.Equal("Alice Wonderland", body.Contacts[0].Name);
        Assert.Empty(body.Companies);
        Assert.Empty(body.Deals);
        Assert.Equal(1, body.TotalHits);
    }

    [Fact]
    public async Task Search_ContactByEmail_ReturnsMatchingContact()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        db.Contacts.Add(Contact.Create("Test User", "uniqueaddr@corp.io", null, null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/search?q=uniqueaddr");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Single(body.Contacts);
        Assert.Equal("uniqueaddr@corp.io", body.Contacts[0].Email);
        Assert.Equal(1, body.TotalHits);
    }

    [Fact]
    public async Task Search_CaseInsensitive_ReturnsMatchRegardlessOfCase()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        db.Contacts.Add(Contact.Create("UPPERCASE NAME", "upper@example.com", null, null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/search?q=uppercase");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Single(body.Contacts);
    }

    [Fact]
    public async Task Search_CompanyByName_ReturnsMatchingCompany()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        db.Companies.Add(Company.Create("Acme Corp", "acme.com", "Manufacturing", Guid.NewGuid()));
        db.Companies.Add(Company.Create("Globex Inc", "globex.com", "Energy", Guid.NewGuid()));
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/search?q=acme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Empty(body.Contacts);
        Assert.Single(body.Companies);
        Assert.Equal("Acme Corp", body.Companies[0].Name);
        Assert.Equal(1, body.TotalHits);
    }

    [Fact]
    public async Task Search_CompanyByDomain_ReturnsMatchingCompany()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        db.Companies.Add(Company.Create("Some Company", "uniquedomain.io", null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/search?q=uniquedomain");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Single(body.Companies);
        Assert.Equal(1, body.TotalHits);
    }

    [Fact]
    public async Task Search_DealByTitle_ReturnsMatchingDeal()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospecting", order: 0);
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync();
        var stage = pipeline.Stages[0];

        db.Deals.Add(Deal.Create("Enterprise License Deal", 50_000m, Guid.NewGuid(), pipeline.Id, stage.Id));
        db.Deals.Add(Deal.Create("SMB Starter Pack", 5_000m, Guid.NewGuid(), pipeline.Id, stage.Id));
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/search?q=enterprise");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Empty(body.Contacts);
        Assert.Empty(body.Companies);
        Assert.Single(body.Deals);
        Assert.Equal("Enterprise License Deal", body.Deals[0].Title);
        Assert.Equal(1, body.TotalHits);
    }

    [Fact]
    public async Task Search_AcrossMultipleTypes_ReturnsCombinedResultsAndCorrectTotalHits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        db.Contacts.Add(Contact.Create("Apex Contact", "apex@example.com", null, null, Guid.NewGuid()));
        db.Companies.Add(Company.Create("Apex Corp", "apex.com", null, Guid.NewGuid()));

        var pipeline = Pipeline.Create("Pipeline");
        pipeline.AddStage("Stage", order: 0);
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync();

        db.Deals.Add(Deal.Create("Apex Enterprise Deal", 100m, Guid.NewGuid(), pipeline.Id, pipeline.Stages[0].Id));
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/search?q=apex");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Single(body.Contacts);
        Assert.Single(body.Companies);
        Assert.Single(body.Deals);
        Assert.Equal(3, body.TotalHits);
    }

    /// <summary>
    /// Regression test: TotalHits must reflect the true uncapped match count, not the
    /// capped list size. When 15 contacts match and the list is capped at 10, TotalHits
    /// must be 15 — not 10 — so the client can offer a "see all 15 contacts" affordance.
    /// </summary>
    [Fact]
    public async Task Search_MoreThan10MatchingContacts_TotalHitsReflectsTrueCountNotCappedList()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        for (var i = 1; i <= 15; i++)
            db.Contacts.Add(Contact.Create($"Searchable Person {i:D2}", $"search{i}@example.com", null, null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/search?q=searchable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Equal(10, body.Contacts.Count);   // capped at 10 in the returned list
        Assert.Equal(15, body.TotalHits);         // true total, not the capped list length
    }

    [Fact]
    public async Task Search_MoreThan10MatchingDeals_TotalHitsReflectsTrueCountNotCappedList()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var pipeline = Pipeline.Create("Cap Test Pipeline");
        pipeline.AddStage("Stage", order: 0);
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync();

        for (var i = 1; i <= 12; i++)
            db.Deals.Add(Deal.Create($"CapDeal {i:D2}", 0m, Guid.NewGuid(), pipeline.Id, pipeline.Stages[0].Id));
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/search?q=capdeal");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(body);
        Assert.Equal(10, body.Deals.Count);
        Assert.Equal(12, body.TotalHits);
    }
}
