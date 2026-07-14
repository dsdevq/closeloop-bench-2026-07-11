using System.Net;
using System.Net.Http.Json;
using Api.Features.Companies;
using Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Features.Companies;

public sealed class CompaniesEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CompaniesEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;");

                builder.ConfigureTestServices(services =>
                {
                    var dbName = "CompaniesTest_" + Guid.NewGuid();

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
    public async Task GetCompanies_EmptyDb_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/companies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse[]>();
        Assert.NotNull(body);
        Assert.Empty(body);
    }

    [Fact]
    public async Task PostCompany_ValidRequest_Returns201WithCreatedResource()
    {
        var ownerId = Guid.NewGuid();
        var req = new CreateCompanyRequest("Acme Corp", "acme.com", "Technology", ownerId);

        var response = await _client.PostAsJsonAsync("/companies", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("Acme Corp", body.Name);
        Assert.Equal("acme.com", body.Domain);
        Assert.Equal("Technology", body.Industry);
        Assert.Equal(ownerId, body.OwnerId);
    }

    [Fact]
    public async Task PostCompany_NullOptionalFields_Returns201()
    {
        var ownerId = Guid.NewGuid();
        var req = new CreateCompanyRequest("Private Co", null, null, ownerId);

        var response = await _client.PostAsJsonAsync("/companies", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse>();
        Assert.NotNull(body);
        Assert.Null(body.Domain);
        Assert.Null(body.Industry);
    }

    [Fact]
    public async Task CreateThenGet_RoundTrip_ReturnsSameCompany()
    {
        var ownerId = Guid.NewGuid();
        var req = new CreateCompanyRequest("GlobalTech", "globaltech.io", "Software", ownerId);
        var createResponse = await _client.PostAsJsonAsync("/companies", req);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CompanyResponse>();
        Assert.NotNull(created);

        var getResponse = await _client.GetAsync($"/companies/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<CompanyResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("GlobalTech", fetched.Name);
        Assert.Equal("globaltech.io", fetched.Domain);
        Assert.Equal("Software", fetched.Industry);
    }

    [Fact]
    public async Task GetCompany_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/companies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostCompany_BlankName_Returns422ValidationProblem()
    {
        var req = new CreateCompanyRequest("", null, null, Guid.NewGuid());

        var response = await _client.PostAsJsonAsync("/companies", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostCompany_EmptyOwnerId_Returns422ValidationProblem()
    {
        var req = new CreateCompanyRequest("Valid Corp", null, null, Guid.Empty);

        var response = await _client.PostAsJsonAsync("/companies", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetCompanies_AfterCreatingTwo_ReturnsBothCompanies()
    {
        var ownerId = Guid.NewGuid();
        await _client.PostAsJsonAsync("/companies", new CreateCompanyRequest("Alpha Inc", null, null, ownerId));
        await _client.PostAsJsonAsync("/companies", new CreateCompanyRequest("Beta Ltd", null, null, ownerId));

        var response = await _client.GetAsync("/companies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse[]>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Length);
    }

    [Fact]
    public async Task PostContact_WithValidCompanyId_LinksContactToCompany()
    {
        // Verify the Contact→Company FK is functional end-to-end.
        var ownerId = Guid.NewGuid();
        var companyResp = await _client.PostAsJsonAsync("/companies",
            new CreateCompanyRequest("Linked Corp", null, null, ownerId));
        Assert.Equal(HttpStatusCode.Created, companyResp.StatusCode);
        var company = await companyResp.Content.ReadFromJsonAsync<CompanyResponse>();
        Assert.NotNull(company);

        var contactResp = await _client.PostAsJsonAsync("/contacts",
            new Api.Features.Contacts.CreateContactRequest("Eve", "eve@linked.com", null, company.Id));
        Assert.Equal(HttpStatusCode.Created, contactResp.StatusCode);
        var contact = await contactResp.Content.ReadFromJsonAsync<Api.Features.Contacts.ContactResponse>();
        Assert.NotNull(contact);
        Assert.Equal(company.Id, contact.CompanyId);
    }
}
