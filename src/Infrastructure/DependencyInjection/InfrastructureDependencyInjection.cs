using Domain.Interfaces;
using Infrastructure.Database;
using Infrastructure.Providers;
using Infrastructure.Repositories;
using Infrastructure.Security;
using Application.Interfaces.Providers;
using Application.Interfaces.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        services.Configure<EfiPixOptions>(configuration.GetSection(EfiPixOptions.SectionName));
        services.AddSingleton<EfiPixAccessTokenCache>();
        services.AddHttpClient(EfiPixProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
                EfiPixHttpMessageHandlerFactory.Criar(
                    serviceProvider.GetRequiredService<IOptions<EfiPixOptions>>().Value));
        services.AddScoped<IPixProvider>(serviceProvider =>
            new EfiPixProvider(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(EfiPixProvider.HttpClientName),
                serviceProvider.GetRequiredService<IOptions<EfiPixOptions>>().Value,
                serviceProvider.GetRequiredService<EfiPixAccessTokenCache>()));
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<ICodigoIndicacaoGenerator, CodigoIndicacaoGenerator>();
        services.AddScoped<IAccessTokenGenerator, JwtAccessTokenGenerator>();
        services.AddSingleton<IDadosPixProtector>(_ =>
        {
            var encryptionKey = configuration["DadosPixEncryption:Key"]
                ?? Environment.GetEnvironmentVariable("INDICA2_DADOS_PIX_ENCRYPTION_KEY");

            if (string.IsNullOrWhiteSpace(encryptionKey))
            {
                throw new InvalidOperationException(
                    "Configure a chave externa de criptografia dos Dados Pix antes de utilizar sua persistência.");
            }

            return new AesGcmDadosPixProtector(encryptionKey);
        });
        services.AddScoped<IIndicacaoRepository, IndicacaoMySqlRepository>();
        services.AddScoped<ICashbackRepository, CashbackMySqlRepository>();
        services.AddScoped<IPagamentoPixRepository, PagamentoPixMySqlRepository>();
        services.AddScoped<IPagamentoVistoriaRepository, PagamentoVistoriaMySqlRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioMySqlRepository>();
        services.AddScoped<IVistoriaRepository, VistoriaMySqlRepository>();
        services.AddScoped<IDadosPixRepository, DadosPixMySqlRepository>();

        return services;
    }
}
