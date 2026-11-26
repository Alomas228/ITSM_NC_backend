using ItsmNc.Api.Authentication;
using ItsmNc.Api.Data;
using ItsmNc.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Подключаем API-контроллеры и описание HTTP-контрактов.
builder.Services.AddControllers();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<JwtBearerDocumentTransformer>());

// Настройки PostgreSQL проверяются при первом обращении к контексту БД.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("AuthDatabase");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Не задана строка подключения ConnectionStrings:AuthDatabase. " +
            "Добавьте её в секреты пользователя или задайте переменную ConnectionStrings__AuthDatabase.");
    }

    options.UseNpgsql(connectionString);
});

// Подключаем управление учётными записями и их хранение через EF Core.
builder.Services.AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddKeycloakAuthentication(builder.Configuration);

var app = builder.Build();

// Документ OpenAPI доступен только в среде разработки.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("../openapi/v1.json", "ITSM NC API v1");
        options.DocumentTitle = "ITSM NC — проверка API";
        options.ConfigObject.PersistAuthorization = false;
        options.ConfigObject.ValidatorUrl = null;
        options.OAuthClientId(app.Configuration["Keycloak:ClientId"]!);
        options.OAuthAppName("ITSM NC Swagger");
        options.OAuthUsePkce();
        options.OAuthScopes("openid", "profile", "email");
        options.OAuthAdditionalQueryStringParams(new Dictionary<string, string>
        {
            ["prompt"] = "login"
        });
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
