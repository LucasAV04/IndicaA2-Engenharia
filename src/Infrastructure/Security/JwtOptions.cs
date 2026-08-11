namespace Infrastructure.Security;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public int ExpirationMinutes { get; init; } = 60;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience)) throw new InvalidOperationException("As configurações Jwt:Issuer e Jwt:Audience são obrigatórias.");
        if (string.IsNullOrWhiteSpace(Key) || System.Text.Encoding.UTF8.GetByteCount(Key) < 32) throw new InvalidOperationException("A configuração Jwt:Key é obrigatória e deve possuir ao menos 32 bytes.");
        if (ExpirationMinutes <= 0) throw new InvalidOperationException("A configuração Jwt:ExpirationMinutes deve ser maior que zero.");
    }
}
