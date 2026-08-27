using System.Data;
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
    public async Task TentarIniciarProcessamentoAsync_QuandoPendente_DeveAdquirirAtomicaEPreservarSnapshots()
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoPix) = await CriarPagamentoPixAsync();
        await repository.AdicionarAsync(pagamentoPix, CancellationToken.None);
        var antes = await ObterRegistroPersistidoAsync(pagamentoPix.Id);
        var statusCashbackAntes = await ObterStatusCashbackAsync(pagamentoPix.CashbackId);

        var adquirido = await repository.TentarIniciarProcessamentoAsync(pagamentoPix.Id, CancellationToken.None);

        var depois = await ObterRegistroPersistidoAsync(pagamentoPix.Id);
        var reidratado = await repository.ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None);
        Assert.True(adquirido);
        Assert.Equal((int)StatusPagamentoPix.Processando, depois.Status);
        Assert.Equal(1, depois.QuantidadeTentativas);
        Assert.True(depois.UpdatedAt >= antes.UpdatedAt);
        AssertSnapshotsEProtecaoPreservados(antes, depois);
        Assert.Equal(statusCashbackAntes, await ObterStatusCashbackAsync(pagamentoPix.CashbackId));
        Assert.NotNull(reidratado);
        Assert.Equal(StatusPagamentoPix.Processando, reidratado!.Status);
        Assert.Equal(1, reidratado.QuantidadeTentativas);
    }

    [MySqlIntegrationFact]
    public async Task TentarIniciarProcessamentoAsync_QuandoFalhou_DeveAdquirirQuintaTentativaSemMarcarFalhaDefinitiva()
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoPix) = await CriarPagamentoPixAsync();
        var falhouComQuatroTentativas = PagamentoPix.Reidratar(
            pagamentoPix.Id,
            pagamentoPix.CashbackId,
            pagamentoPix.UsuarioBeneficiarioId,
            pagamentoPix.Valor,
            pagamentoPix.TipoChavePix,
            pagamentoPix.ChavePix,
            StatusPagamentoPix.Falhou,
            4,
            pagamentoPix.CreatedAt,
            pagamentoPix.UpdatedAt);
        await repository.AdicionarAsync(falhouComQuatroTentativas, CancellationToken.None);
        var antes = await ObterRegistroPersistidoAsync(pagamentoPix.Id);

        var adquirido = await repository.TentarIniciarProcessamentoAsync(pagamentoPix.Id, CancellationToken.None);

        var depois = await ObterRegistroPersistidoAsync(pagamentoPix.Id);
        Assert.True(adquirido);
        Assert.Equal((int)StatusPagamentoPix.Processando, depois.Status);
        Assert.Equal(PagamentoPix.TentativasMaximas, depois.QuantidadeTentativas);
        AssertSnapshotsEProtecaoPreservados(antes, depois);
    }

    [MySqlIntegrationFact]
    public Task TentarIniciarProcessamentoAsync_QuandoProcessando_DeveRecusarSemAlterarRegistro() =>
        ValidarEstadoNaoElegivelAsync(StatusPagamentoPix.Processando, 1);

    [MySqlIntegrationFact]
    public Task TentarIniciarProcessamentoAsync_QuandoConcluido_DeveRecusarSemAlterarRegistro() =>
        ValidarEstadoNaoElegivelAsync(StatusPagamentoPix.Concluido, 1);

    [MySqlIntegrationFact]
    public Task TentarIniciarProcessamentoAsync_QuandoFalhaDefinitiva_DeveRecusarSemAlterarRegistro() =>
        ValidarEstadoNaoElegivelAsync(StatusPagamentoPix.FalhaDefinitiva, PagamentoPix.TentativasMaximas);

    [MySqlIntegrationFact]
    public Task TentarIniciarProcessamentoAsync_QuandoCancelado_DeveRecusarSemAlterarRegistro() =>
        ValidarEstadoNaoElegivelAsync(StatusPagamentoPix.Cancelado, 0);

    [MySqlIntegrationFact]
    public Task TentarIniciarProcessamentoAsync_QuandoLimiteDeTentativasForAtingido_DeveRecusarSextaTentativa() =>
        ValidarEstadoNaoElegivelAsync(StatusPagamentoPix.Processando, PagamentoPix.TentativasMaximas);

    private async Task ValidarEstadoNaoElegivelAsync(
        StatusPagamentoPix status,
        int quantidadeTentativas)
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoPix) = await CriarPagamentoPixAsync();
        var estadoNaoElegivel = PagamentoPix.Reidratar(
            pagamentoPix.Id,
            pagamentoPix.CashbackId,
            pagamentoPix.UsuarioBeneficiarioId,
            pagamentoPix.Valor,
            pagamentoPix.TipoChavePix,
            pagamentoPix.ChavePix,
            status,
            quantidadeTentativas,
            pagamentoPix.CreatedAt,
            pagamentoPix.UpdatedAt);
        await repository.AdicionarAsync(estadoNaoElegivel, CancellationToken.None);
        var antes = await ObterRegistroPersistidoAsync(pagamentoPix.Id);

        var adquirido = await repository.TentarIniciarProcessamentoAsync(pagamentoPix.Id, CancellationToken.None);

        var depois = await ObterRegistroPersistidoAsync(pagamentoPix.Id);
        Assert.False(adquirido);
        Assert.Equal(antes.Status, depois.Status);
        Assert.Equal(antes.QuantidadeTentativas, depois.QuantidadeTentativas);
        Assert.Equal(antes.UpdatedAt, depois.UpdatedAt, TimeSpan.FromMilliseconds(1));
        AssertSnapshotsEProtecaoPreservados(antes, depois);
    }

    [MySqlIntegrationFact]
    public Task TentarIniciarProcessamentoAsync_QuandoDoisExecutoresConcorrerem_DevePermitirSomenteUmClaim() =>
        ValidarConcorrenciaDeClaimAsync(2);

    [MySqlIntegrationFact]
    public Task TentarIniciarProcessamentoAsync_QuandoCincoExecutoresConcorrerem_DevePermitirSomenteUmClaim() =>
        ValidarConcorrenciaDeClaimAsync(5);

    [MySqlIntegrationFact]
    public Task TentarIniciarProcessamentoAsync_QuandoDezExecutoresConcorrerem_DevePermitirSomenteUmClaim() =>
        ValidarConcorrenciaDeClaimAsync(10);

    [MySqlIntegrationFact]
    public async Task TentarIniciarProcessamentoAsync_QuandoCancellationForSolicitado_NaoDeveAlterarOrdem()
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoPix) = await CriarPagamentoPixAsync();
        await repository.AdicionarAsync(pagamentoPix, CancellationToken.None);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.TentarIniciarProcessamentoAsync(pagamentoPix.Id, cancellationTokenSource.Token));

        var depois = await ObterRegistroPersistidoAsync(pagamentoPix.Id);
        Assert.Equal((int)StatusPagamentoPix.Pendente, depois.Status);
        Assert.Equal(0, depois.QuantidadeTentativas);
    }

    private async Task ValidarConcorrenciaDeClaimAsync(int quantidadeExecutores)
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoPix) = await CriarPagamentoPixAsync();
        await repository.AdicionarAsync(pagamentoPix, CancellationToken.None);
        var antes = await ObterRegistroPersistidoAsync(pagamentoPix.Id);
        var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claims = Enumerable.Range(0, quantidadeExecutores)
            .Select(async _ =>
            {
                await inicio.Task;
                return await CriarRepository().TentarIniciarProcessamentoAsync(
                    pagamentoPix.Id,
                    CancellationToken.None);
            })
            .ToArray();

        inicio.SetResult();
        var resultados = await Task.WhenAll(claims);

        var depois = await ObterRegistroPersistidoAsync(pagamentoPix.Id);
        Assert.Equal(1, resultados.Count(resultado => resultado));
        Assert.Equal((int)StatusPagamentoPix.Processando, depois.Status);
        Assert.Equal(1, depois.QuantidadeTentativas);
        AssertSnapshotsEProtecaoPreservados(antes, depois);
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

    [MySqlIntegrationFact]
    public async Task ObterPorIdAsync_QuandoMaterialDeOutraOrdemForCopiadoDeveFalharNaAutenticacao()
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoA) = await CriarPagamentoPixAsync("ordem-a@exemplo.com");
        var (_, pagamentoB) = await CriarPagamentoPixAsync("ordem-b@exemplo.com");
        await repository.AdicionarAsync(pagamentoA, CancellationToken.None);
        await repository.AdicionarAsync(pagamentoB, CancellationToken.None);
        var materialB = await ObterRegistroPersistidoAsync(pagamentoB.Id);

        await AtualizarMaterialCriptograficoAsync(pagamentoA.Id, materialB);

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            repository.ObterPorIdAsync(pagamentoA.Id, CancellationToken.None));
        Assert.DoesNotContain(pagamentoA.ChavePix, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(pagamentoB.ChavePix, exception.Message, StringComparison.Ordinal);
    }

    [MySqlIntegrationFact]
    public Task ObterPorIdAsync_QuandoCashbackIdAutenticadoForAlteradoDeveFalharNaAutenticacao()
    {
        return ValidarAlteracaoDeContextoAsync("cashback_id");
    }

    [MySqlIntegrationFact]
    public Task ObterPorIdAsync_QuandoUsuarioBeneficiarioAutenticadoForAlteradoDeveFalharNaAutenticacao()
    {
        return ValidarAlteracaoDeContextoAsync("usuario_beneficiario_id");
    }

    [MySqlIntegrationFact]
    public Task ObterPorIdAsync_QuandoValorAutenticadoForAlteradoDeveFalharNaAutenticacao()
    {
        return ValidarAlteracaoDeContextoAsync("valor");
    }

    [MySqlIntegrationFact]
    public Task ObterPorIdAsync_QuandoTipoChavePixAutenticadoForAlteradoDeveFalharNaAutenticacao()
    {
        return ValidarAlteracaoDeContextoAsync("tipo_chave_pix");
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

    private async Task ValidarAlteracaoDeContextoAsync(string coluna)
    {
        await fixture.LimparDadosAsync();
        var (repository, pagamentoPix) = await CriarPagamentoPixAsync();
        var (_, pagamentoAlternativo) = await CriarPagamentoPixAsync();
        await repository.AdicionarAsync(pagamentoPix, CancellationToken.None);

        await using (var connection = fixture.ConnectionFactory.Create())
        {
            await connection.OpenAsync();
            await using var command = new MySqlCommand(CriarSqlAlteracaoDeContexto(coluna), connection);
            command.Parameters.Add("@id", MySqlDbType.VarChar).Value = pagamentoPix.Id.ToString();

            switch (coluna)
            {
                case "cashback_id":
                    command.Parameters.Add("@valor", MySqlDbType.VarChar).Value = pagamentoAlternativo.CashbackId.ToString();
                    break;
                case "usuario_beneficiario_id":
                    command.Parameters.Add("@valor", MySqlDbType.VarChar).Value = pagamentoAlternativo.UsuarioBeneficiarioId.ToString();
                    break;
                case "valor":
                    command.Parameters.Add("@valor", MySqlDbType.Decimal).Value = pagamentoPix.Valor + 1m;
                    break;
                case "tipo_chave_pix":
                    command.Parameters.Add("@valor", MySqlDbType.Int32).Value = (int)TipoChavePix.Cpf;
                    break;
            }

            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            repository.ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None));
        Assert.DoesNotContain(pagamentoPix.ChavePix, exception.Message, StringComparison.Ordinal);
    }

    private static string CriarSqlAlteracaoDeContexto(string coluna) => coluna switch
    {
        "cashback_id" => "UPDATE pagamentos_pix SET cashback_id = @valor WHERE id = @id;",
        "usuario_beneficiario_id" => "UPDATE pagamentos_pix SET usuario_beneficiario_id = @valor WHERE id = @id;",
        "valor" => "UPDATE pagamentos_pix SET valor = @valor WHERE id = @id;",
        "tipo_chave_pix" => "UPDATE pagamentos_pix SET tipo_chave_pix = @valor WHERE id = @id;",
        _ => throw new ArgumentOutOfRangeException(nameof(coluna))
    };

    private async Task AtualizarMaterialCriptograficoAsync(Guid id, RegistroPersistido material)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            """
            UPDATE pagamentos_pix
            SET
                chave_pix_ciphertext = @ciphertext,
                chave_pix_nonce = @nonce,
                chave_pix_tag = @tag,
                encryption_version = @encryptionVersion
            WHERE id = @id;
            """,
            connection);
        command.Parameters.Add("@ciphertext", MySqlDbType.Blob).Value = material.Ciphertext;
        command.Parameters.Add("@nonce", MySqlDbType.VarBinary).Value = material.Nonce;
        command.Parameters.Add("@tag", MySqlDbType.VarBinary).Value = material.Tag;
        command.Parameters.Add("@encryptionVersion", MySqlDbType.Int32).Value = material.EncryptionVersion;
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = id.ToString();
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ObterStatusCashbackAsync(Guid cashbackId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT status FROM cashbacks WHERE id = @id;",
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = cashbackId.ToString();

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static void AssertSnapshotsEProtecaoPreservados(
        RegistroPersistido antes,
        RegistroPersistido depois)
    {
        Assert.Equal(antes.CashbackId, depois.CashbackId);
        Assert.Equal(antes.UsuarioBeneficiarioId, depois.UsuarioBeneficiarioId);
        Assert.Equal(antes.Valor, depois.Valor);
        Assert.Equal(antes.TipoChavePix, depois.TipoChavePix);
        Assert.Equal(antes.Ciphertext, depois.Ciphertext);
        Assert.Equal(antes.Nonce, depois.Nonce);
        Assert.Equal(antes.Tag, depois.Tag);
        Assert.Equal(antes.EncryptionVersion, depois.EncryptionVersion);
        Assert.Equal(antes.CreatedAt, depois.CreatedAt, TimeSpan.FromMilliseconds(1));
    }

    private async Task<(PagamentoPixMySqlRepository Repository, PagamentoPix PagamentoPix)> CriarPagamentoPixAsync(
        string chavePix = "snapshot.pagamento@exemplo.com")
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
                chavePix));
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
            ObterGuid(reader, 0, "cashback_id"),
            ObterGuid(reader, 1, "usuario_beneficiario_id"),
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

    private static Guid ObterGuid(MySqlDataReader reader, int ordinal, string nomeColuna)
    {
        return reader.GetValue(ordinal) switch
        {
            Guid guid when guid != Guid.Empty => guid,
            string texto when Guid.TryParse(texto, out var guid) && guid != Guid.Empty => guid,
            _ => throw new DataException($"O GUID persistido na coluna '{nomeColuna}' é inválido.")
        };
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
        Guid CashbackId,
        Guid UsuarioBeneficiarioId,
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
