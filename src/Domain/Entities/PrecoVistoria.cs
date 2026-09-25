using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class PrecoVistoria : BaseEntity
{
    public Guid TipoPlantaId { get; private set; }
    public string NomeTipoPlanta { get; private set; }
    public decimal PrecoM2 { get; private set; }
    public ModalidadeAcrescimo Modalidade { get; private set; }
    public decimal Acrescimo { get; private set; }
    public int Versao { get; private set; }
    public bool Ativo { get; private set; } = true;
    public DateTime? DesativadoEm { get; private set; }

    public PrecoVistoria(Guid tipoPlantaId, string nome, decimal precoM2,
        ModalidadeAcrescimo modalidade, decimal acrescimo, int versao, DateTime instanteUtc)
    {
        if (tipoPlantaId == Guid.Empty || versao <= 0) throw new DomainException("Tipo e versão do preço são obrigatórios.");
        ValidarValores(precoM2, modalidade, acrescimo);
        TipoPlantaId = tipoPlantaId; NomeTipoPlanta = TipoPlanta.ValidarNome(nome);
        PrecoM2 = precoM2; Modalidade = modalidade; Acrescimo = acrescimo; Versao = versao;
        CreatedAt = UpdatedAt = instanteUtc;
    }

    public static void ValidarValores(decimal precoM2, ModalidadeAcrescimo modalidade, decimal acrescimo)
    {
        if (precoM2 <= 0 || precoM2 > 99999999.9999m || decimal.Round(precoM2, 4) != precoM2)
            throw new DomainException("Preço por m² deve ser positivo, com até quatro casas e dentro do limite técnico.");
        if (!Enum.IsDefined(modalidade) || acrescimo < 0 || acrescimo > 99999999.9999m || decimal.Round(acrescimo, 4) != acrescimo)
            throw new DomainException("Modalidade ou acréscimo inválido; são permitidas até quatro casas decimais.");
        if (modalidade == ModalidadeAcrescimo.Percentual && acrescimo > 10000m)
            throw new DomainException("O percentual excede o limite técnico de 10000%.");
    }

    public void Desativar(DateTime instanteUtc)
    {
        if (!Ativo) return;
        Ativo = false; DesativadoEm = UpdatedAt = instanteUtc;
    }

    internal static PrecoVistoria Reidratar(Guid id, Guid tipo, string nome, decimal preco,
        ModalidadeAcrescimo modalidade, decimal acrescimo, int versao, bool ativo,
        DateTime criado, DateTime atualizado, DateTime? desativado) =>
        new(tipo, nome, preco, modalidade, acrescimo, versao, criado)
        { Id = id, Ativo = ativo, UpdatedAt = atualizado, DesativadoEm = desativado };
}
