using System.Security.Cryptography;

namespace Application.Recebimentos;

public sealed class CobrancaPixVistoriaService(ICobrancaPixVistoriaStore store,
    IRecebimentoPixProcessamentoService processador, RecebimentoPixOptions options, TimeProvider clock)
    : ICobrancaPixVistoriaService
{
    public async Task<CobrancaAdministrativa> GerarAsync(Guid pagamentoId, CancellationToken ct)
    {
        options.ExigirHabilitado();
        var geracao = await store.PrepararAsync(pagamentoId, options.ExpiracaoSegundos, ct);
        if (geracao.Preparacao is not null)
            await processador.ExecutarPreparacaoAsync(geracao.Preparacao, ct);
        return (await store.ListarAsync(ct)).Single(c => c.Id == geracao.Id);
    }
    public async Task CancelarPagamentoAsync(Guid pagamentoId, CancellationToken ct)
    {
        options.ExigirHabilitado();
        await store.SolicitarCancelamentoAsync(pagamentoId, ct);
    }
    public async Task<LinkPagamento> RotacionarLinkAsync(Guid id, CancellationToken ct)
    {
        options.ExigirHabilitado();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expira = clock.GetUtcNow().UtcDateTime.AddHours(24);
        await store.RotacionarLinkAsync(id, RecebimentoPixValidacao.HashLink(token)!, expira, ct);
        return new(options.UrlPublica.TrimEnd('/') + "/pagar#" + token, expira);
    }
    public Task<CobrancaPublica?> ObterPublicaAsync(string token, CancellationToken ct)
    {
        options.ExigirHabilitado();
        var hash = RecebimentoPixValidacao.HashLink(token);
        return hash is null ? Task.FromResult<CobrancaPublica?>(null) : store.ObterPublicaAsync(hash, ct);
    }
}

public sealed class RecebimentoPixProcessamentoService(ICobrancaPixVistoriaStore cobrancas,
    IRecebimentoPixWebhookStore inbox, ICobrancaPixVistoriaProvider provider, RecebimentoPixOptions options)
    : IRecebimentoPixProcessamentoService
{
    public async Task ProcessarCobrancaAsync(Guid id, CancellationToken ct)
    {
        options.ExigirHabilitado();
        var p = await cobrancas.AdquirirAsync(id, ct);
        if (p is null) return;
        await ExecutarPreparacaoAsync(p, ct);
    }
    public async Task ExecutarPreparacaoAsync(PreparacaoCobranca p, CancellationToken ct)
    {
        options.ExigirHabilitado();
        ResultadoCobrancaProvider result;
        try
        {
            result = p.Operacao switch
            {
                OperacaoCobranca.Criar => await provider.CriarAsync(p.Txid, p.Valor, p.ExpiracaoSegundos, ct),
                OperacaoCobranca.Remover => await provider.RemoverAsync(p.Txid, ct),
                _ => await provider.ConsultarAsync(p.Txid, ct)
            };
        }
        catch
        {
            await cobrancas.FinalizarAsync(p, new(SituacaoCobrancaProvider.Indeterminada, "transport"), CancellationToken.None);
            throw;
        }
        await cobrancas.FinalizarAsync(p, result, CancellationToken.None);
    }
    public async Task ProcessarEventoAsync(Guid id, CancellationToken ct)
    {
        options.ExigirHabilitado();
        var p = await inbox.AdquirirAsync(id, ct);
        if (p is null) return;
        ResultadoConsultaPix result;
        try { result = await provider.ConsultarRecebimentoAsync(p.Evento.EndToEndId, ct); }
        catch
        {
            await inbox.FinalizarAsync(p, null, CancellationToken.None);
            throw;
        }
        await inbox.FinalizarAsync(p, result, CancellationToken.None);
    }
}
