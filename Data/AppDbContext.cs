using Microsoft.EntityFrameworkCore;

namespace NFirst.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<NotaFiscal> Notas { get; set; }
}