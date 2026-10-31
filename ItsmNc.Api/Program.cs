var builder = WebApplication.CreateBuilder(args);

// Подключаем API-контроллеры и описание HTTP-контрактов.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Документ OpenAPI доступен только в среде разработки.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();

    