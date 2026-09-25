using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Services;

public sealed record CalculoVistoria(Guid PrecoId, int Versao, Guid TipoPlantaId, string NomeTipoPlanta,
    decimal AreaM2, PacoteVistoria Pacote, decimal PrecoM2, ModalidadeAcrescimo Modalidade,
    decimal Acrescimo, decimal ValorBase, decimal ValorFinal, DateTime CalculadoEmUtc);

public static class MotorPrecificacaoVistoria
{
    public static void ValidarEntrada(decimal area, PacoteVistoria pacote)
    {
        if (area <= 0 || area > 99999999.99m || decimal.Round(area, 2) != area)
            throw new DomainException("Área deve ser positiva, com até duas casas e dentro do limite técnico.");
        if (!Enum.IsDefined(pacote)) throw new DomainException("Pacote inválido.");
    }
    public static CalculoVistoria Calcular(PrecoVistoria preco, string nomeAtualTipo, decimal area,
        PacoteVistoria pacote, DateTime instanteUtc)
    {
        ArgumentNullException.ThrowIfNull(preco);
        ValidarEntrada(area, pacote);
        try
        {
            var valorBase = checked(preco.PrecoM2 * area);
            var adicional = pacote == PacoteVistoria.Simples ? 0m : preco.Modalidade == ModalidadeAcrescimo.Fixo
                ? preco.Acrescimo : checked(valorBase * preco.Acrescimo / 100m);
            var final = decimal.Round(checked(valorBase + adicional), 2, MidpointRounding.AwayFromZero);
            if (final <= 0 || final > 9999999999.99m)
                throw new DomainException("Valor final fora da capacidade monetária suportada.");
            return new(preco.Id, preco.Versao, preco.TipoPlantaId, TipoPlanta.ValidarNome(nomeAtualTipo),
                area, pacote, preco.PrecoM2, preco.Modalidade, pacote == PacoteVistoria.Simples ? 0m : preco.Acrescimo,
                valorBase, final, instanteUtc);
        }
        catch (OverflowException) { throw new DomainException("Cálculo excede a capacidade monetária suportada."); }
    }
}
