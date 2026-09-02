using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class OperacaoPagamentoPix : BaseEntity
{
    public Guid PagamentoPixId { get; private set; }

    public TipoOperacaoPagamentoPix TipoOperacao { get; private set; }

    public int? NumeroTentativaEnvio { get; private set; }

    public string ReferenciaIdempotente { get; private set; } = string.Empty;

    public ResultadoOperacaoPagamentoPix? Resultado { get; private set; }

    public string? IdentificadorProvider { get; private set; }

    public string? Codigo { get; private set; }

    public DateTime? FinishedAt { get; private set; }

    private OperacaoPagamentoPix()
    {
    }

    public static OperacaoPagamentoPix IniciarEnvio(Guid pagamentoPixId, int numeroTentativaEnvio) =>
        Criar(pagamentoPixId, TipoOperacaoPagamentoPix.Envio, numeroTentativaEnvio);

    public static OperacaoPagamentoPix IniciarConsulta(Guid pagamentoPixId) =>
        Criar(pagamentoPixId, TipoOperacaoPagamentoPix.Consulta, null);

    internal static OperacaoPagamentoPix Reidratar(
        Guid id,
        Guid pagamentoPixId,
        TipoOperacaoPagamentoPix tipoOperacao,
        int? numeroTentativaEnvio,
        string referenciaIdempotente,
        ResultadoOperacaoPagamentoPix? resultado,
        string? identificadorProvider,
        string? codigo,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime? finishedAt)
    {
        ValidarDadosIniciais(pagamentoPixId, tipoOperacao, numeroTentativaEnvio, referenciaIdempotente);
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador persistido da operação é obrigatório.", nameof(id));
        if (createdAt == default || updatedAt == default || updatedAt < createdAt)
            throw new ArgumentException("As datas persistidas da operação são inválidas.", nameof(updatedAt));

        ValidarFinalizacao(resultado, identificadorProvider, codigo, finishedAt, createdAt);

        return new OperacaoPagamentoPix
        {
            Id = id,
            PagamentoPixId = pagamentoPixId,
            TipoOperacao = tipoOperacao,
            NumeroTentativaEnvio = numeroTentativaEnvio,
            ReferenciaIdempotente = referenciaIdempotente,
            Resultado = resultado,
            IdentificadorProvider = NormalizarOpcional(identificadorProvider, nameof(identificadorProvider)),
            Codigo = NormalizarOpcional(codigo, nameof(codigo)),
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            FinishedAt = finishedAt
        };
    }

    public void Finalizar(
        ResultadoOperacaoPagamentoPix resultado,
        string? identificadorProvider = null,
        string? codigo = null)
    {
        if (FinishedAt.HasValue)
            throw new DomainException("A operação de pagamento Pix já foi finalizada e não pode ser sobrescrita.");
        if (!Enum.IsDefined(resultado))
            throw new DomainException("O resultado da operação de pagamento Pix é inválido.");

        Resultado = resultado;
        IdentificadorProvider = NormalizarOpcional(identificadorProvider, nameof(identificadorProvider));
        Codigo = NormalizarOpcional(codigo, nameof(codigo));
        FinishedAt = DateTime.UtcNow;
        AtualizarDataAlteracao();
    }

    private static OperacaoPagamentoPix Criar(
        Guid pagamentoPixId,
        TipoOperacaoPagamentoPix tipoOperacao,
        int? numeroTentativaEnvio)
    {
        var referenciaIdempotente = pagamentoPixId.ToString("N");
        ValidarDadosIniciais(pagamentoPixId, tipoOperacao, numeroTentativaEnvio, referenciaIdempotente);

        return new OperacaoPagamentoPix
        {
            PagamentoPixId = pagamentoPixId,
            TipoOperacao = tipoOperacao,
            NumeroTentativaEnvio = numeroTentativaEnvio,
            ReferenciaIdempotente = referenciaIdempotente
        };
    }

    private static void ValidarDadosIniciais(
        Guid pagamentoPixId,
        TipoOperacaoPagamentoPix tipoOperacao,
        int? numeroTentativaEnvio,
        string referenciaIdempotente)
    {
        if (pagamentoPixId == Guid.Empty)
            throw new ArgumentException("O identificador do Pagamento Pix é obrigatório.", nameof(pagamentoPixId));
        if (!Enum.IsDefined(tipoOperacao))
            throw new ArgumentOutOfRangeException(nameof(tipoOperacao), "O tipo da operação é inválido.");
        if (!string.Equals(referenciaIdempotente, pagamentoPixId.ToString("N"), StringComparison.Ordinal))
            throw new ArgumentException("A referência idempotente persistida é inválida.", nameof(referenciaIdempotente));
        if (tipoOperacao == TipoOperacaoPagamentoPix.Envio &&
            numeroTentativaEnvio is not > 0 or > PagamentoPix.TentativasMaximas)
            throw new ArgumentException("Uma operação de envio exige o número da tentativa correspondente.", nameof(numeroTentativaEnvio));
        if (tipoOperacao == TipoOperacaoPagamentoPix.Consulta && numeroTentativaEnvio.HasValue)
            throw new ArgumentException("Uma operação de consulta não possui tentativa financeira.", nameof(numeroTentativaEnvio));
    }

    private static void ValidarFinalizacao(
        ResultadoOperacaoPagamentoPix? resultado,
        string? identificadorProvider,
        string? codigo,
        DateTime? finishedAt,
        DateTime createdAt = default)
    {
        if (finishedAt.HasValue != resultado.HasValue)
            throw new ArgumentException("Resultado e data de finalização devem existir juntos.", nameof(finishedAt));
        if (resultado.HasValue && !Enum.IsDefined(resultado.Value))
            throw new ArgumentOutOfRangeException(nameof(resultado), "O resultado persistido é inválido.");
        if (!resultado.HasValue && (!string.IsNullOrWhiteSpace(identificadorProvider) || !string.IsNullOrWhiteSpace(codigo)))
            throw new ArgumentException("Uma operação aberta não pode possuir metadados de finalização.", nameof(resultado));
        if (finishedAt.HasValue && createdAt != default && finishedAt.Value < createdAt)
            throw new ArgumentException("A finalização persistida não pode ser anterior ao início.", nameof(finishedAt));
        _ = NormalizarOpcional(identificadorProvider, nameof(identificadorProvider));
        _ = NormalizarOpcional(codigo, nameof(codigo));
    }

    private static string? NormalizarOpcional(string? valor, string nome)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var normalizado = valor.Trim();
        if (normalizado.Length > 255)
            throw new ArgumentOutOfRangeException(nome, "O metadado da operação excede o tamanho permitido.");

        return normalizado;
    }
}
