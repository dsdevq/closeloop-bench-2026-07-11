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

namespace Api.Tests.Features.Contacts;

public sealed class ContactsEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ContactsEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Satisfy the null-check guard in Program.cs without a real Postgres server.
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;");

                // ConfigureTestServices runs after Program.cs registers services.
                builder.ConfigureTestServices(services =>
                {
                    var dbName = "ContactsTest_" + Guid.NewGuid();

                    // Remove the Npgsql options action (UseNpgsql) registered by startup.
                    // IDbContextOptionsConfiguration<T> is the hook EF Core uses to apply
                    // the optionsAction; leaving it in place while also adding UseInMemoryDatabase
                    // causes a conflicting provider exception.
                    var optConfigType = typeof(IDbContextOptionsConfiguration<CrmDbContext>);
                    foreach (var d in services.Where(d => d.ServiceType == optConfigType).ToList())
                        services.Remove(d);

                    // Remove the resolved DbContextOptions<CrmDbContext> if already cached.
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
    public async Task GetContacts_EmptyDb_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/contacts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ContactResponse[]>();
        Assert.NotNull(body);
        Assert.Empty(body);
    }

    [Fact]
    public async Task PostContact_ValidRequest_Returns201WithCreatedResource()
    {
        var req = new CreateContactRequest("Alice Smith", "alice@example.com", "+1-555-0101", null);

        var response = await _client.PostAsJsonAsync("/contacts", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("Alice Smith", body.Name);
        Assert.Equal("alice@example.com", body.Email);
        Assert.Equal("+1-555-0101", body.Phone);
        Assert.Null(body.CompanyId);
    }

    [Fact]
    public async Task CreateThenGet_RoundTrip_ReturnsSameContact()
    {
        var req = new CreateContactRequest("Bob Jones", "bob@example.com", null, null);
        var createResponse = await _client.PostAsJsonAsync("/contacts", req);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(created);

        var getResponse = await _client.GetAsync($"/contacts/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Bob Jones", fetched.Name);
        Assert.Equal("bob@example.com", fetched.Email);
        Assert.Null(fetched.Phone);
    }

    [Fact]
    public async Task GetContact_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/contacts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostContact_BlankName_Returns422ValidationProblem()
    {
        var req = new CreateContactRequest("", "valid@example.com", null, null);

        var response = await _client.PostAsJsonAsync("/contacts", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostContact_InvalidEmail_Returns422ValidationProblem()
    {
        var req = new CreateContactRequest("Valid Name", "not-an-email", null, null);

        var response = await _client.PostAsJsonAsync("/contacts", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetContacts_AfterCreatingTwo_ReturnsBothContacts()
    {
        await _client.PostAsJsonAsync("/contacts", new CreateContactRequest("Carol", "carol@example.com", null, null));
        await _client.PostAsJsonAsync("/contacts", new CreateContactRequest("Dave", "dave@example.com", null, null));

        var response = await _client.GetAsync("/contacts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ContactResponse[]>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Length);
    }
}
