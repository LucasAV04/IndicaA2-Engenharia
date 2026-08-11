using Application.Interfaces.Security;

namespace Infrastructure.Security;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("A senha é obrigatória.", nameof(password));
        return BCrypt.Net.BCrypt.HashPassword(password);
    }
    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("A senha é obrigatória.", nameof(password));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("O hash da senha é obrigatório.", nameof(passwordHash));
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }
}
