using System.Net.Mail;
using System.Security.Cryptography;
using Application.Contas;
using Infrastructure.Email;
using Infrastructure.Repositories;
using Infrastructure.Security;

namespace API.Processing;

internal sealed class EmailOutboxWorker(IServiceScopeFactory scopes, EmailWorkerOptions options,
    TimeProvider time, Uri origem, ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.IntervaloSegundos), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ProcessarAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task ProcessarAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IEmailOutboxStore>();
        IReadOnlyList<Guid> ids;
        try { ids = await store.SelecionarAsync(options.TamanhoLote, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Seleção de e-mail indisponível ({Tipo})", ex.GetType().Name);
            return;
        }

        foreach (var id in ids.Take(options.TamanhoLote).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var envio = await store.AdquirirAsync(id, ct);
                if (envio is not null)
                    await EnviarAsync(scope.ServiceProvider, store, envio, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("E-mail {Id}: execução indisponível ({Tipo})", id, ex.GetType().Name);
            }
        }
    }

    private async Task EnviarAsync(IServiceProvider services, IEmailOutboxStore store, EnvioEmail envio,
        CancellationToken ct)
    {
        var estado = EstadoEmail.Enviado;
        int? erro = null;
        string? tipo = null;
        try
        {
            var token = envio.Material is null ? null : services.GetRequiredService<TokenContaProtector>()
                .Decifrar(envio.Material, envio.Usuario, envio.Referencia, (FinalidadeTokenConta)envio.Template);
            var email = TemplatesEmail.Criar(origem, envio.Email, envio.Template, token);
            await services.GetRequiredService<IEmailSender>().EnviarAsync(email, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            tipo = ex.GetType().Name;
            var permanente = ex is CryptographicException or FormatException or ArgumentException
                || ex is SmtpException smtp && (int)smtp.StatusCode >= 500;
            estado = permanente ? EstadoEmail.FalhaPermanente : EstadoEmail.Indeterminado;
            erro = permanente ? 1 : 2;
        }
        await store.FinalizarAsync(envio, estado, erro, ct);
        if (erro is not null)
            logger.LogWarning("E-mail {Id} {Template} tentativa {Tentativa} terminou em {Estado} ({Tipo})",
                envio.Id, envio.Template, envio.Tentativa, estado, tipo);
    }
}
