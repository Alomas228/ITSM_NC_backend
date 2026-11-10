using ItsmNc.Api.Data;
using ItsmNc.Api.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Подключаем API-контроллеры и описание HTTP-контрактов.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

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
    .AddEntityFrameworkStores<ApplicationDbContext>();

var app = builder.Build();

// Документ OpenAPI доступен только в среде разработки.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
