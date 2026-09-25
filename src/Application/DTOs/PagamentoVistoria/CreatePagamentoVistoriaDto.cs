namespace Application.DTOs.PagamentoVistoria;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class CreatePagamentoVistoriaDto
{
    public Guid VistoriaId { get; set; }
}
