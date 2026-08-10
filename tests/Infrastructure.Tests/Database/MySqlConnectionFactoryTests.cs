using Infrastructure.Database;
using Xunit;

namespace Infrastructure.Tests.Database;

public sealed class MySqlConnectionFactoryTests
{
    [Fact]
    public void Create_QuandoConnectionStringValida_DeveCriarNovaConexaoSemAbriLa()
    {
        var factory = new MySqlConnectionFactory("Server=localhost;Database=indica_a2;User ID=test;Password=test;");

        using var primeira = factory.Create();
        using var segunda = factory.Create();

        Assert.NotSame(primeira, segunda);
        Assert.Equal(System.Data.ConnectionState.Closed, primeira.State);
        Assert.Equal("indica_a2", primeira.Database);
    }

    [Fact]
    public void Construtor_QuandoConnectionStringVazia_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new MySqlConnectionFactory(" "));
    }
}
