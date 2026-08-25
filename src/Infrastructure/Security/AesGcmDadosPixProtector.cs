using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Security;

public sealed class AesGcmDadosPixProtector : IDadosPixProtector
{
    public const int EncryptionVersion = 1;
    public const int KeySizeInBytes = 32;
    public const int NonceSizeInBytes = 12;
    public const int TagSizeInBytes = 16;

    private readonly byte[] _key;

    public AesGcmDadosPixProtector(string keyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new ArgumentException("A chave de criptografia dos Dados Pix é obrigatória.", nameof(keyBase64));

        try
        {
            _key = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("A chave de criptografia dos Dados Pix deve estar em Base64 válido.", nameof(keyBase64), exception);
        }

        if (_key.Length != KeySizeInBytes)
            throw new ArgumentException("A chave de criptografia dos Dados Pix deve possuir 32 bytes.", nameof(keyBase64));
    }

    public DadosPixProtegido Proteger(string chavePix)
    {
        if (string.IsNullOrWhiteSpace(chavePix))
            throw new ArgumentException("A chave Pix a proteger é obrigatória.", nameof(chavePix));

        var plaintext = Encoding.UTF8.GetBytes(chavePix);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeInBytes];

        using var aesGcm = new AesGcm(_key, TagSizeInBytes);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        return new DadosPixProtegido(ciphertext, nonce, tag, EncryptionVersion);
    }

    public string Desproteger(DadosPixProtegido dadosPixProtegido)
    {
        ArgumentNullException.ThrowIfNull(dadosPixProtegido);

        if (dadosPixProtegido.EncryptionVersion != EncryptionVersion ||
            dadosPixProtegido.Ciphertext.Length == 0 ||
            dadosPixProtegido.Nonce.Length != NonceSizeInBytes ||
            dadosPixProtegido.Tag.Length != TagSizeInBytes)
        {
            throw new CryptographicException("Não foi possível descriptografar os Dados Pix armazenados.");
        }

        var plaintext = new byte[dadosPixProtegido.Ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(_key, TagSizeInBytes);
            aesGcm.Decrypt(
                dadosPixProtegido.Nonce,
                dadosPixProtegido.Ciphertext,
                dadosPixProtegido.Tag,
                plaintext);
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException("Não foi possível descriptografar os Dados Pix armazenados.", exception);
        }

        return Encoding.UTF8.GetString(plaintext);
    }
}
