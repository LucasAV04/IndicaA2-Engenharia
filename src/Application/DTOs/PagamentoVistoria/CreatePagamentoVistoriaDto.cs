namespace Application.DTOs.PagamentoVistoria;

public sealed class CreatePagamentoVistoriaDto
{
    public Guid VistoriaId { get; set; }

    public decimal Valor { get; set; }
}
