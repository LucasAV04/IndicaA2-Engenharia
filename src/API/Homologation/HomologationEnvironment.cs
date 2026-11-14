using System.Net;
using System.Security.Cryptography.X509Certificates;
using Infrastructure.Database;
using MySqlConnector;

namespace API.Homologation;

internal static class HomologationEnvironment
{
    internal static readonly string[] DisabledFlags =
    [
        "PagamentoPix:ProcessamentoWorker:Habilitado",
        "RecebimentoPix:ProcessamentoWorker:Habilitado",
        "CashbackPagamentoPreparacaoWorker:Habilitado",
        "RecebimentoPix:Habilitado"
    ];

    internal static void Validate(IConfiguration configuration)
    {
        if (DisabledFlags.Any(key => configuration.GetValue<bool>(key))
            || configuration.GetSection("EfiPix").GetChildren().Any(item => !string.IsNullOrWhiteSpace(item.Value)))
            throw new InvalidOperationException("Integrações financeiras devem permanecer desabilitadas.");

        if (!Uri.TryCreate(configuration["PublicWeb:BaseUrl"], UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.Host != "localhost" || url.AbsolutePath != "/"
            || url.UserInfo.Length + url.Query.Length + url.Fragment.Length != 0)
            throw new InvalidOperationException("Configure uma origem HTTPS local.");

        var database = new MySqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection") ?? "");
        if (database.Server != "mysql" || database.Port != 3306 || database.Database != "indicaa2_homologacao"
            || database.UserID != "indicaa2")
            throw new InvalidOperationException("Banco fora do ambiente de homologação.");

        var values = new[]
        {
            database.Password, configuration["Jwt:Key"], configuration["DadosPixEncryption:Key"],
            configuration["Homologation:ChargeKey"], configuration["Homologation:CertificatePassword"]
        };
        if (values.Any(value => value is null || value.Length < 32 || value.Distinct().Count() < 12
                || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
                || value.Contains("change-me", StringComparison.OrdinalIgnoreCase))
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length
            || values.Skip(1).Take(3).Any(value => !IsKey(value!)))
            throw new InvalidOperationException("Segredos de homologação inválidos.");
    }

    internal static void Configure(WebApplicationBuilder builder)
    {
        var settings = new ConfigurationBuilder().AddJsonFile("/run/secrets/application.json").Build();
        // Runtime flags remain subject to validation. Only the listed secrets come from the mount.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = new MySqlConnectionStringBuilder
            {
                Server = "mysql", Database = "indicaa2_homologacao", UserID = "indicaa2",
                Password = settings["MySqlPassword"], ConnectionTimeout = 3, DefaultCommandTimeout = 15,
                SslMode = MySqlSslMode.Required
            }.ConnectionString,
            ["Jwt:Key"] = settings["JwtKey"],
            ["DadosPixEncryption:Key"] = settings["PixKey"],
            ["Homologation:ChargeKey"] = settings["ChargeKey"],
            ["Homologation:CertificatePassword"] = settings["CertificatePassword"]
        });
        Validate(builder.Configuration);
        builder.Services.AddSingleton(new MigrationJournal(Path.Combine(AppContext.BaseDirectory, "database")));
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.AddServerHeader = false;
            server.Listen(IPAddress.Any, 8443, endpoint => endpoint.UseHttps("/run/secrets/localhost.pfx",
                settings["CertificatePassword"]));
        });
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
    }

    internal static async Task RunAdminAsync(WebApplicationBuilder builder, string command, CancellationToken token)
    {
        if (!builder.Environment.IsEnvironment("Homologation"))
            throw new InvalidOperationException("Comando permitido apenas em Homologation.");
        Configure(builder);
        if (command == "--preflight-homologation")
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                "/run/secrets/localhost.pfx", builder.Configuration["Homologation:CertificatePassword"]);
            if (!certificate.HasPrivateKey || certificate.NotAfter <= DateTime.Now)
                throw new InvalidOperationException("Certificado local inválido.");
            return;
        }

        var database = new MySqlConnectionStringBuilder(builder.Configuration.GetConnectionString("DefaultConnection") ?? "")
        {
            Pooling = false
        };
        var factory = new MySqlConnectionFactory(database.ConnectionString);
        var journal = new MigrationJournal(Path.Combine(AppContext.BaseDirectory, "database"));
        await using var connection = factory.Create();
        await connection.OpenAsync(token);
        if (command == "--migrate-homologation")
            await journal.ApplyAsync(connection, token);
        else if (command == "--bootstrap-homologation")
        {
            var admin = new ConfigurationBuilder().AddJsonFile("/run/secrets/admin.json").Build();
            await HomologationBootstrap.CreateAsync(connection, journal, factory,
                admin["Name"] ?? "", admin["Email"] ?? "", admin["Password"] ?? "", token);
        }
        else
            throw new InvalidOperationException("Comando administrativo inválido.");
    }

    private static bool IsKey(string value)
    {
        Span<byte> bytes = stackalloc byte[32];
        return Convert.TryFromBase64String(value, bytes, out var written) && written == bytes.Length
            && Convert.ToBase64String(bytes) == value;
    }
}
