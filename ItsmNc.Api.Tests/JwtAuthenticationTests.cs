using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace ItsmNc.Api.Tests;

[TestClass]
public sealed class JwtAuthenticationTests
{
    private KeycloakApiFactory factory = null!;
    private HttpClient client = null!;

    [TestInitialize]
    public void Initialize()
    {
        factory = new KeycloakApiFactory();
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        client.Dispose();
        factory.Dispose();
    }

    [TestMethod]
    public async Task OpenApiIsAvailableWithoutToken()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "OpenAPI должен быть доступен в среде разработки.");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("bearer", document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        var operation = document.GetProperty("paths").GetProperty("/api/auth/me").GetProperty("get");
        Assert.IsTrue(operation.GetProperty("security").EnumerateArray().Any(item => item.TryGetProperty("Bearer", out _)));
        Assert.IsTrue(operation.GetProperty("responses").TryGetProperty("401", out _));
        Assert.IsTrue(operation.GetProperty("responses").TryGetProperty("403", out _));
    }

    [TestMethod]
    public async Task MissingTokenReturns401WithoutRedirect()
    {
        using var response = await Me(null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, "Без токена требуется ответ 401.");
        Assert.IsNull(response.Headers.Location, "API не должен перенаправлять запрос на форму входа.");
        Assert.IsTrue(response.Headers.WwwAuthenticate.Any(header => header.Scheme == "Bearer"));
    }

    [TestMethod]
    public async Task MalformedTokenReturns401()
    {
        using var response = await Me("not-a-jwt");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("user")]
    [DataRow("moderator")]
    [DataRow("admin")]
    [DataRow("another-application-role")]
    public async Task AnyOwnClientRoleAllowsAccess(string role)
    {
        using var response = await Me(factory.Token(roles: [role]));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Доступ разрешён любой роли своего клиента.");
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("integration-subject", user.GetProperty("subject").GetString());
        Assert.AreEqual("integration-user", user.GetProperty("userName").GetString());
        Assert.AreEqual("integration-team", user.GetProperty("teamSlug").GetString());
        var roles = user.GetProperty("roles").EnumerateArray().Select(value => value.GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { role }, roles, "В ответ не должны попадать роли Service Desk.");
        Assert.IsFalse(user.TryGetProperty("passwordHash", out _));
    }

    [TestMethod]
    public async Task WrongIssuerReturns401()
    {
        using var response = await Me(factory.Token(issuer: "https://other.example/realms/training"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task WrongAudienceReturns401()
    {
        using var response = await Me(factory.Token(audience: "service-desk"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ExpiredTokenReturns401()
    {
        using var response = await Me(factory.Token(expired: true));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task FutureTokenReturns401()
    {
        using var response = await Me(factory.Token(future: true));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task TokenWithoutExpirationReturns401()
    {
        using var response = await Me(factory.Token(noExpiration: true));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task WrongSignatureReturns401()
    {
        using var response = await Me(factory.Token(wrongSignature: true));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task IdTokenIsNotAcceptedAsAccessToken()
    {
        using var response = await Me(factory.Token(tokenType: "ID"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MissingSubjectReturns401()
    {
        using var response = await Me(factory.Token(includeSubject: false));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task OtherClientRoleDoesNotGrantAccess()
    {
        using var response = await Me(factory.Token(otherClientOnly: true));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task FlatAndRealmRolesDoNotGrantAccess()
    {
        using var response = await Me(factory.Token(otherClientOnly: true, flatRole: true));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task EmptyOwnClientRolesReturn403()
    {
        using var response = await Me(factory.Token(roles: []));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task BlankOwnClientRolesReturn403()
    {
        using var response = await Me(factory.Token(roles: ["", " "]));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task MalformedRoleJsonReturns401InsteadOf500()
    {
        using var response = await Me(factory.Token(malformedRoles: true));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DuplicateRolesAreReturnedOnce()
    {
        using var response = await Me(factory.Token(roles: ["user", "user", "moderator"]));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        var roles = user.GetProperty("roles").EnumerateArray().Select(value => value.GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "moderator", "user" }, roles);
    }

    [TestMethod]
    public async Task UnsignedTokenReturns401()
    {
        using var response = await Me(factory.Token(unsigned: true));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task UnexpectedAlgorithmReturns401()
    {
        using var response = await Me(factory.Token(wrongAlgorithm: true));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<HttpResponseMessage> Me(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
}
