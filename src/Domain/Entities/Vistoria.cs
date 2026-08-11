using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class Vistoria : BaseEntity
{
    public Guid UsuarioId { get; private set; }

    public string TipoPlanta { get; private set; } = string.Empty;

    public decimal AreaM2 { get; private set; }

    public PacoteVistoria Pacote { get; private set; }

    public DateTime DataAgendada { get; private set; }

    public StatusVistoria Status { get; private set; }

    public Vistoria(
        Guid usuarioId,
        string tipoPlanta,
        decimal areaM2,
        PacoteVistoria pacote,
        DateTime dataAgendada)
    {
        if (usuarioId == Guid.Empty)
            throw new DomainException("O usuário contratante é obrigatório.");
        if (string.IsNullOrWhiteSpace(tipoPlanta))
            throw new DomainException("O tipo de planta é obrigatório.");
        if (areaM2 <= 0)
            throw new DomainException("A área da vistoria deve ser maior que zero.");
        if (!Enum.IsDefined(pacote))
            throw new DomainException("O pacote de vistoria informado é inválido.");
        if (dataAgendada == default)
            throw new DomainException("A data agendada é obrigatória.");

        UsuarioId = usuarioId;
        TipoPlanta = tipoPlanta.Trim();
        AreaM2 = areaM2;
        Pacote = pacote;
        DataAgendada = dataAgendada;
        Status = StatusVistoria.Agendada;
    }

    public void MarcarRealizada()
    {
        if (Status is StatusVistoria.Realizada)
            return;

        GarantirTransicao(StatusVistoria.Agendada, "marcar a vistoria como realizada");
        Status = StatusVistoria.Realizada;
        AtualizarDataAlteracao();
    }

    public void Concluir()
    {
        if (Status is StatusVistoria.Concluida)
            return;

        GarantirTransicao(StatusVistoria.Realizada, "concluir a vistoria");
        Status = StatusVistoria.Concluida;
        AtualizarDataAlteracao();
    }

    public void Cancelar()
    {
        if (Status is StatusVistoria.Cancelada)
            return;

        GarantirTransicao(StatusVistoria.Agendada, "cancelar a vistoria");
        Status = StatusVistoria.Cancelada;
        AtualizarDataAlteracao();
    }

    private void GarantirTransicao(StatusVistoria statusEsperado, string acao)
    {
        if (Status != statusEsperado)
        {
            throw new DomainException(
                $"Não é possível {acao}: status atual é '{Status}', esperado '{statusEsperado}'.");
        }
    }
}
