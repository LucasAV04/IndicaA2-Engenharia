using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class DadosPix : BaseEntity
{
    public Guid UsuarioId { get; private set; }
    public TipoChavePix TipoChavePix { get; private set; }
    public string ChavePix { get; private set; } = string.Empty;

    private DadosPix()
    {
    }

    public DadosPix(Guid usuarioId, TipoChavePix tipoChavePix, string chavePix)
    {
        if (usuarioId == Guid.Empty)
            throw new DomainException("O usuário dos dados Pix é obrigatório.");

        UsuarioId = usuarioId;
        DefinirChave(tipoChavePix, chavePix);
    }

    internal static DadosPix Reidratar(
        Guid id,
        Guid usuarioId,
        TipoChavePix tipoChavePix,
        string chavePix,
        DateTime createdAt,
        DateTime updatedAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador persistido é obrigatório.", nameof(id));
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("O identificador do usuário persistido é obrigatório.", nameof(usuarioId));
        if (!Enum.IsDefined(tipoChavePix))
            throw new ArgumentOutOfRangeException(nameof(tipoChavePix), "O tipo de chave Pix persistido é inválido.");
        if (string.IsNullOrWhiteSpace(chavePix))
            throw new ArgumentException("A chave Pix persistida é obrigatória.", nameof(chavePix));
        if (createdAt == default)
            throw new ArgumentException("A data de criação persistida é obrigatória.", nameof(createdAt));
        if (updatedAt == default)
            throw new ArgumentException("A data de atualização persistida é obrigatória.", nameof(updatedAt));
        if (updatedAt < createdAt)
            throw new ArgumentException("A data de atualização não pode ser anterior à data de criação.", nameof(updatedAt));

        return new DadosPix
        {
            Id = id,
            UsuarioId = usuarioId,
            TipoChavePix = tipoChavePix,
            ChavePix = chavePix,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void Atualizar(TipoChavePix tipoChavePix, string chavePix)
    {
        DefinirChave(tipoChavePix, chavePix);
        AtualizarDataAlteracao();
    }

    private void DefinirChave(TipoChavePix tipoChavePix, string chavePix)
    {
        if (!Enum.IsDefined(tipoChavePix))
            throw new DomainException("O tipo de chave Pix é inválido.");
        if (string.IsNullOrWhiteSpace(chavePix))
            throw new DomainException("A chave Pix é obrigatória.");

        var normalizada = tipoChavePix switch
        {
            TipoChavePix.Cpf => NormalizarDocumento(chavePix, 11, "CPF"),
            TipoChavePix.Cnpj => NormalizarDocumento(chavePix, 14, "CNPJ"),
            TipoChavePix.Email => NormalizarEmail(chavePix),
            TipoChavePix.Telefone => NormalizarTelefone(chavePix),
            TipoChavePix.Aleatoria => NormalizarAleatoria(chavePix),
            _ => throw new DomainException("O tipo de chave Pix é inválido.")
        };

        TipoChavePix = tipoChavePix;
        ChavePix = normalizada;
    }

    private static string NormalizarDocumento(string chave, int tamanho, string nome)
    {
        if (chave.Any(caractere => !char.IsDigit(caractere) && caractere is not ('.' or '-' or '/' or ' ')))
            throw new DomainException($"A chave Pix do tipo {nome} é inválida.");

        var somenteDigitos = new string(chave.Where(char.IsDigit).ToArray());
        if (somenteDigitos.Length != tamanho || somenteDigitos.Distinct().Count() == 1)
            throw new DomainException($"A chave Pix do tipo {nome} é inválida.");

        var documentoValido = tamanho == 11
            ? CpfEhValido(somenteDigitos)
            : CnpjEhValido(somenteDigitos);

        if (!documentoValido)
            throw new DomainException($"A chave Pix do tipo {nome} é inválida.");

        return somenteDigitos;
    }

    private static string NormalizarEmail(string chave)
    {
        var email = chave.Trim().ToLowerInvariant();
        var partes = email.Split('@');
        if (email.Any(char.IsWhiteSpace) || partes.Length != 2 ||
            string.IsNullOrWhiteSpace(partes[0]) ||
            string.IsNullOrWhiteSpace(partes[1]) ||
            partes[1].StartsWith(".", StringComparison.Ordinal) ||
            partes[1].EndsWith(".", StringComparison.Ordinal))
        {
            throw new DomainException("A chave Pix do tipo e-mail é inválida.");
        }

        return email;
    }

    private static string NormalizarTelefone(string chave)
    {
        if (chave.Any(caractere => !char.IsDigit(caractere) && caractere is not ('+' or '(' or ')' or '-' or ' ')))
            throw new DomainException("A chave Pix do tipo telefone é inválida.");

        var somenteDigitos = new string(chave.Where(char.IsDigit).ToArray());
        var dddValido = somenteDigitos.Length is 12 or 13 &&
                        somenteDigitos.StartsWith("55", StringComparison.Ordinal) &&
                        somenteDigitos[2] is >= '1' and <= '9' &&
                        somenteDigitos[3] is >= '1' and <= '9';

        if (!dddValido)
            throw new DomainException("A chave Pix do tipo telefone deve conter código do país 55 e DDD brasileiro válido.");

        return somenteDigitos;
    }

    private static string NormalizarAleatoria(string chave)
    {
        if (!Guid.TryParse(chave.Trim(), out var valor) || valor == Guid.Empty)
            throw new DomainException("A chave Pix aleatória é inválida.");
        return valor.ToString("D");
    }

    private static bool CpfEhValido(string cpf)
    {
        var primeiroDigito = CalcularDigitoVerificador(cpf[..9], 10);
        var segundoDigito = CalcularDigitoVerificador(cpf[..9] + primeiroDigito, 11);

        return cpf[9] - '0' == primeiroDigito && cpf[10] - '0' == segundoDigito;
    }

    private static bool CnpjEhValido(string cnpj)
    {
        var primeiroDigito = CalcularDigitoVerificador(cnpj[..12], 5);
        var segundoDigito = CalcularDigitoVerificador(cnpj[..12] + primeiroDigito, 6);

        return cnpj[12] - '0' == primeiroDigito && cnpj[13] - '0' == segundoDigito;
    }

    private static int CalcularDigitoVerificador(string baseDocumento, int pesoInicial)
    {
        var soma = 0;
        var peso = pesoInicial;

        foreach (var caractere in baseDocumento)
        {
            soma += (caractere - '0') * peso;
            peso = peso == 2 ? 9 : peso - 1;
        }

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
