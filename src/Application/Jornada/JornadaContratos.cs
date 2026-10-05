using Domain.Enums;

namespace Application.Jornada;

public enum TipoNotificacao { VistoriaVinculada=0, PagamentoConfirmado=1, DadosPixNecessarios=2, CashbackCriado=3, CashbackPago=4, FalhaFinanceira=5, CobrancaRevisao=6 }
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record IndicacaoPublicaRequest(string Codigo, string Nome, string Telefone, string VersaoTermo, bool Consentimento);
public sealed record ProtocoloIndicacao(string Protocolo);
public sealed record IndicacaoPortal(string Nome, string TelefoneMascarado, StatusIndicacao Status, DateTime CriadaEm);
public sealed record CashbackPortal(decimal Valor, StatusCashback Status, StatusPagamentoPix? SituacaoPix, DateTime CriadoEm);
public sealed record PerfilPortal(string Nome, string Codigo, string Link);
public sealed record NotificacaoResponse(Guid Id, TipoNotificacao Tipo, DateTime CriadaEm, DateTime? LidaEm);
public sealed record OrigemIndicacaoResponse(Guid Id, int Origem, string? VersaoTermo, DateTime? ConsentimentoEm);
public sealed record IndicadoresJornada(long SemDadosPix, long DisponiveisSemOrdem, long PixPendente, long PixProcessando, long PixConcluido, long PixFalhou, long PixFalhaDefinitiva, long NaoLidas);
public interface IJornadaPublicaStore
{
    Task<bool> CodigoUtilizavelAsync(string codigo,CancellationToken ct);
    Task<ProtocoloIndicacao> CaptarAsync(IndicacaoPublicaRequest request,string chave,DateTime agora,CancellationToken ct);
}
public interface IJornadaConsultaStore
{
    Task<(string Nome,string Codigo)> PerfilAsync(Guid usuarioId,CancellationToken ct);
    Task<IReadOnlyList<IndicacaoPortal>> IndicacoesAsync(Guid usuarioId,CancellationToken ct);
    Task<IReadOnlyList<CashbackPortal>> CashbacksAsync(Guid usuarioId,CancellationToken ct);
    Task<IReadOnlyList<NotificacaoResponse>> NotificacoesAsync(Guid usuarioId,bool administrador,CancellationToken ct);
    Task<long> NaoLidasAsync(Guid usuarioId,bool administrador,CancellationToken ct);
    Task<bool> LerAsync(Guid usuarioId,bool administrador,Guid? notificacaoId,DateTime agora,CancellationToken ct);
    Task<IndicadoresJornada> IndicadoresAsync(CancellationToken ct);
    Task<IReadOnlyList<OrigemIndicacaoResponse>> OrigensAsync(CancellationToken ct);
}
public interface IJornadaFinanceiraStore
{
    Task ConcluirVistoriaAsync(Guid vistoriaId,CancellationToken ct);
    Task PrepararCashbackAsync(Guid cashbackId,CancellationToken ct);
    Task<IReadOnlyList<Guid>> CandidatosAsync(int limite,CancellationToken ct);
}
