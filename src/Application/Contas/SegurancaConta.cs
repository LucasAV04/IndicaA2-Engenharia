using System.Security.Cryptography;
using System.Text;

namespace Application.Contas;

public enum FinalidadeTokenConta : byte
{
    ConfirmacaoEmail,
    RedefinicaoSenha
}

public static class SegurancaConta
{
    public static bool NovaSenhaValida(string? senha) =>
        !string.IsNullOrWhiteSpace(senha) && senha.Length >= 8
        && Encoding.UTF8.GetByteCount(senha) <= 72;

    public static TimeSpan Validade(FinalidadeTokenConta finalidade) => finalidade switch
    {
        FinalidadeTokenConta.ConfirmacaoEmail => TimeSpan.FromHours(24),
        FinalidadeTokenConta.RedefinicaoSenha => TimeSpan.FromMinutes(30),
        _ => throw new ArgumentOutOfRangeException(nameof(finalidade))
    };

    public static string GerarToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static byte[]? HashToken(string? token)
    {
        if (token is not { Length: 64 } ||
            token.Any(c => !char.IsAsciiHexDigitLower(c)))
            return null;

        return SHA256.HashData(Convert.FromHexString(token));
    }
}
