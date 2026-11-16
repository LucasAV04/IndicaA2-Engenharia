using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Application.Contas;
using Infrastructure.Email;
using Infrastructure.Repositories;
using Infrastructure.Security;

namespace API.Processing;

internal static class ConfiguracaoEmail
{
    internal static void Adicionar(WebApplicationBuilder builder)
    {
        var conf = builder.Configuration;
        if (!conf.GetValue<bool>("Email:Habilitado"))
        {
            if (conf.GetValue<bool>("Email:Worker:Habilitado"))
                throw new InvalidOperationException("Habilite a configuração de e-mail antes do worker.");
            return;
        }

        var options = conf.GetSection("Email").Get<EmailOptions>() ?? new();
        var local = builder.Environment.IsEnvironment("Homologation");
        if (!Uri.TryCreate(conf["PublicWeb:BaseUrl"], UriKind.Absolute, out var origem)
            || origem.Scheme != Uri.UriSchemeHttps || origem.UserInfo.Length + origem.Query.Length + origem.Fragment.Length != 0
            || origem.AbsolutePath != "/" || Uri.CheckHostName(options.Host) == UriHostNameType.Unknown
            || options.Port is < 1 or > 65535 || !MailAddress.TryCreate(options.Remetente, out var from)
            || from.Address != options.Remetente || options.Worker.IntervaloSegundos is < 1 or > 3600
            || options.Worker.TamanhoLote is < 1 or > 100
            || (options.Usuario.Length == 0) != (options.Senha.Length == 0)
            || (!local && !options.UsarTls)
            || (local && (options.Host != "mailpit" || options.Port != 1025 || from.Host != "example.invalid"
                || options.Usuario.Length != 0 || options.Senha.Length != 0)))
            throw new InvalidOperationException("Configuração de e-mail inválida.");

        var chave = conf["Email:TokenKey"];
        if (string.IsNullOrEmpty(chave) || !ChavesIndependentes(chave, conf))
            throw new InvalidOperationException("Configure uma chave de conta independente com 32 bytes.");

        builder.Services.AddSingleton(new TokenContaProtector(chave));
        builder.Services.AddScoped<IEmailSender>(_ => new SmtpEmailSender(options, local));
        if (options.Worker.Habilitado)
        {
            builder.Services.AddScoped<IEmailOutboxStore, EmailOutboxMySqlStore>();
            builder.Services.AddHostedService(sp => new EmailOutboxWorker(
                sp.GetRequiredService<IServiceScopeFactory>(), options.Worker, sp.GetRequiredService<TimeProvider>(),
                origem, sp.GetRequiredService<ILogger<EmailOutboxWorker>>()));
        }
    }

    private static bool ChavesIndependentes(string raw, IConfiguration conf)
    {
        Span<byte> bytes = stackalloc byte[32];
        if (!Convert.TryFromBase64String(raw, bytes, out var count) || count != bytes.Length
            || Convert.ToBase64String(bytes) != raw)
            return false;

        var outras = new[] { conf["Jwt:Key"], conf["DadosPixEncryption:Key"],
            Environment.GetEnvironmentVariable("INDICA2_DADOS_PIX_ENCRYPTION_KEY"),
            Environment.GetEnvironmentVariable("INDICA2_COBRANCA_PIX_ENCRYPTION_KEY"),
            conf["Homologation:ChargeKey"] };
        Span<byte> outra = stackalloc byte[32];
        foreach (var value in outras)
        {
            if (value is null) continue;
            if (CryptographicOperations.FixedTimeEquals(bytes, Encoding.UTF8.GetBytes(value))
                || (Convert.TryFromBase64String(value, outra, out var size) && size == 32
                    && CryptographicOperations.FixedTimeEquals(bytes, outra)))
                return false;
        }
        return true;
    }
}
