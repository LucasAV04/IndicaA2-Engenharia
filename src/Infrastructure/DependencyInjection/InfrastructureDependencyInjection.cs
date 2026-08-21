using Domain.Interfaces;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Infrastructure.Security;
using Application.Interfaces.Security;
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
        var jwtOptions = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        jwtOptions.Validate();

        services.AddSingleton(new MySqlConnectionFactory(connectionString));
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<ICodigoIndicacaoGenerator, CodigoIndicacaoGenerator>();
        services.AddScoped<IAccessTokenGenerator, JwtAccessTokenGenerator>();
        services.AddScoped<IIndicacaoRepository, IndicacaoMySqlRepository>();
        services.AddScoped<IPagamentoVistoriaRepository, PagamentoVistoriaMySqlRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioMySqlRepository>();
        services.AddScoped<IVistoriaRepository, VistoriaMySqlRepository>();

        return services;
    }
}
