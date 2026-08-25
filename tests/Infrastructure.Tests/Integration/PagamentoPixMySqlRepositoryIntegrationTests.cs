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
public sealed class PagamentoPixMySqlRepositoryIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task AdicionarEConsultar_DevePreservarSnapshotsSemPersistirPlaintext()
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoPix) = await CriarPagamentoPixAsync();

        await repository.AdicionarAsync(pagamentoPix, CancellationToken.None);

        var porId = await repository.ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None);
        var porCashback = await repository.ObterPorCashbackIdAsync(pagamentoPix.CashbackId, CancellationToken.None);
        var porBeneficiario = await repository.ObterPorUsuarioBeneficiarioIdAsync(
            pagamentoPix.UsuarioBeneficiarioId,
            CancellationToken.None);
        var registro = await ObterRegistroPersistidoAsync(pagamentoPix.Id);

        Assert.NotNull(porId);
        Assert.NotNull(porCashback);
        Assert.Contains(porBeneficiario, item => item.Id == pagamentoPix.Id);
        Assert.Equal(pagamentoPix.CashbackId, porId!.CashbackId);
        Assert.Equal(pagamentoPix.UsuarioBeneficiarioId, porId.UsuarioBeneficiarioId);
        Assert.Equal(pagamentoPix.Valor, porId.Valor);
        Assert.Equal(pagamentoPix.TipoChavePix, porId.TipoChavePix);
        Assert.Equal(pagamentoPix.ChavePix, porId.ChavePix);
        Assert.Equal(StatusPagamentoPix.Pendente, porId.Status);
        Assert.Equal(0, porId.QuantidadeTentativas);
        Assert.Equal(pagamentoPix.CreatedAt, porId.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(pagamentoPix.UpdatedAt, porId.UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(DateTimeKind.Utc, porId.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, porId.UpdatedAt.Kind);
        Assert.NotEmpty(registro.Ciphertext);
        Assert.Equal(AesGcmDadosPixProtector.NonceSizeInBytes, registro.Nonce.Length);
        Assert.Equal(AesGcmDadosPixProtector.TagSizeInBytes, registro.Tag.Length);
        Assert.Equal(AesGcmDadosPixProtector.EncryptionVersion, registro.EncryptionVersion);
        Assert.False(ContemUtf8(registro.Ciphertext, pagamentoPix.ChavePix));
        Assert.False(ContemUtf8(registro.Nonce, pagamentoPix.ChavePix));
        Assert.False(ContemUtf8(registro.Tag, pagamentoPix.ChavePix));
    }

    [MySqlIntegrationFact]
    public async Task AtualizarAsync_DeveAlterarSomenteEstadoTentativasEDataDeAtualizacao()
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoPix) = await CriarPagamentoPixAsync();
        await repository.AdicionarAsync(pagamentoPix, CancellationToken.None);
        var anterior = await ObterRegistroPersistidoAsync(pagamentoPix.Id);

        await Task.Delay(TimeSpan.FromMilliseconds(2));
        pagamentoPix.IniciarTentativa();
        await repository.AtualizarAsync(pagamentoPix, CancellationToken.None);

        var atual = await ObterRegistroPersistidoAsync(pagamentoPix.Id);
        var reidratado = await repository.ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None);

        Assert.Equal(anterior.CashbackId, atual.CashbackId);
        Assert.Equal(anterior.UsuarioBeneficiarioId, atual.UsuarioBeneficiarioId);
        Assert.Equal(anterior.Valor, atual.Valor);
        Assert.Equal(anterior.TipoChavePix, atual.TipoChavePix);
        Assert.Equal(anterior.Ciphertext, atual.Ciphertext);
        Assert.Equal(anterior.Nonce, atual.Nonce);
        Assert.Equal(anterior.Tag, atual.Tag);
        Assert.Equal(anterior.EncryptionVersion, atual.EncryptionVersion);
        Assert.Equal(anterior.CreatedAt, atual.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal((int)StatusPagamentoPix.Processando, atual.Status);
        Assert.Equal(1, atual.QuantidadeTentativas);
        Assert.True(atual.UpdatedAt >= anterior.UpdatedAt);
        Assert.NotNull(reidratado);
        Assert.Equal(StatusPagamentoPix.Processando, reidratado!.Status);
        Assert.Equal(1, reidratado.QuantidadeTentativas);
    }

    [MySqlIntegrationFact]
    public async Task AdicionarEObterPorIdAsync_DevePreservarTodosOsEstadosValidosETentativas()
    {
        await fixture.LimparDadosAsync();
        var cenarios = new[]
        {
            (Status: StatusPagamentoPix.Pendente, Tentativas: 0),
            (Status: StatusPagamentoPix.Processando, Tentativas: 1),
            (Status: StatusPagamentoPix.Falhou, Tentativas: 1),
            (Status: StatusPagamentoPix.Processando, Tentativas: 5),
            (Status: StatusPagamentoPix.FalhaDefinitiva, Tentativas: 5),
            (Status: StatusPagamentoPix.Concluido, Tentativas: 1),
            (Status: StatusPagamentoPix.Cancelado, Tentativas: 0),
            (Status: StatusPagamentoPix.Cancelado, Tentativas: 1)
        };

        foreach (var cenario in cenarios)
        {
            var (repository, pagamentoOriginal) = await CriarPagamentoPixAsync();
            var pagamentoPix = PagamentoPix.Reidratar(
                pagamentoOriginal.Id,
                pagamentoOriginal.CashbackId,
                pagamentoOriginal.UsuarioBeneficiarioId,
                pagamentoOriginal.Valor,
                pagamentoOriginal.TipoChavePix,
                pagamentoOriginal.ChavePix,
                cenario.Status,
                cenario.Tentativas,
                pagamentoOriginal.CreatedAt,
                pagamentoOriginal.UpdatedAt);

            await repository.AdicionarAsync(pagamentoPix, CancellationToken.None);

            var reidratado = await repository.ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None);

            Assert.NotNull(reidratado);
            Assert.Equal(cenario.Status, reidratado!.Status);
            Assert.Equal(cenario.Tentativas, reidratado.QuantidadeTentativas);
        }
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoCashbackJaPossuirOrdem_DeveTraduzirConstraintEspecifica()
    {
        await fixture.LimparDadosAsync();
        var (repository, primeiro) = await CriarPagamentoPixAsync();
        await repository.AdicionarAsync(primeiro, CancellationToken.None);
        var duplicado = PagamentoPix.Reidratar(
            Guid.NewGuid(),
            primeiro.CashbackId,
            primeiro.UsuarioBeneficiarioId,
            primeiro.Valor,
            primeiro.TipoChavePix,
            "segundo.snapshot@exemplo.com",
            StatusPagamentoPix.Pendente,
            0,
            primeiro.CreatedAt,
            primeiro.UpdatedAt);

        await Assert.ThrowsAsync<PagamentoPixJaExisteException>(() =>
            repository.AdicionarAsync(duplicado, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoOutraConstraintForViolada_NaoDeveMascararMySqlException()
    {
        await fixture.LimparDadosAsync();
        var (repository, primeiro) = await CriarPagamentoPixAsync();
        await repository.AdicionarAsync(primeiro, CancellationToken.None);
        var (_, segundo) = await CriarPagamentoPixAsync();
        var idDuplicado = PagamentoPix.Reidratar(
            primeiro.Id,
            segundo.CashbackId,
            segundo.UsuarioBeneficiarioId,
            segundo.Valor,
            segundo.TipoChavePix,
            segundo.ChavePix,
            segundo.Status,
            segundo.QuantidadeTentativas,
            segundo.CreatedAt,
            segundo.UpdatedAt);

        await Assert.ThrowsAsync<MySqlException>(() =>
            repository.AdicionarAsync(idDuplicado, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoCashbackOuUsuarioNaoExistirem_DevePropagarFalhaDeForeignKey()
    {
        await fixture.LimparDadosAsync();
        var repository = CriarRepository();
        var cashbackInexistente = PagamentoPix.Criar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            TipoChavePix.Email,
            "fk-cashback@exemplo.com");

        await Assert.ThrowsAsync<MySqlException>(() =>
            repository.AdicionarAsync(cashbackInexistente, CancellationToken.None));

        var (_, pagamentoValido) = await CriarPagamentoPixAsync();
        var usuarioInexistente = PagamentoPix.Reidratar(
            Guid.NewGuid(),
            pagamentoValido.CashbackId,
            Guid.NewGuid(),
            pagamentoValido.Valor,
            pagamentoValido.TipoChavePix,
            pagamentoValido.ChavePix,
            pagamentoValido.Status,
            pagamentoValido.QuantidadeTentativas,
            pagamentoValido.CreatedAt,
            pagamentoValido.UpdatedAt);

        await Assert.ThrowsAsync<MySqlException>(() =>
            repository.AdicionarAsync(usuarioInexistente, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public Task ObterPorIdAsync_QuandoCiphertextForAdulterado_DeveFalharSemExporChavePix()
    {
        return ValidarMaterialCriptograficoAdulteradoAsync("chave_pix_ciphertext");
    }

    [MySqlIntegrationFact]
    public Task ObterPorIdAsync_QuandoNonceForAdulterado_DeveFalharSemExporChavePix()
    {
        return ValidarMaterialCriptograficoAdulteradoAsync("chave_pix_nonce");
    }

    [MySqlIntegrationFact]
    public Task ObterPorIdAsync_QuandoTagForAdulterada_DeveFalharSemExporChavePix()
    {
        return ValidarMaterialCriptograficoAdulteradoAsync("chave_pix_tag");
    }

    private async Task ValidarMaterialCriptograficoAdulteradoAsync(string coluna)
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoPix) = await CriarPagamentoPixAsync();
        await repository.AdicionarAsync(pagamentoPix, CancellationToken.None);
        var registro = await ObterRegistroPersistidoAsync(pagamentoPix.Id);
        var materialAdulterado = coluna switch
        {
            "chave_pix_ciphertext" => registro.Ciphertext.ToArray(),
            "chave_pix_nonce" => registro.Nonce.ToArray(),
            _ => registro.Tag.ToArray()
        };
        materialAdulterado[0] ^= 1;

        await using (var connection = fixture.ConnectionFactory.Create())
        {
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                $"UPDATE pagamentos_pix SET {coluna} = @material WHERE id = @id;",
                connection);
            command.Parameters.Add("@material", MySqlDbType.Blob).Value = materialAdulterado;
            command.Parameters.Add("@id", MySqlDbType.VarChar).Value = pagamentoPix.Id.ToString();
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            repository.ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None));
        Assert.DoesNotContain(pagamentoPix.ChavePix, exception.Message, StringComparison.Ordinal);
    }

    private async Task<(PagamentoPixMySqlRepository Repository, PagamentoPix PagamentoPix)> CriarPagamentoPixAsync()
    {
        var usuarioRepository = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistoriaRepository = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var indicacaoRepository = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var pagamentoVistoriaRepository = new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        var cashbackRepository = new CashbackMySqlRepository(fixture.ConnectionFactory);
        var indicador = IntegrationTestData.CriarUsuario();
        var indicada = IntegrationTestData.CriarUsuario();
        await usuarioRepository.AdicionarAsync(indicador, CancellationToken.None);
        await usuarioRepository.AdicionarAsync(indicada, CancellationToken.None);

        var vistoria = IntegrationTestData.CriarVistoria(indicada.Id);
        await vistoriaRepository.AdicionarAsync(vistoria, CancellationToken.None);
        var indicacao = new Indicacao(
            indicador.Id,
            "Indicada Pix",
            "11999999999",
            indicador.CodigoIndicacao!);
        indicacao.VincularVistoria(vistoria.Id);
        await indicacaoRepository.AdicionarAsync(indicacao, CancellationToken.None);

        var pagamentoVistoria = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id);
        pagamentoVistoria.Confirmar();
        await pagamentoVistoriaRepository.AdicionarAsync(pagamentoVistoria, CancellationToken.None);

        var cashback = Cashback.Criar(
            indicacao.Id,
            pagamentoVistoria.Id,
            indicador.Id,
            pagamentoVistoria.Valor);
        cashback.Aprovar();
        await cashbackRepository.AdicionarAsync(cashback, CancellationToken.None);

        return (
            CriarRepository(),
            PagamentoPix.Criar(
                cashback.Id,
                indicador.Id,
                cashback.Valor,
                TipoChavePix.Email,
                "snapshot.pagamento@exemplo.com"));
    }

    private PagamentoPixMySqlRepository CriarRepository() =>
        new(fixture.ConnectionFactory, new AesGcmDadosPixProtector(CriarChaveDeTesteBase64()));

    private async Task<RegistroPersistido> ObterRegistroPersistidoAsync(Guid id)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            """
            SELECT cashback_id, usuario_beneficiario_id, valor, tipo_chave_pix,
                   chave_pix_ciphertext, chave_pix_nonce, chave_pix_tag, encryption_version,
                   status, quantidade_tentativas, created_at, updated_at
            FROM pagamentos_pix
            WHERE id = @id;
            """,
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = id.ToString();
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        return new RegistroPersistido(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetDecimal(2),
            reader.GetInt32(3),
            reader.GetFieldValue<byte[]>(4),
            reader.GetFieldValue<byte[]>(5),
            reader.GetFieldValue<byte[]>(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Utc));
    }

    private static string CriarChaveDeTesteBase64() =>
        Convert.ToBase64String(
            Enumerable.Range(1, AesGcmDadosPixProtector.KeySizeInBytes)
                .Select(valor => (byte)valor)
                .ToArray());

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

    private sealed record RegistroPersistido(
        string CashbackId,
        string UsuarioBeneficiarioId,
        decimal Valor,
        int TipoChavePix,
        byte[] Ciphertext,
        byte[] Nonce,
        byte[] Tag,
        int EncryptionVersion,
        int Status,
        int QuantidadeTentativas,
        DateTime CreatedAt,
        DateTime UpdatedAt);
}
