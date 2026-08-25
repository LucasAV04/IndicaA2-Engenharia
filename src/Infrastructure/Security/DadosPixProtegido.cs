namespace Infrastructure.Security;

public sealed record DadosPixProtegido(
    byte[] Ciphertext,
    byte[] Nonce,
    byte[] Tag,
    int EncryptionVersion);
