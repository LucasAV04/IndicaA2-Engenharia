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

    private Vistoria()
    {
    }

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

    internal static Vistoria Reidratar(
        Guid id,
        Guid usuarioId,
        string tipoPlanta,
        decimal areaM2,
        PacoteVistoria pacote,
        DateTime dataAgendada,
        StatusVistoria status,
        DateTime createdAt,
        DateTime updatedAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador persistido é obrigatório.", nameof(id));
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("O usuário contratante persistido é obrigatório.", nameof(usuarioId));
        if (string.IsNullOrWhiteSpace(tipoPlanta))
            throw new ArgumentException("O tipo de planta persistido é obrigatório.", nameof(tipoPlanta));
        if (areaM2 <= 0)
            throw new ArgumentOutOfRangeException(nameof(areaM2), "A área persistida deve ser maior que zero.");
        if (!Enum.IsDefined(pacote))
            throw new ArgumentOutOfRangeException(nameof(pacote), "O pacote persistido é inválido.");
        if (dataAgendada == default)
            throw new ArgumentException("A data agendada persistida é obrigatória.", nameof(dataAgendada));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), "O status persistido é inválido.");
        if (createdAt == default)
            throw new ArgumentException("A data de criação persistida é obrigatória.", nameof(createdAt));
        if (updatedAt == default)
            throw new ArgumentException("A data de atualização persistida é obrigatória.", nameof(updatedAt));
        if (updatedAt < createdAt)
            throw new ArgumentException("A data de atualização não pode ser anterior à data de criação.", nameof(updatedAt));

        return new Vistoria
        {
            Id = id,
            UsuarioId = usuarioId,
            TipoPlanta = tipoPlanta,
            AreaM2 = areaM2,
            Pacote = pacote,
            DataAgendada = dataAgendada,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
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
