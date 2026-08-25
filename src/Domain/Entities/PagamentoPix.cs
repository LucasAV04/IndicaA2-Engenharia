using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class PagamentoPix : BaseEntity
{
    public const int TentativasMaximas = 5;

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

    public void IniciarTentativa()
    {
        if (QuantidadeTentativas >= TentativasMaximas)
            throw new LimiteTentativasPagamentoPixAtingidoException();
        if (Status is not (StatusPagamentoPix.Pendente or StatusPagamentoPix.Falhou))
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
}
