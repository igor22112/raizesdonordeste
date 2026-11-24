using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RaizesDoNordeste.Infrastructure.Persistencia;

/// <summary>Usada apenas pelo dotnet-ef para gerar migrações (não abre conexão).</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=RaizesDoNordeste;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options, TimeProvider.System);
    }
}
