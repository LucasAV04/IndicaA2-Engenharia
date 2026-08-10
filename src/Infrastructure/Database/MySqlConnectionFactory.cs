using MySqlConnector;

namespace Infrastructure.Database;

public sealed class MySqlConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A connection string é obrigatória.", nameof(connectionString));

        _connectionString = connectionString;
    }

    public MySqlConnection Create() => new(_connectionString);
}
