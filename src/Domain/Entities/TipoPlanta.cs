using Domain.Exceptions;

namespace Domain.Entities;

public sealed class TipoPlanta : BaseEntity
{
    public const int TamanhoMaximoNome = 150;
    public string Nome { get; private set; }
    public string NomeNormalizado { get; private set; }
    public bool Ativo { get; private set; }

    public TipoPlanta(string nome, DateTime instanteUtc)
    {
        Nome = ValidarNome(nome);
        NomeNormalizado = Nome.ToUpperInvariant();
        Ativo = true;
        CreatedAt = UpdatedAt = instanteUtc;
    }

    public void Renomear(string nome, DateTime instanteUtc)
    {
        Nome = ValidarNome(nome);
        NomeNormalizado = Nome.ToUpperInvariant();
        UpdatedAt = instanteUtc;
    }

    public void Desativar(DateTime instanteUtc) { Ativo = false; UpdatedAt = instanteUtc; }

    public static string ValidarNome(string nome)
    {
        var valor = nome?.Trim();
        if (string.IsNullOrEmpty(valor) || valor.Length > TamanhoMaximoNome)
            throw new DomainException("O nome do tipo de planta deve conter entre 1 e 150 caracteres.");
        return valor;
    }

    internal static TipoPlanta Reidratar(Guid id, string nome, bool ativo, DateTime criado, DateTime atualizado) =>
        new(nome, criado) { Id = id, Ativo = ativo, UpdatedAt = atualizado };
}
