using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

/// <summary>Identidade e valor da operação lógica; não representa uma resposta do provider.</summary>
public sealed class CobrancaPixVistoria
{
    public Guid Id { get; }
    public Guid PagamentoVistoriaId { get; }
    public string Txid { get; }
    public decimal Valor { get; }
    public int ExpiracaoSegundos { get; }
    public DateTime CreatedAt { get; }

    public CobrancaPixVistoria(Guid pagamentoVistoriaId, decimal valor, int expiracaoSegundos, DateTime agora)
    {
        if (pagamentoVistoriaId == Guid.Empty || valor <= 0 || valor > 9999999999.99m || decimal.Round(valor, 2) != valor)
            throw new DomainException("Pagamento incompatível com cobrança Pix.");
        if (expiracaoSegundos is < 60 or > 86400 || agora.Kind != DateTimeKind.Utc)
            throw new DomainException("Prazo ou instante de cobrança inválido.");
        Id = Guid.NewGuid();
        PagamentoVistoriaId = pagamentoVistoriaId;
        Txid = Id.ToString("N");
        Valor = valor;
        ExpiracaoSegundos = expiracaoSegundos;
        CreatedAt = agora;
    }

    public static bool PermiteReemissao(StatusCobrancaPixVistoria status) =>
        status is StatusCobrancaPixVistoria.Expirada or StatusCobrancaPixVistoria.Removida or StatusCobrancaPixVistoria.FalhaDefinitiva;
}
