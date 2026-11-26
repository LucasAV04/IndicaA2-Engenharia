namespace Application.Jornada;

public static class TelefoneBrasileiro
{
    public static string? Normalizar(string? valor, bool opcional = false)
    {
        var texto = valor?.Trim() ?? "";
        if (opcional && texto.Length == 0) return null;

        if (texto.Length > 30 || texto.Skip(texto.StartsWith('+') ? 1 : 0)
                .Any(c => !char.IsAsciiDigit(c) && !" ()-.".Contains(c)))
            throw new ArgumentException("Telefone inválido.");

        var digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
        // Parênteses iniciais identificam um DDD nacional, inclusive 55.
        var temPais = digitos.Length is 12 or 13 && digitos.StartsWith("55", StringComparison.Ordinal)
            && (texto.StartsWith('+') || texto.StartsWith("55", StringComparison.Ordinal));
        var nacional = temPais ? digitos[2..] : digitos;
        if ((texto.StartsWith('+') && !temPais) || nacional.Length is not (10 or 11)
            || nacional[0] == '0' || nacional[1] == '0')
            throw new ArgumentException("Telefone inválido.");

        return digitos;
    }
}
