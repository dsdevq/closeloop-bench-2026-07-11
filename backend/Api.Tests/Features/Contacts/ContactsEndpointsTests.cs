using System.Net;
using System.Net.Http.Json;
using Api.Features.Contacts;
using Domain.Entities;
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
        var ownerId = Guid.NewGuid();
        var req = new CreateContactRequest("Alice Smith", "alice@example.com", "+1-555-0101", null, ownerId);

        var response = await _client.PostAsJsonAsync("/contacts", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("Alice Smith", body.Name);
        Assert.Equal("alice@example.com", body.Email);
        Assert.Equal("+1-555-0101", body.Phone);
        Assert.Null(body.CompanyId);
        Assert.Equal(ownerId, body.OwnerId);
    }

    [Fact]
    public async Task CreateThenGet_RoundTrip_ReturnsSameContact()
    {
        var ownerId = Guid.NewGuid();
        var req = new CreateContactRequest("Bob Jones", "bob@example.com", null, null, ownerId);
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
        var req = new CreateContactRequest("", "valid@example.com", null, null, Guid.NewGuid());

        var response = await _client.PostAsJsonAsync("/contacts", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostContact_InvalidEmail_Returns422ValidationProblem()
    {
        var req = new CreateContactRequest("Valid Name", "not-an-email", null, null, Guid.NewGuid());

        var response = await _client.PostAsJsonAsync("/contacts", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostContact_EmptyOwnerId_Returns422ValidationProblem()
    {
        var req = new CreateContactRequest("Valid Name", "valid@example.com", null, null, Guid.Empty);

        var response = await _client.PostAsJsonAsync("/contacts", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetContacts_AfterCreatingTwo_ReturnsBothContacts()
    {
        await _client.PostAsJsonAsync("/contacts", new CreateContactRequest("Carol", "carol@example.com", null, null, Guid.NewGuid()));
        await _client.PostAsJsonAsync("/contacts", new CreateContactRequest("Dave", "dave@example.com", null, null, Guid.NewGuid()));

        var response = await _client.GetAsync("/contacts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ContactResponse[]>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Length);
    }

    [Fact]
    public async Task PatchContactOwner_NewOwner_Returns200AndFiresContactAssignedNotification()
    {
        var originalOwnerId = Guid.NewGuid();
        var newOwnerId = Guid.NewGuid();
        var createResp = await _client.PostAsJsonAsync("/contacts",
            new CreateContactRequest("Eve Adams", "eve@example.com", null, null, originalOwnerId));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var contact = await createResp.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(contact);

        var patchResp = await _client.PatchAsJsonAsync($"/contacts/{contact.Id}/owner",
            new PatchContactOwnerRequest(newOwnerId));

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var body = await patchResp.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(body);
        Assert.Equal(newOwnerId, body.OwnerId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var notifications = await db.Notifications.ToListAsync();
        Assert.Single(notifications);
        Assert.Equal(NotificationTrigger.ContactAssigned, notifications[0].Trigger);
        Assert.Equal(newOwnerId, notifications[0].RecipientUserId);
        Assert.Equal(contact.Id, notifications[0].RelatedEntityId);
    }

    [Fact]
    public async Task PatchContactOwner_SameOwner_Returns200AndNoNotificationCreated()
    {
        var ownerId = Guid.NewGuid();
        var createResp = await _client.PostAsJsonAsync("/contacts",
            new CreateContactRequest("Frank Brown", "frank@example.com", null, null, ownerId));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var contact = await createResp.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(contact);

        // PATCH with the same owner — must not create a spurious notification
        var patchResp = await _client.PatchAsJsonAsync($"/contacts/{contact.Id}/owner",
            new PatchContactOwnerRequest(ownerId));

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var body = await patchResp.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(body);
        Assert.Equal(ownerId, body.OwnerId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var notifications = await db.Notifications.ToListAsync();
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task PatchContactOwner_UnknownContact_Returns404()
    {
        var patchResp = await _client.PatchAsJsonAsync($"/contacts/{Guid.NewGuid()}/owner",
            new PatchContactOwnerRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, patchResp.StatusCode);
    }

    [Fact]
    public async Task PatchContactOwner_EmptyOwnerId_Returns422()
    {
        var ownerId = Guid.NewGuid();
        var createResp = await _client.PostAsJsonAsync("/contacts",
            new CreateContactRequest("Grace Hall", "grace@example.com", null, null, ownerId));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var contact = await createResp.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.NotNull(contact);

        var patchResp = await _client.PatchAsJsonAsync($"/contacts/{contact.Id}/owner",
            new PatchContactOwnerRequest(Guid.Empty));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, patchResp.StatusCode);
    }

    [Fact]
    public async Task PostContact_ThenGetById_RoundTrip_ReturnsSameContact()
    {
        var req = new CreateContactRequest("Alice Smith", "alice@example.com", "+1-555-0101", null, Guid.NewGuid());
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
        await _client.PostAsJsonAsync("/contacts", new CreateContactRequest("Bob Jones", "bob@example.com", null, null, Guid.NewGuid()));
        await _client.PostAsJsonAsync("/contacts", new CreateContactRequest("Carol White", "carol@example.com", null, null, Guid.NewGuid()));

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
        var req = new CreateContactRequest("", "valid@example.com", null, null, Guid.NewGuid());

        var response = await _client.PostAsJsonAsync("/contacts", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
