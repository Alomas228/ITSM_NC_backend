using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ItsmNc.Api.Tests;

internal sealed class KeycloakApiFactory : WebApplicationFactory<Program>
{
    internal const string Issuer = "https://auth.makar-star.space/realms/training";
    internal const string ClientId = "student-service";
    private readonly RSA rsa = RSA.Create(2048);
    private readonly RsaSecurityKey key;

    public KeycloakApiFactory()
    {
        key = new RsaSecurityKey(rsa) { KeyId = "integration-test-key" };
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = Issuer,
                ["Keycloak:Audience"] = ClientId,
                ["Keycloak:ClientId"] = ClientId
            }));
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Подменяется только источник OIDC-метаданных и публичного ключа.
                // Проверку подписи и остальные проверки выполняет настоящий JwtBearerHandler.
                var http = new HttpClient(new MetadataHandler(key));
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    Issuer + "/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever(http) { RequireHttps = true });
            });
        });
    }

    internal string Token(
        string[]? roles = null,
        string? issuer = null,
        string? audience = null,
        bool expired = false,
        bool wrongSignature = false,
        bool includeSubject = true,
        string tokenType = "Bearer",
        bool otherClientOnly = false,
        bool flatRole = false,
        bool malformedRoles = false,
        bool future = false,
        bool noExpiration = false,
        bool unsigned = false,
        bool wrongAlgorithm = false)
    {
        var claims = new List<Claim>
        {
            new("preferred_username", "integration-user"),
            new("team_slug", "integration-team"),
            new("typ", tokenType),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        if (includeSubject) claims.Add(new("sub", "integration-subject"));
        if (flatRole)
        {
            claims.Add(new(ClaimTypes.Role, "admin"));
            claims.Add(new("realm_access", JsonSerializer.Serialize(new { roles = new[] { "admin" } }), JsonClaimValueTypes.Json));
        }

        if (malformedRoles) claims.Add(new("resource_access", "{broken-json"));
        else
        {
            var access = new Dictionary<string, object>
            {
                ["service-desk"] = new { roles = new[] { "admin" } }
            };
            if (!otherClientOnly) access[ClientId] = new { roles = roles ?? ["user"] };
            claims.Add(new("resource_access", JsonSerializer.Serialize(access), JsonClaimValueTypes.Json));
        }

        var now = DateTime.UtcNow;
        using var otherRsa = wrongSignature ? RSA.Create(2048) : null;
        var signingKey = wrongSignature
            ? new RsaSecurityKey(otherRsa!) { KeyId = key.KeyId }
            : key;
        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? ClientId,
            claims: claims,
            notBefore: future ? now.AddMinutes(5) : now.AddMinutes(-20),
            expires: noExpiration ? null : expired ? now.AddMinutes(-5) : now.AddMinutes(10),
            signingCredentials: unsigned ? null : wrongAlgorithm
                ? new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = key.KeyId }, SecurityAlgorithms.HmacSha256)
                : new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) rsa.Dispose();
    }

    private sealed class MetadataHandler(RsaSecurityKey key) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object body;
            if (request.RequestUri?.AbsoluteUri == Issuer + "/.well-known/openid-configuration")
            {
                body = new { issuer = Issuer, jwks_uri = Issuer + "/protocol/openid-connect/certs" };
            }
            else if (request.RequestUri?.AbsoluteUri == Issuer + "/protocol/openid-connect/certs")
            {
                var parameters = key.Rsa.ExportParameters(false);
                body = new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA", use = "sig", kid = key.KeyId, alg = "RS256",
                            n = Base64UrlEncoder.Encode(parameters.Modulus),
                            e = Base64UrlEncoder.Encode(parameters.Exponent)
                        }
                    }
                };
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            });
        }
    }
}
