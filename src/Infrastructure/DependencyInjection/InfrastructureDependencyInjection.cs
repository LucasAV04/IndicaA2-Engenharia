using Domain.Interfaces;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.DependencyInjection;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A connection string 'ConnectionStrings:DefaultConnection' é obrigatória.");
        }

        services.AddSingleton(new MySqlConnectionFactory(connectionString));
        services.AddScoped<IIndicacaoRepository, IndicacaoMySqlRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioMySqlRepository>();
        services.AddScoped<IVistoriaRepository, VistoriaMySqlRepository>();

        return services;
    }
}
