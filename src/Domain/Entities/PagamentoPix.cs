using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class PagamentoPix : BaseEntity
{
    public const int TentativasMaximas = 5;

    public static IReadOnlyList<StatusPagamentoPix> StatusElegiveisParaIniciarTentativa =>
        [StatusPagamentoPix.Pendente, StatusPagamentoPix.Falhou];

    public Guid CashbackId { get; private set; }

    public Guid UsuarioBeneficiarioId { get; private set; }

    public decimal Valor { get; private set; }

    public TipoChavePix TipoChavePix { get; private set; }

    public string ChavePix { get; private set; } = string.Empty;

    public StatusPagamentoPix Status { get; private set; }

    public int QuantidadeTentativas { get; private set; }

    private PagamentoPix()
    {
    }

    public static PagamentoPix Criar(
        Guid cashbackId,
        Guid usuarioBeneficiarioId,
        decimal valor,
        TipoChavePix tipoChavePix,
        string chavePix)
    {
        if (cashbackId == Guid.Empty)
            throw new DomainException("O identificador do cashback é obrigatório.");
        if (usuarioBeneficiarioId == Guid.Empty)
            throw new DomainException("O usuário beneficiário é obrigatório.");
        if (valor <= 0)
            throw new DomainException("O valor do Pagamento Pix deve ser maior que zero.");
        if (!Enum.IsDefined(tipoChavePix))
            throw new DomainException("O tipo de chave Pix é inválido.");
        if (string.IsNullOrWhiteSpace(chavePix))
            throw new DomainException("A chave Pix é obrigatória.");

        return new PagamentoPix
        {
            CashbackId = cashbackId,
            UsuarioBeneficiarioId = usuarioBeneficiarioId,
            Valor = valor,
            TipoChavePix = tipoChavePix,
            ChavePix = chavePix,
            Status = StatusPagamentoPix.Pendente,
            QuantidadeTentativas = 0
        };
    }

    internal static PagamentoPix Reidratar(
        Guid id,
        Guid cashbackId,
        Guid usuarioBeneficiarioId,
        decimal valor,
        TipoChavePix tipoChavePix,
        string chavePix,
        StatusPagamentoPix status,
        int quantidadeTentativas,
        DateTime createdAt,
        DateTime updatedAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador persistido é obrigatório.", nameof(id));
        if (cashbackId == Guid.Empty)
            throw new ArgumentException("O identificador do cashback persistido é obrigatório.", nameof(cashbackId));
        if (usuarioBeneficiarioId == Guid.Empty)
            throw new ArgumentException("O usuário beneficiário persistido é obrigatório.", nameof(usuarioBeneficiarioId));
        if (valor <= 0)
            throw new ArgumentOutOfRangeException(nameof(valor), "O valor persistido deve ser maior que zero.");
        if (!Enum.IsDefined(tipoChavePix))
            throw new ArgumentOutOfRangeException(nameof(tipoChavePix), "O tipo de chave Pix persistido é inválido.");
        if (string.IsNullOrWhiteSpace(chavePix))
            throw new ArgumentException("A chave Pix persistida é obrigatória.", nameof(chavePix));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), "O status persistido é inválido.");
        if (quantidadeTentativas is < 0 or > TentativasMaximas)
            throw new ArgumentOutOfRangeException(nameof(quantidadeTentativas), "A quantidade de tentativas persistida é inválida.");
        if (createdAt == default)
            throw new ArgumentException("A data de criação persistida é obrigatória.", nameof(createdAt));
        if (updatedAt == default)
            throw new ArgumentException("A data de atualização persistida é obrigatória.", nameof(updatedAt));
        if (updatedAt < createdAt)
            throw new ArgumentException("A data de atualização não pode ser anterior à data de criação.", nameof(updatedAt));

        GarantirCoerenciaStatusETentativas(status, quantidadeTentativas);

        return new PagamentoPix
        {
            Id = id,
            CashbackId = cashbackId,
            UsuarioBeneficiarioId = usuarioBeneficiarioId,
            Valor = valor,
            TipoChavePix = tipoChavePix,
            ChavePix = chavePix,
            Status = status,
            QuantidadeTentativas = quantidadeTentativas,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void IniciarTentativa()
    {
        if (QuantidadeTentativas >= TentativasMaximas)
            throw new LimiteTentativasPagamentoPixAtingidoException();
        if (!StatusElegiveisParaIniciarTentativa.Contains(Status))
            throw CriarExcecaoTransicaoInvalida("iniciar uma tentativa");

        QuantidadeTentativas++;
        Status = StatusPagamentoPix.Processando;
        AtualizarDataAlteracao();
    }

    public void RegistrarFalha()
    {
        GarantirStatus(StatusPagamentoPix.Processando, "registrar uma falha");

        Status = QuantidadeTentativas == TentativasMaximas
            ? StatusPagamentoPix.FalhaDefinitiva
            : StatusPagamentoPix.Falhou;
        AtualizarDataAlteracao();
    }

    public void ConfirmarConclusao()
    {
        GarantirStatus(StatusPagamentoPix.Processando, "confirmar a conclusão");

        Status = StatusPagamentoPix.Concluido;
        AtualizarDataAlteracao();
    }

    public void Cancelar()
    {
        if (Status == StatusPagamentoPix.Cancelado)
            return;
        if (Status is not (StatusPagamentoPix.Pendente or StatusPagamentoPix.Falhou))
            throw CriarExcecaoTransicaoInvalida("cancelar");

        Status = StatusPagamentoPix.Cancelado;
        AtualizarDataAlteracao();
    }

    private void GarantirStatus(StatusPagamentoPix statusEsperado, string acao)
    {
        if (Status != statusEsperado)
            throw CriarExcecaoTransicaoInvalida(acao);
    }

    private TransicaoPagamentoPixInvalidaException CriarExcecaoTransicaoInvalida(string acao) =>
        new(acao, Status.ToString());

    private static void GarantirCoerenciaStatusETentativas(
        StatusPagamentoPix status,
        int quantidadeTentativas)
    {
        var combinacaoValida = status switch
        {
            StatusPagamentoPix.Pendente => quantidadeTentativas == 0,
            StatusPagamentoPix.Processando => quantidadeTentativas is >= 1 and <= TentativasMaximas,
            StatusPagamentoPix.Falhou => quantidadeTentativas is >= 1 and < TentativasMaximas,
            StatusPagamentoPix.FalhaDefinitiva => quantidadeTentativas == TentativasMaximas,
            StatusPagamentoPix.Concluido => quantidadeTentativas is >= 1 and <= TentativasMaximas,
            StatusPagamentoPix.Cancelado => quantidadeTentativas is >= 0 and < TentativasMaximas,
            _ => false
        };

        if (!combinacaoValida)
        {
            throw new ArgumentException(
                "A combinação persistida de status e quantidade de tentativas é inválida.",
                nameof(quantidadeTentativas));
        }
    }
}
