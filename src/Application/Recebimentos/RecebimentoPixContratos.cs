using Domain.Enums;

namespace Application.Recebimentos;

public enum SituacaoCobrancaProvider { Indeterminada, Ausente, Ativa, Concluida, Removida, FalhaDefinitiva, BloqueioOperacional, Limitada, Indisponivel, Conflito }
public enum SituacaoConsultaPix { Confirmado, AindaNaoDisponivel, BloqueioOperacional, Indeterminado }
public readonly record struct ResultadoConsultaPix(SituacaoConsultaPix Situacao, EventoPix? Evento = null)
{
    public override string ToString() => $"ResultadoConsultaPix({Situacao})";
    public static implicit operator ResultadoConsultaPix(EventoPix? evento) =>
        evento is null ? new(SituacaoConsultaPix.Indeterminado) : new(SituacaoConsultaPix.Confirmado, evento);
}
public enum OperacaoCobranca { Criar, Consultar, Remover }

// Tipos internos não são contratos públicos. ToString não expõe códigos nem tokens.
public sealed record ResultadoCobrancaProvider(SituacaoCobrancaProvider Situacao, string Codigo,
    string? Txid = null, decimal? Valor = null, DateTime? CriadaEm = null, int? ExpiracaoSegundos = null,
    int? Revisao = null, string? PixCopiaECola = null, IReadOnlyList<EventoPix>? Recebimentos = null)
{
    public override string ToString() => $"ResultadoCobrancaProvider({Situacao})";
}
public sealed record EventoPix(string EndToEndId, string Txid, decimal Valor, DateTime Horario)
{
    public override string ToString() => "EventoPix";
}
public sealed record PreparacaoCobranca(Guid Id, Guid PagamentoId, string Txid, decimal Valor,
    int ExpiracaoSegundos, Guid LeaseId, Guid OperacaoId, OperacaoCobranca Operacao)
{
    public override string ToString() => $"PreparacaoCobranca({Id})";
}
public sealed record PreparacaoRecebimento(Guid Id, Guid LeaseId, EventoPix Evento)
{
    public override string ToString() => $"PreparacaoRecebimento({Id})";
}
public sealed record GeracaoCobranca(Guid Id, PreparacaoCobranca? Preparacao)
{
    public override string ToString() => $"GeracaoCobranca({Id})";
}
public sealed record CobrancaAdministrativa(Guid Id, Guid PagamentoVistoriaId, decimal Valor,
    StatusCobrancaPixVistoria Status, DateTime? VenceEm, DateTime? ConfirmadoEm,
    Guid? VistoriaId = null, string? Cliente = null, bool Divergencia = false);
public sealed record CobrancaPublica(decimal Valor, StatusCobrancaPixVistoria Status,
    DateTime? VenceEm, string? PixCopiaECola, bool Confirmada)
{
    public override string ToString() => "CobrancaPublica";
}
public sealed record LinkPagamento(string Link, DateTime ExpiraEm)
{
    public override string ToString() => "LinkPagamento";
}
public sealed record AuditoriaCobranca(Guid Id, int Tipo, string? Codigo, DateTime StartedAt, DateTime? FinishedAt);
public sealed record IndicadoresRecebimento(long Ativas, long ConfirmacoesPendentes, long Confirmadas, long Expiradas, long CobrancasDivergentes, long EventosDivergentes);

public interface ICobrancaPixVistoriaProvider
{
    Task<ResultadoCobrancaProvider> CriarAsync(string txid, decimal valor, int expiracaoSegundos, CancellationToken ct);
    Task<ResultadoCobrancaProvider> ConsultarAsync(string txid, CancellationToken ct);
    Task<ResultadoCobrancaProvider> RemoverAsync(string txid, CancellationToken ct);
    Task<ResultadoConsultaPix> ConsultarRecebimentoAsync(string endToEndId, CancellationToken ct);
    Task<bool> ConfigurarWebhookAsync(CancellationToken ct);
    Task<bool> ConsultarWebhookAsync(CancellationToken ct);
}
public interface ICobrancaPixVistoriaStore
{
    Task<GeracaoCobranca> PrepararAsync(Guid pagamentoId, int expiracaoSegundos, CancellationToken ct);
    Task<PreparacaoCobranca?> AdquirirAsync(Guid id, CancellationToken ct);
    Task FinalizarAsync(PreparacaoCobranca preparacao, ResultadoCobrancaProvider resultado, CancellationToken ct);
    Task SolicitarCancelamentoAsync(Guid pagamentoId, CancellationToken ct);
    Task<IReadOnlyList<CobrancaAdministrativa>> ListarAsync(CancellationToken ct);
    Task<IReadOnlyList<AuditoriaCobranca>> AuditoriaAsync(Guid id, CancellationToken ct);
    Task<IndicadoresRecebimento> IndicadoresAsync(CancellationToken ct);
    Task RotacionarLinkAsync(Guid id, byte[] hash, DateTime expiraEm, CancellationToken ct);
    Task<CobrancaPublica?> ObterPublicaAsync(byte[] hash, CancellationToken ct);
}
public interface IRecebimentoPixWebhookStore
{
    Task PersistirAsync(IReadOnlyList<EventoPix> eventos, CancellationToken ct);
    Task<PreparacaoRecebimento?> AdquirirAsync(Guid id, CancellationToken ct);
    Task FinalizarAsync(PreparacaoRecebimento preparacao, ResultadoConsultaPix resultado, CancellationToken ct);
}
public interface IRecebimentoPixCandidatoStore
{
    Task<IReadOnlyList<Guid>> CobrançasAsync(int limite, CancellationToken ct);
    Task<IReadOnlyList<Guid>> EventosAsync(int limite, CancellationToken ct);
}
public interface IRecebimentoPixProcessamentoService
{
    Task ExecutarPreparacaoAsync(PreparacaoCobranca preparacao, CancellationToken ct);
    Task ProcessarCobrancaAsync(Guid id, CancellationToken ct);
    Task ProcessarEventoAsync(Guid id, CancellationToken ct);
}
public interface ICobrancaPixVistoriaService
{
    Task<CobrancaAdministrativa> GerarAsync(Guid pagamentoId, CancellationToken ct);
    Task CancelarPagamentoAsync(Guid pagamentoId, CancellationToken ct);
    Task<LinkPagamento> RotacionarLinkAsync(Guid id, CancellationToken ct);
    Task<CobrancaPublica?> ObterPublicaAsync(string token, CancellationToken ct);
}
