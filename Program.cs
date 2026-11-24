using NFirst.Services;
using Microsoft.EntityFrameworkCore;
using NFirst.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=nfirst.db"));
// Scoped porque o NotaService usa o AppDbContext, que também é scoped.
builder.Services.AddScoped<NotaService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
// Aplica as migrations pendentes ao iniciar, para o banco sempre estar atualizado.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
// Serve o front-end estático (wwwroot) e abre o index.html na raiz.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();