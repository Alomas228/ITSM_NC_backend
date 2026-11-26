using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Microsoft.Extensions.Options;

namespace ItsmNc.Api.Authentication;

public sealed class JwtBearerDocumentTransformer(IOptions<KeycloakOptions> settings) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Токен доступа Keycloak для клиента student-service."
        };

        var authority = settings.Value.Authority.TrimEnd('/');
        document.Components.SecuritySchemes["Keycloak"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "Вход через готовую форму Keycloak. Пароль вводится только у провайдера; client_secret не нужен.",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{authority}/protocol/openid-connect/auth"),
                    TokenUrl = new Uri($"{authority}/protocol/openid-connect/token"),
                    Scopes = new Dictionary<string, string>
                    {
                        ["openid"] = "Вход через Keycloak",
                        ["profile"] = "Данные пользователя",
                        ["email"] = "Адрес электронной почты"
                    }
                }
            }
        };
        // API защищён общей политикой; документ OpenAPI доступен отдельно в Development.
        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations?.Values.AsEnumerable() ?? Enumerable.Empty<OpenApiOperation>()))
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = []
            });
            // Отдельные требования обозначают альтернативные способы передать один access token.
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Keycloak", document)] = ["openid", "profile", "email"]
            });
        }

        return Task.CompletedTask;
    }
}
