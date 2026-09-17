using System.Globalization;
using System.Text.Json;
using Application.Interfaces.Providers;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Infrastructure.Security;
using MySqlConnector;

namespace Infrastructure.Tests.Integration;

// Dados exclusivamente fictícios; utiliza a factory do banco descartável da fixture.
internal sealed class ProcessamentoPixCenario(MySqlIntegrationFixture fixture) : IDisposable
{
    private readonly AesGcmDadosPixProtector _protector = new(
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(n => (byte)n).ToArray()));
    public PagamentoPixMySqlRepository Pagamentos => new(fixture.ConnectionFactory, _protector);
    public CashbackMySqlRepository Cashbacks => new(fixture.ConnectionFactory);
    public OperacaoPagamentoPixMySqlRepository Operacoes => new(fixture.ConnectionFactory);
    public PagamentoPixEnvioMySqlStore Envios => new(fixture.ConnectionFactory);
    public PagamentoPixCandidatoProcessamentoMySqlStore Seletor => new(fixture.ConnectionFactory);

    public PagamentoPixProcessamentoService Processador(IPixProvider provider, InterceptadorTransacionalPix? interceptarAplicacao = null) => new(
        Pagamentos, new PagamentoPixEnvioService(Pagamentos, Envios, provider),
        new PagamentoPixReconciliacaoService(Pagamentos, Operacoes,
            new PagamentoPixReconciliacaoMySqlStore(fixture.ConnectionFactory), provider),
        new PagamentoPixAplicacaoResultadoService(Pagamentos, Cashbacks,
            new PagamentoPixAplicacaoResultadoMySqlStore(fixture.ConnectionFactory, _protector, interceptarAplicacao ?? TransacaoPixSemIntercepcao.ExecutarAsync)));

    public async Task<PagamentoPix> CriarAsync(StatusPagamentoPix status = StatusPagamentoPix.Pendente, int tentativas = 0)
    {
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var indicador = IntegrationTestData.CriarUsuario();
        var indicada = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(indicador, default);
        await usuarios.AdicionarAsync(indicada, default);
        var vistoria = IntegrationTestData.CriarVistoria(indicada.Id);
        await new VistoriaMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(vistoria, default);
        var indicacao = new Indicacao(indicador.Id, "Indicada ficticia", "11999999999", indicador.CodigoIndicacao!);
        indicacao.VincularVistoria(vistoria.Id);
        await new IndicacaoMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(indicacao, default);
        var pagamento = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id);
        pagamento.Confirmar();
        await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(pagamento, default);
        var cashback = Cashback.Criar(indicacao.Id, pagamento.Id, indicador.Id, pagamento.Valor);
        cashback.Aprovar();
        await Cashbacks.AdicionarAsync(cashback, default);
        var pix = PagamentoPix.Criar(cashback.Id, indicador.Id, cashback.Valor, TipoChavePix.Email, "ficticio@example.invalid");
        if (status != StatusPagamentoPix.Pendente)
            pix = PagamentoPix.Reidratar(pix.Id, pix.CashbackId, pix.UsuarioBeneficiarioId, pix.Valor,
                pix.TipoChavePix, pix.ChavePix, status, tentativas, pix.CreatedAt, pix.UpdatedAt);
        await Pagamentos.AdicionarAsync(pix, default);
        return pix;
    }

    public async Task ExecutarAsync(string sql, params (string Nome, object Valor)[] parametros)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (nome, valor) in parametros) command.Parameters.AddWithValue(nome, valor);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string> SnapshotAsync(string sql, params (string Nome, object Valor)[] parametros)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (nome, valor) in parametros) command.Parameters.AddWithValue(nome, valor);
        await using var reader = await command.ExecuteReaderAsync();
        var linhas = new List<string[]>();
        while (await reader.ReadAsync())
        {
            var campos = new string[reader.FieldCount];
            for (var i = 0; i < campos.Length; i++)
                campos[i] = reader.GetValue(i) switch
                {
                    DBNull => "<NULL>",
                    byte[] bytes => Convert.ToHexString(bytes),
                    DateTime data => data.ToString("O", CultureInfo.InvariantCulture),
                    IFormattable valor => valor.ToString(null, CultureInfo.InvariantCulture),
                    object valor => valor.ToString()!
                };
            linhas.Add(campos);
        }
        return JsonSerializer.Serialize(linhas);
    }

    public async Task<string> SnapshotIntegralAsync() => string.Join("\n",
        await SnapshotAsync("SELECT * FROM pagamentos_pix ORDER BY id"),
        await SnapshotAsync("SELECT * FROM cashbacks ORDER BY id"),
        await SnapshotAsync("SELECT * FROM operacoes_pagamento_pix ORDER BY id"));

    public Task<string> SnapshotImutavelAsync(Guid id) => SnapshotAsync("""
        SELECT id, cashback_id, usuario_beneficiario_id, valor, tipo_chave_pix,
               chave_pix_ciphertext, chave_pix_nonce, chave_pix_tag, encryption_version, created_at
        FROM pagamentos_pix WHERE id = @id
        """, ("@id", id.ToString()));

    public Task ExpirarEnvioAsync(Guid id) => ExecutarAsync("""
        UPDATE pagamentos_pix SET envio_lease_expira_em = DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 1 SECOND)
        WHERE id = @id
        """, ("@id", id.ToString()));

    public async Task<Guid?> TokenEnvioAsync(Guid id)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("SELECT envio_lease_id FROM pagamentos_pix WHERE id = @id", connection);
        command.Parameters.AddWithValue("@id", id.ToString());
        var token = await command.ExecuteScalarAsync();
        return token is null or DBNull ? null : token is Guid guid ? guid : Guid.Parse((string)token);
    }

    public void Dispose() => _protector.Dispose();
}
