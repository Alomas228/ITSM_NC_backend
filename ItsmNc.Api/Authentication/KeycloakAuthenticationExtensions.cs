using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ItsmNc.Api.Authentication;

public static class KeycloakAuthenticationExtensions
{
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetRequiredSection(KeycloakOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => Uri.TryCreate(options.Authority, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && string.IsNullOrEmpty(uri.UserInfo)
                && string.IsNullOrEmpty(uri.Query)
                && string.IsNullOrEmpty(uri.Fragment),
                "Keycloak:Authority должен быть абсолютным HTTPS-адресом без реквизитов доступа и параметров.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<KeycloakOptions>>((options, settings) =>
            {
                var keycloak = settings.Value;
                options.Authority = keycloak.Authority.TrimEnd('/');
                options.Audience = keycloak.Audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority,
                    ValidateAudience = true,
                    ValidAudience = keycloak.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateAccessToken
                };
            });

        services.AddAuthorization(options =>
        {
            // Для доступа нужна хотя бы одна роль именно клиента нашего приложения.
            var access = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ClaimTypes.Role)
                .Build();
            options.DefaultPolicy = access;
            options.FallbackPolicy = access;
        });

        return services;
    }

    private static Task ValidateAccessToken(TokenValidatedContext context)
    {
        var settings = context.HttpContext.RequestServices.GetRequiredService<IOptions<KeycloakOptions>>().Value;
        var principal = context.Principal;
        if (principal?.Identity is not ClaimsIdentity identity
            || string.IsNullOrWhiteSpace(principal.FindFirstValue("sub"))
            || principal.FindFirstValue("typ") != "Bearer"
            || principal.FindFirstValue("iss") != settings.Authority.TrimEnd('/'))
        {
            context.Fail("Требуется access token доверенного Keycloak с идентификатором пользователя.");
            return Task.CompletedTask;
        }

        // Плоские роли и роли других клиентов не дают права в нашем API.
        foreach (var claim in identity.FindAll(ClaimTypes.Role).ToArray())
            identity.RemoveClaim(claim);

        var resourceAccess = principal.FindFirstValue("resource_access");
        if (resourceAccess is null)
            return Task.CompletedTask;

        try
        {
            using var document = JsonDocument.Parse(resourceAccess);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(settings.ClientId, out var client)
                || client.ValueKind != JsonValueKind.Object
                || !client.TryGetProperty("roles", out var roles)
                || roles.ValueKind != JsonValueKind.Array)
                return Task.CompletedTask;

            var names = roles.EnumerateArray()
                .Where(role => role.ValueKind == JsonValueKind.String)
                .Select(role => role.GetString())
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.Ordinal);

            foreach (var role in names)
                identity.AddClaim(new Claim(ClaimTypes.Role, role!, ClaimValueTypes.String, settings.Authority.TrimEnd('/')));
        }
        catch (JsonException)
        {
            context.Fail("Некорректный формат ролей в токене.");
        }

        return Task.CompletedTask;
    }
}
