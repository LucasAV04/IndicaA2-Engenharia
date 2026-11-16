using System.Globalization;

namespace Application.Relatorios;

public enum TipoRelatorio
{
    PagamentosRecebidos,
    CashbacksDisponiveis,
    CashbacksPagos,
    PixEmAberto,
    FalhasFinanceiras,
    CobrancasDivergentes,
    Emails
}

public sealed record LinhaRelatorio(Guid Id, DateTime DataUtc, int Status, decimal? Valor);
public sealed record PaginaRelatorio(long Total, decimal? ValorTotal, IReadOnlyList<LinhaRelatorio> Linhas);

public sealed record FiltroRelatorio(TipoRelatorio Tipo, DateOnly Inicio, DateOnly Fim, int? Status,
    int Pagina, int Tamanho)
{
    public const int LimiteExportacao = 5000;

    public bool Valido => Enum.IsDefined(Tipo) && Fim != DateOnly.MaxValue
        && Fim.DayNumber - Inicio.DayNumber is >= 0 and < 366
        && Status is null or >= 0 and <= 10 && Pagina >= 1 && Tamanho is >= 1 and <= 100;

    public DateTime DeUtc => Inicio.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    public DateTime AteUtc => Fim.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    public long Deslocamento => (long)(Pagina - 1) * Tamanho;
}

public interface IRelatorioOperacionalStore
{
    Task<PaginaRelatorio> ConsultarAsync(FiltroRelatorio filtro, bool exportar, CancellationToken cancellationToken);
}

public static class CsvRelatorio
{
    public static string Linha(LinhaRelatorio linha) => string.Join(',',
        Celula(linha.Id.ToString()),
        Celula(linha.DataUtc.ToString("O", CultureInfo.InvariantCulture)),
        Celula(linha.Status.ToString(CultureInfo.InvariantCulture)),
        Celula(linha.Valor?.ToString("0.00", CultureInfo.InvariantCulture) ?? ""));

    public static string Celula(string valor)
    {
        var limpo = new string(valor.Where(c => !char.IsControl(c)).ToArray());
        var inicio = limpo.AsSpan().TrimStart();
        if (!inicio.IsEmpty && inicio[0] is '=' or '+' or '-' or '@')
            limpo = "'" + limpo;
        return "\"" + limpo.Replace("\"", "\"\"") + "\"";
    }
}
