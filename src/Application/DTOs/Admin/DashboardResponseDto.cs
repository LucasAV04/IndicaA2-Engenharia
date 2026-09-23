namespace Application.DTOs.Admin;

public sealed class DashboardResponseDto
{
    public long TiposCadastrados { get; init; }
    public long TiposComPrecoAtivo { get; init; }
    public long TiposSemConfiguracao { get; init; }
    public int? UltimaVersaoPreco { get; init; }
    public string? UltimoTipoPreco { get; init; }
    public long TotalUsuarios { get; init; }
    public long UsuariosAtivos { get; init; }
    public IReadOnlyDictionary<string, long> Indicacoes { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> Vistorias { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> PagamentosVistoria { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> Cashbacks { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> PagamentosPix { get; init; } = new Dictionary<string, long>();
    public decimal ReceitaConfirmada { get; init; }
    public decimal PagamentosPendentes { get; init; }
    public decimal CashbackDisponivel { get; init; }
    public decimal CashbackPago { get; init; }
    public decimal PixPendenteProcessando { get; init; }
    public decimal PixConcluido { get; init; }
    public long FalhasPix { get; init; }
    public long FalhasDefinitivasPix { get; init; }
    public DateTime CalculadoEmUtc { get; init; }
}
