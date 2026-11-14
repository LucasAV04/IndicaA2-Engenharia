using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Contas;

namespace Infrastructure.Security;

public sealed record TokenContaCifrado(byte[] Ciphertext, byte[] Nonce, byte[] Tag, byte Versao);

public sealed class TokenContaProtector
{
    private readonly byte[] _key;

    public TokenContaProtector(string keyBase64)
    {
        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length != 32)
            throw new ArgumentException("A chave de conta deve ter 32 bytes.", nameof(keyBase64));
    }

    public TokenContaCifrado Cifrar(string token, Guid usuario, Guid referencia, FinalidadeTokenConta finalidade)
    {
        var plaintext = Encoding.UTF8.GetBytes(token);
        try
        {
            var ciphertext = new byte[plaintext.Length];
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            using var aes = new AesGcm(_key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Aad(usuario, referencia, finalidade));
            return new TokenContaCifrado(ciphertext, nonce, tag, 1);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Decifrar(TokenContaCifrado material, Guid usuario, Guid referencia,
        FinalidadeTokenConta finalidade)
    {
        if (material.Versao != 1 || material.Nonce.Length != 12 || material.Tag.Length != 16
            || material.Ciphertext.Length != 64)
            throw new CryptographicException("Material de conta inválido.");

        var plaintext = new byte[material.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(material.Nonce, material.Ciphertext, material.Tag, plaintext, Aad(usuario, referencia, finalidade));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] Aad(Guid usuario, Guid referencia, FinalidadeTokenConta finalidade) =>
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"indicaa2:conta:v1:{usuario:N}:{referencia:N}:{(byte)finalidade}"));
}
