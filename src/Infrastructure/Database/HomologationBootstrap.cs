using System.Net.Mail;
using System.Text;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Repositories;
using Infrastructure.Security;
using MySqlConnector;

namespace Infrastructure.Database;

public static class HomologationBootstrap
{
    public static async Task CreateAsync(MySqlConnection connection, MigrationJournal journal,
        MySqlConnectionFactory factory, string name, string email, string password, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150 || email.Length > 254
            || !MailAddress.TryCreate(email, out var address) || address.Address != email
            || string.IsNullOrWhiteSpace(password) || Encoding.UTF8.GetByteCount(password) is < 20 or > 72)
            throw new InvalidOperationException("Credencial administrativa inválida.");

        await MigrationJournal.AcquireLockAsync(connection, "bootstrap", token);
        if (!await journal.IsReadyAsync(connection, token))
            throw new InvalidOperationException("Schema incompleto.");

        var hasher = new BCryptPasswordHasher();
        await using var command = new MySqlCommand("""
            SELECT nome,email,senha_hash,tipo_usuario FROM usuarios
            WHERE tipo_usuario=@admin OR email=@email
            """, connection);
        command.Parameters.AddWithValue("@admin", (int)TipoUsuario.Administrador);
        command.Parameters.AddWithValue("@email", email);
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
            {
                if (reader.GetInt32(3) != (int)TipoUsuario.Administrador
                    || !string.Equals(email, reader.GetString(1), StringComparison.OrdinalIgnoreCase)
                    || reader.GetString(0) != name.Trim() || !hasher.VerifyPassword(password, reader.GetString(2))
                    || await reader.ReadAsync(token))
                    throw new InvalidOperationException("Administrador ou e-mail conflitante.");
                return;
            }
        }

        await new UsuarioMySqlRepository(factory).AdicionarAsync(
            new Usuario(name, email, hasher.HashPassword(password), tipoUsuario: TipoUsuario.Administrador), token);
    }
}
