using System.Net;
using System.Net.Http.Json;
using Api.Features.Contacts;
using Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tests.Api;

public sealed class ContactsEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ContactsEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;");

                builder.ConfigureTestServices(services =>
                {
                    var dbName = "ContactsTests_" + Guid.NewGuid();

                    var optConfigType = typeof(IDbContextOptionsConfiguration<CrmDbContext>);
                    foreach (var d in services.Where(d => d.ServiceType == optConfigType).ToList())
                        services.Remove(d);
                    foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<CrmDbContext>)).ToList())
                        services.Remove(d);

                    services.AddDbContext<CrmDbContext>(o => o.UseInMemoryDatabase(dbName));
                });
            });
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task PostContact_ThenGetById_RoundTrip_ReturnsSameContact()
    {
        var req = new CreateContactRequest("Alice Smith", "alice@example.com", "+1-555-0101", null);
        var createResponse = await _client.PostAsJsonAsync("/contacts", req);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);

        var getResponse = await _client.GetAsync($"/contacts/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Alice Smith", fetched.Name);
        Assert.Equal("alice@example.com", fetched.Email);
        Assert.Equal("+1-555-0101", fetched.Phone);
        Assert.Null(fetched.CompanyId);
    }

    [Fact]
    public async Task GetContacts_AfterCreatingContacts_ReturnsList()
    {
        await _client.PostAsJsonAsync("/contacts", new CreateContactRequest("Bob Jones", "bob@example.com", null, null));
        await _client.PostAsJsonAsync("/contacts", new CreateContactRequest("Carol White", "carol@example.com", null, null));

        var response = await _client.GetAsync("/contacts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ContactResponse[]>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Length);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/contacts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostContact_MissingName_Returns422UnprocessableEntity()
    {
        var req = new CreateContactRequest("", "valid@example.com", null, null);

        var response = await _client.PostAsJsonAsync("/contacts", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
