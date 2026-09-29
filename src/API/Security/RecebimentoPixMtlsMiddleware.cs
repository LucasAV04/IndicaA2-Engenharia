using System.Security.Cryptography.X509Certificates;
using Application.Recebimentos;

namespace API.Security;

// Somente certificado da conexão TLS. Headers de certificado nunca são considerados.
public sealed class RecebimentoPixMtlsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RecebimentoPixOptions options)
    {
        if (!options.Habilitado && (context.Request.Path.StartsWithSegments("/api/cobrancas-pix-vistoria")
            || context.Request.Path.StartsWithSegments("/api/public/cobranca-pix-vistoria")))
        { context.Response.StatusCode=404; return; }
        if (!context.Request.Path.StartsWithSegments("/api/webhooks/efi")) { await next(context); return; }
        if (!options.Habilitado) { context.Response.StatusCode=404; return; }
        var certificado = await context.Connection.GetClientCertificateAsync(context.RequestAborted);
        if (!context.Request.IsHttps || certificado is null || !Confiavel(certificado, options.WebhookCaPath))
        { context.Response.StatusCode=403; return; }
        await next(context);
    }
    internal static bool Confiavel(X509Certificate2 certificado, string caPath)
    {
        try
        {
            using var chain = new X509Chain();
            var authorities = new X509Certificate2Collection();
            authorities.ImportFromPemFile(caPath);
            try
            {
                if (authorities.Count==0) return false;
                chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
                chain.ChainPolicy.ExtraStore.AddRange(authorities);
                chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck;
                chain.ChainPolicy.DisableCertificateDownloads=true;
                chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.2"));
                return chain.Build(certificado);
            }
            finally { foreach(var a in authorities) a.Dispose(); }
        }
        catch (Exception e) when(e is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException) { return false; }
    }
}
