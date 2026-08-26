using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Security;

public sealed class AesGcmDadosPixProtector : IDadosPixProtector, IDisposable
{
    public const int EncryptionVersion = 1;
    public const int KeySizeInBytes = 32;
    public const int NonceSizeInBytes = 12;
    public const int TagSizeInBytes = 16;

    private byte[]? _key;

    public AesGcmDadosPixProtector(string keyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new ArgumentException("A chave de criptografia dos Dados Pix é obrigatória.", nameof(keyBase64));

        byte[] key;

        try
        {
            key = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("A chave de criptografia dos Dados Pix deve estar em Base64 válido.", nameof(keyBase64), exception);
        }

        if (key.Length != KeySizeInBytes)
        {
            CryptographicOperations.ZeroMemory(key);

            throw new ArgumentException("A chave de criptografia dos Dados Pix deve possuir 32 bytes.", nameof(keyBase64));
        }

        _key = key;
    }

    public DadosPixProtegido Proteger(string chavePix)
    {
        return Proteger(chavePix, Array.Empty<byte>());
    }

    public DadosPixProtegido Proteger(string chavePix, byte[] associatedData)
    {
        if (string.IsNullOrWhiteSpace(chavePix))
            throw new ArgumentException("A chave Pix a proteger é obrigatória.", nameof(chavePix));
        ArgumentNullException.ThrowIfNull(associatedData);

        var plaintext = Encoding.UTF8.GetBytes(chavePix);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeInBytes];

        try
        {
            using var aesGcm = new AesGcm(ObterChave(), TagSizeInBytes);
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        return new DadosPixProtegido(ciphertext, nonce, tag, EncryptionVersion);
    }

    public string Desproteger(DadosPixProtegido dadosPixProtegido)
    {
        return Desproteger(dadosPixProtegido, Array.Empty<byte>());
    }

    public string Desproteger(DadosPixProtegido dadosPixProtegido, byte[] associatedData)
    {
        ArgumentNullException.ThrowIfNull(dadosPixProtegido);
        ArgumentNullException.ThrowIfNull(associatedData);

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
            using var aesGcm = new AesGcm(ObterChave(), TagSizeInBytes);
            aesGcm.Decrypt(
                dadosPixProtegido.Nonce,
                dadosPixProtegido.Ciphertext,
                dadosPixProtegido.Tag,
                plaintext,
                associatedData);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException("Não foi possível descriptografar os Dados Pix armazenados.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Dispose()
    {
        if (_key is null)
            return;

        CryptographicOperations.ZeroMemory(_key);
        _key = null;
    }

    private byte[] ObterChave()
    {
        return _key ?? throw new ObjectDisposedException(nameof(AesGcmDadosPixProtector));
    }
}
