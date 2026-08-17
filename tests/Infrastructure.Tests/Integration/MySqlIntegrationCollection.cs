using Xunit;

namespace Infrastructure.Tests.Integration;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MySqlIntegrationCollection : ICollectionFixture<MySqlIntegrationFixture>
{
    public const string Name = "MySqlIntegration";
}

public sealed class MySqlIntegrationFactAttribute : FactAttribute
{
    public MySqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MySqlIntegrationFixture.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Configure a variavel de ambiente {MySqlIntegrationFixture.ConnectionStringEnvironmentVariable} com uma conexao administrativa MySQL sem database para executar os testes de integracao.";
        }
    }
}
