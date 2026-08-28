namespace Application.Interfaces.Providers;

public static class PixReferenciaIdempotente
{
    public static string Criar(Guid pagamentoPixId)
    {
        if (pagamentoPixId == Guid.Empty)
            throw new ArgumentException("O identificador do Pagamento Pix é obrigatório.", nameof(pagamentoPixId));

        return pagamentoPixId.ToString("N");
    }
}
