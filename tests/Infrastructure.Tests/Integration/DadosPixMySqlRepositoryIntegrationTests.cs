using System.Security.Cryptography;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Infrastructure.Repositories;
using Infrastructure.Security;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DadosPixMySqlRepositoryIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task AdicionarEObterPorUsuarioIdAsync_DeveManterChaveETimestampsSemPlaintextNoBanco()
    {
        await fixture.LimparDadosAsync();
        var (repository, dadosPix) = await CriarDadosPixAsync(TipoChavePix.Email, "pix.teste@example.com");

        await repository.AdicionarAsync(dadosPix, CancellationToken.None);
        var persistido = await repository.ObterPorUsuarioIdAsync(dadosPix.UsuarioId, CancellationToken.None);
        var materialPersistido = await ObterMaterialPersistidoAsync(dadosPix.Id);

        Assert.NotNull(persistido);
        Assert.Equal(dadosPix.Id, persistido.Id);
        Assert.Equal(dadosPix.UsuarioId, persistido.UsuarioId);
        Assert.Equal(TipoChavePix.Email, persistido.TipoChavePix);
        Assert.Equal("pix.teste@example.com", persistido.ChavePix);
        Assert.Equal(dadosPix.CreatedAt, persistido.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(dadosPix.UpdatedAt, persistido.UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(DateTimeKind.Utc, persistido.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, persistido.UpdatedAt.Kind);
        Assert.NotEmpty(materialPersistido.Ciphertext);
        Assert.Equal(AesGcmDadosPixProtector.NonceSizeInBytes, materialPersistido.Nonce.Length);
        Assert.Equal(AesGcmDadosPixProtector.TagSizeInBytes, materialPersistido.Tag.Length);
        Assert.Equal(AesGcmDadosPixProtector.EncryptionVersion, materialPersistido.EncryptionVersion);
        Assert.False(ContemUtf8(materialPersistido.Ciphertext, "pix.teste@example.com"));
        Assert.False(ContemUtf8(materialPersistido.Nonce, "pix.teste@example.com"));
        Assert.False(ContemUtf8(materialPersistido.Tag, "pix.teste@example.com"));
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoCpfForInformadoNaoDevePersistirChaveNormalizadaEmTextoPuro()
    {
        await fixture.LimparDadosAsync();
        var (repository, dadosPix) = await CriarDadosPixAsync(TipoChavePix.Cpf, "123.456.789-09");

        await repository.AdicionarAsync(dadosPix, CancellationToken.None);
        var materialPersistido = await ObterMaterialPersistidoAsync(dadosPix.Id);

        Assert.Equal("12345678909", dadosPix.ChavePix);
        Assert.False(ContemUtf8(materialPersistido.Ciphertext, dadosPix.ChavePix));
        Assert.False(ContemUtf8(materialPersistido.Nonce, dadosPix.ChavePix));
        Assert.False(ContemUtf8(materialPersistido.Tag, dadosPix.ChavePix));
    }

    [MySqlIntegrationFact]
    public async Task AtualizarAsync_DeveGerarNovoMaterialPreservandoUsuarioECriacao()
    {
        await fixture.LimparDadosAsync();
        var (repository, dadosPix) = await CriarDadosPixAsync(TipoChavePix.Email, "primeira.chave@example.com");
        await repository.AdicionarAsync(dadosPix, CancellationToken.None);
        var materialAnterior = await ObterMaterialPersistidoAsync(dadosPix.Id);
        var createdAt = dadosPix.CreatedAt;
        var usuarioId = dadosPix.UsuarioId;

        await Task.Delay(TimeSpan.FromMilliseconds(2));
        dadosPix.Atualizar(TipoChavePix.Email, "segunda.chave@example.com");
        await repository.AtualizarAsync(dadosPix, CancellationToken.None);

        var materialAtual = await ObterMaterialPersistidoAsync(dadosPix.Id);
        var persistido = await repository.ObterPorUsuarioIdAsync(usuarioId, CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.Equal("segunda.chave@example.com", persistido.ChavePix);
        Assert.Equal(usuarioId, persistido.UsuarioId);
        Assert.Equal(createdAt, persistido.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(dadosPix.UpdatedAt, persistido.UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.True(persistido.UpdatedAt >= createdAt);
        Assert.NotEqual(materialAnterior.Nonce, materialAtual.Nonce);
        Assert.NotEqual(materialAnterior.Ciphertext, materialAtual.Ciphertext);
        Assert.NotEqual(materialAnterior.Tag, materialAtual.Tag);
    }

    [MySqlIntegrationFact]
    public async Task RemoverAsync_DeveExcluirDadosPixEPermitirAusenciaPosterior()
    {
        await fixture.LimparDadosAsync();
        var (repository, dadosPix) = await CriarDadosPixAsync(TipoChavePix.Email, "remover@example.com");
        await repository.AdicionarAsync(dadosPix, CancellationToken.None);

        await repository.RemoverAsync(dadosPix, CancellationToken.None);
        var persistido = await repository.ObterPorUsuarioIdAsync(dadosPix.UsuarioId, CancellationToken.None);

        Assert.Null(persistido);
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoUsuarioJaPossuirDadosPixDeveTraduzirConstraintEspecifica()
    {
        await fixture.LimparDadosAsync();
        var (repository, dadosPix) = await CriarDadosPixAsync(TipoChavePix.Email, "primeiro@example.com");
        await repository.AdicionarAsync(dadosPix, CancellationToken.None);
        var duplicado = DadosPix.Reidratar(
            Guid.NewGuid(), dadosPix.UsuarioId, TipoChavePix.Email, "segundo@example.com",
            dadosPix.CreatedAt, dadosPix.UpdatedAt);

        await Assert.ThrowsAsync<DadosPixJaExisteException>(() =>
            repository.AdicionarAsync(duplicado, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoOutraConstraintForVioladaNaoDeveMascararMySqlException()
    {
        await fixture.LimparDadosAsync();
        var (repository, primeiro) = await CriarDadosPixAsync(TipoChavePix.Email, "primeiro@example.com");
        await repository.AdicionarAsync(primeiro, CancellationToken.None);
        var (_, segundo) = await CriarDadosPixAsync(TipoChavePix.Email, "segundo@example.com");
        var idDuplicado = DadosPix.Reidratar(
            primeiro.Id, segundo.UsuarioId, segundo.TipoChavePix, segundo.ChavePix,
            segundo.CreatedAt, segundo.UpdatedAt);

        await Assert.ThrowsAsync<MySqlException>(() => repository.AdicionarAsync(idDuplicado, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoUsuarioNaoExistirDevePropagarFalhaDeForeignKey()
    {
        await fixture.LimparDadosAsync();
        var repository = CriarRepository();
        var dadosPix = new DadosPix(Guid.NewGuid(), TipoChavePix.Email, "fk-invalida@example.com");

        await Assert.ThrowsAsync<MySqlException>(() => repository.AdicionarAsync(dadosPix, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task ObterPorUsuarioIdAsync_QuandoCiphertextForAdulteradoDeveFalharNaAutenticacao()
    {
        await fixture.LimparDadosAsync();
        var (repository, dadosPix) = await CriarDadosPixAsync(TipoChavePix.Email, "autenticado@example.com");
        await repository.AdicionarAsync(dadosPix, CancellationToken.None);
        var material = await ObterMaterialPersistidoAsync(dadosPix.Id);
        material.Ciphertext[0] ^= 1;

        await using (var connection = fixture.ConnectionFactory.Create())
        {
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "UPDATE dados_pix SET chave_pix_ciphertext = @ciphertext WHERE id = @id;", connection);
            command.Parameters.Add("@ciphertext", MySqlDbType.Blob).Value = material.Ciphertext;
            command.Parameters.Add("@id", MySqlDbType.VarChar).Value = dadosPix.Id.ToString();
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            repository.ObterPorUsuarioIdAsync(dadosPix.UsuarioId, CancellationToken.None));
        Assert.DoesNotContain(dadosPix.ChavePix, exception.Message, StringComparison.Ordinal);
    }

    private async Task<(DadosPixMySqlRepository Repository, DadosPix DadosPix)> CriarDadosPixAsync(
        TipoChavePix tipoChavePix,
        string chavePix)
    {
        var usuarioRepository = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var usuario = IntegrationTestData.CriarUsuario();
        await usuarioRepository.AdicionarAsync(usuario, CancellationToken.None);

        return (CriarRepository(), new DadosPix(usuario.Id, tipoChavePix, chavePix));
    }

    private DadosPixMySqlRepository CriarRepository() =>
        new(fixture.ConnectionFactory, new AesGcmDadosPixProtector(CriarChaveDeTesteBase64()));

    private async Task<MaterialPersistido> ObterMaterialPersistidoAsync(Guid id)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            """
            SELECT chave_pix_ciphertext, chave_pix_nonce, chave_pix_tag, encryption_version
            FROM dados_pix
            WHERE id = @id;
            """,
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = id.ToString();
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        return new MaterialPersistido(
            reader.GetFieldValue<byte[]>(0),
            reader.GetFieldValue<byte[]>(1),
            reader.GetFieldValue<byte[]>(2),
            reader.GetInt32(3));
    }

    private static string CriarChaveDeTesteBase64() =>
        Convert.ToBase64String(Enumerable.Range(1, AesGcmDadosPixProtector.KeySizeInBytes).Select(valor => (byte)valor).ToArray());

    private static bool ContemUtf8(byte[] dados, string valor) =>
        ContemSequencia(dados, System.Text.Encoding.UTF8.GetBytes(valor));

    private static bool ContemSequencia(byte[] origem, byte[] sequencia)
    {
        if (sequencia.Length > origem.Length)
            return false;

        for (var indice = 0; indice <= origem.Length - sequencia.Length; indice++)
        {
            if (origem.AsSpan(indice, sequencia.Length).SequenceEqual(sequencia))
                return true;
        }

        return false;
    }

    private sealed record MaterialPersistido(byte[] Ciphertext, byte[] Nonce, byte[] Tag, int EncryptionVersion);
}
