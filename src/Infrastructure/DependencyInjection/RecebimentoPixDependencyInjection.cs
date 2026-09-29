using Application.Recebimentos;
using Infrastructure.Providers;
using Infrastructure.Repositories;
using Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.DependencyInjection;

public static class RecebimentoPixDependencyInjection
{
    public static IServiceCollection AddRecebimentoPix(this IServiceCollection services, IConfiguration configuration)
    {
        var options=configuration.GetSection("RecebimentoPix").Get<RecebimentoPixOptions>()??new();
        options.Validar();
        var key=Environment.GetEnvironmentVariable("INDICA2_COBRANCA_PIX_ENCRYPTION_KEY");
        var efi=configuration.GetSection("EfiPix").Get<EfiPixOptions>()??new();
        if(options.Habilitado)
        {
            efi.ValidarParaSandbox();
            using var validate=new CobrancaPixProtector(key??"");
            if(!File.Exists(options.WebhookCaPath)) throw new InvalidOperationException("CA externa do webhook ausente.");
            var authorities=new System.Security.Cryptography.X509Certificates.X509Certificate2Collection();
            try
            {
                authorities.ImportFromPemFile(options.WebhookCaPath);
                if (authorities.Count==0 || authorities.Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>().Any(a=>
                    !a.Extensions.OfType<System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension>().Any(e=>e.CertificateAuthority)))
                    throw new InvalidOperationException("CA externa do webhook inválida.");
            }
            finally { foreach(var authority in authorities) authority.Dispose(); }
            using var handler=EfiPixHttpMessageHandlerFactory.Criar(efi);
        }
        services.AddSingleton(options);
        services.AddSingleton(_=>new CobrancaPixProtector(key??throw new InvalidOperationException("Criptografia de cobrança não configurada.")));
        services.AddHttpClient(EfiCobrancaPixVistoriaProvider.HttpClientName)
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(()=>EfiPixHttpMessageHandlerFactory.Criar(efi));
        services.AddScoped<ICobrancaPixVistoriaProvider>(sp=>new EfiCobrancaPixVistoriaProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(EfiCobrancaPixVistoriaProvider.HttpClientName),
            efi,options,sp.GetRequiredService<TimeProvider>(),sp.GetRequiredService<EfiPixAccessTokenCache>()));
        services.AddScoped<CobrancaPixVistoriaMySqlStore>();
        services.AddScoped<ICobrancaPixVistoriaStore>(sp=>sp.GetRequiredService<CobrancaPixVistoriaMySqlStore>());
        services.AddScoped<IRecebimentoPixCandidatoStore>(sp=>sp.GetRequiredService<CobrancaPixVistoriaMySqlStore>());
        services.AddScoped<IRecebimentoPixWebhookStore,RecebimentoPixWebhookMySqlStore>();
        services.AddScoped<ICobrancaPixVistoriaService,CobrancaPixVistoriaService>();
        services.AddScoped<IRecebimentoPixProcessamentoService,RecebimentoPixProcessamentoService>();
        return services;
    }
}
