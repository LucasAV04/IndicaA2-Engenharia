using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Application.Recebimentos;

public static partial class RecebimentoPixValidacao
{
    [GeneratedRegex("^[a-zA-Z0-9]{26,35}$", RegexOptions.CultureInvariant)]
    private static partial Regex TxidRegex();
    [GeneratedRegex("^E[0-9]{8}[0-9]{12}[a-zA-Z0-9]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex E2eRegex();
    public static bool TxidValido(string? value) => value is not null && TxidRegex().IsMatch(value);
    public static bool E2eValido(string? value) => value is not null && E2eRegex().IsMatch(value);
    public static bool EventoValido(EventoPix e, DateTime agora) => TxidValido(e.Txid) && E2eValido(e.EndToEndId)
        && e.Valor > 0 && e.Valor <= 9999999999.99m && decimal.Round(e.Valor, 2) == e.Valor
        && e.Horario.Kind == DateTimeKind.Utc && e.Horario.Year >= 2020 && e.Horario <= agora.AddMinutes(5);
    // DATETIME(6) preserva microssegundos. Canonicalizar antes de comparar ou gerar
    // a identidade evita divergência artificial entre o JSON e a leitura MySQL.
    public static EventoPix Canonicalizar(EventoPix e) => e with
    {
        Horario = new DateTime(e.Horario.Ticks - e.Horario.Ticks % 10, e.Horario.Kind)
    };
    public static byte[] HashEvento(EventoPix e)
    {
        e = Canonicalizar(e);
        return SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', e.EndToEndId, e.Txid,
            e.Valor.ToString("F2", CultureInfo.InvariantCulture), e.Horario.ToString("O", CultureInfo.InvariantCulture))));
    }
    public static byte[]? HashLink(string token)
    {
        if (token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c))) return null;
        return SHA256.HashData(Encoding.ASCII.GetBytes(token));
    }
}
