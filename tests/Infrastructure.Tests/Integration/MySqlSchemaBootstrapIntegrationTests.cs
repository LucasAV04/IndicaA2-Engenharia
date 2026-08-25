using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MySqlSchemaBootstrapIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task ScriptsReais_DevemCriarSchemaCompletoEmBancoNovo()
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        var tabelas = new List<string>();

        {
            await using var command = new MySqlCommand(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE();",
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                tabelas.Add(reader.GetString(0));
        }

        Assert.Contains("usuarios", tabelas);
        Assert.Contains("vistorias", tabelas);
        Assert.Contains("indicacoes", tabelas);
        Assert.Contains("pagamentos_vistoria", tabelas);
        Assert.Contains("cashbacks", tabelas);
        Assert.Contains("dados_pix", tabelas);
        Assert.Contains("pagamentos_pix", tabelas);

        var constraints = new List<string>();

        {
            await using var constraintCommand = new MySqlCommand(
                """
                SELECT constraint_name
                FROM information_schema.table_constraints
                WHERE table_schema = DATABASE()
                  AND table_name = 'indicacoes'
                  AND constraint_type = 'UNIQUE';
                """,
                connection);
            await using var constraintReader = await constraintCommand.ExecuteReaderAsync();

            while (await constraintReader.ReadAsync())
                constraints.Add(constraintReader.GetString(0));
        }

        Assert.Contains("uq_indicacoes_vistoria_id", constraints);

        var constraintsCashbacks = new List<string>();

        {
            await using var constraintCommand = new MySqlCommand(
                """
                SELECT constraint_name
                FROM information_schema.table_constraints
                WHERE table_schema = DATABASE()
                  AND table_name = 'cashbacks'
                  AND constraint_type = 'UNIQUE';
                """,
                connection);
            await using var constraintReader = await constraintCommand.ExecuteReaderAsync();

            while (await constraintReader.ReadAsync())
                constraintsCashbacks.Add(constraintReader.GetString(0));
        }

        Assert.Contains("uq_cashbacks_pagamento_vistoria_id", constraintsCashbacks);

        var foreignKeysCashbacks = new List<string>();

        {
            await using var foreignKeyCommand = new MySqlCommand(
                """
                SELECT constraint_name
                FROM information_schema.table_constraints
                WHERE table_schema = DATABASE()
                  AND table_name = 'cashbacks'
                  AND constraint_type = 'FOREIGN KEY';
                """,
                connection);
            await using var foreignKeyReader = await foreignKeyCommand.ExecuteReaderAsync();

            while (await foreignKeyReader.ReadAsync())
                foreignKeysCashbacks.Add(foreignKeyReader.GetString(0));
        }

        Assert.Contains("fk_cashbacks_indicacoes", foreignKeysCashbacks);
        Assert.Contains("fk_cashbacks_pagamentos_vistoria", foreignKeysCashbacks);
        Assert.Contains("fk_cashbacks_usuarios_indicadores", foreignKeysCashbacks);

        var constraintsDadosPix = new List<string>();
        var foreignKeysDadosPix = new List<string>();

        {
            await using var constraintCommand = new MySqlCommand(
                """
                SELECT constraint_name
                FROM information_schema.table_constraints
                WHERE table_schema = DATABASE()
                  AND table_name = 'dados_pix'
                  AND constraint_type = 'UNIQUE';
                """,
                connection);
            await using var constraintReader = await constraintCommand.ExecuteReaderAsync();

            while (await constraintReader.ReadAsync())
                constraintsDadosPix.Add(constraintReader.GetString(0));
        }

        {
            await using var foreignKeyCommand = new MySqlCommand(
                """
                SELECT constraint_name
                FROM information_schema.table_constraints
                WHERE table_schema = DATABASE()
                  AND table_name = 'dados_pix'
                  AND constraint_type = 'FOREIGN KEY';
                """,
                connection);
            await using var foreignKeyReader = await foreignKeyCommand.ExecuteReaderAsync();

            while (await foreignKeyReader.ReadAsync())
                foreignKeysDadosPix.Add(foreignKeyReader.GetString(0));
        }

        Assert.Contains("uq_dados_pix_usuario_id", constraintsDadosPix);
        Assert.Contains("fk_dados_pix_usuarios", foreignKeysDadosPix);

        var constraintsPagamentosPix = new List<string>();
        var foreignKeysPagamentosPix = new List<string>();

        {
            await using var constraintCommand = new MySqlCommand(
                """
                SELECT constraint_name
                FROM information_schema.table_constraints
                WHERE table_schema = DATABASE()
                  AND table_name = 'pagamentos_pix'
                  AND constraint_type = 'UNIQUE';
                """,
                connection);
            await using var constraintReader = await constraintCommand.ExecuteReaderAsync();

            while (await constraintReader.ReadAsync())
                constraintsPagamentosPix.Add(constraintReader.GetString(0));
        }

        {
            await using var foreignKeyCommand = new MySqlCommand(
                """
                SELECT constraint_name
                FROM information_schema.table_constraints
                WHERE table_schema = DATABASE()
                  AND table_name = 'pagamentos_pix'
                  AND constraint_type = 'FOREIGN KEY';
                """,
                connection);
            await using var foreignKeyReader = await foreignKeyCommand.ExecuteReaderAsync();

            while (await foreignKeyReader.ReadAsync())
                foreignKeysPagamentosPix.Add(foreignKeyReader.GetString(0));
        }

        Assert.Contains("uq_pagamentos_pix_cashback_id", constraintsPagamentosPix);
        Assert.Contains("fk_pagamentos_pix_cashbacks", foreignKeysPagamentosPix);
        Assert.Contains("fk_pagamentos_pix_usuarios_beneficiarios", foreignKeysPagamentosPix);

        var colunasPagamentosPix = new List<string>();
        var regrasExclusaoPagamentosPix = new List<string>();

        {
            await using var colunasCommand = new MySqlCommand(
                """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = DATABASE()
                  AND table_name = 'pagamentos_pix';
                """,
                connection);
            await using var colunasReader = await colunasCommand.ExecuteReaderAsync();

            while (await colunasReader.ReadAsync())
                colunasPagamentosPix.Add(colunasReader.GetString(0));
        }

        {
            await using var regrasExclusaoCommand = new MySqlCommand(
                """
                SELECT delete_rule
                FROM information_schema.referential_constraints
                WHERE constraint_schema = DATABASE()
                  AND table_name = 'pagamentos_pix';
                """,
                connection);
            await using var regrasExclusaoReader = await regrasExclusaoCommand.ExecuteReaderAsync();

            while (await regrasExclusaoReader.ReadAsync())
                regrasExclusaoPagamentosPix.Add(regrasExclusaoReader.GetString(0));
        }

        Assert.Contains("id", colunasPagamentosPix);
        Assert.Contains("cashback_id", colunasPagamentosPix);
        Assert.Contains("usuario_beneficiario_id", colunasPagamentosPix);
        Assert.Contains("valor", colunasPagamentosPix);
        Assert.Contains("tipo_chave_pix", colunasPagamentosPix);
        Assert.Contains("chave_pix_ciphertext", colunasPagamentosPix);
        Assert.Contains("chave_pix_nonce", colunasPagamentosPix);
        Assert.Contains("chave_pix_tag", colunasPagamentosPix);
        Assert.Contains("encryption_version", colunasPagamentosPix);
        Assert.Contains("status", colunasPagamentosPix);
        Assert.Contains("quantidade_tentativas", colunasPagamentosPix);
        Assert.Contains("created_at", colunasPagamentosPix);
        Assert.Contains("updated_at", colunasPagamentosPix);
        Assert.Equal(2, regrasExclusaoPagamentosPix.Count);
        // InnoDB expõe FKs sem ação de exclusão tanto como RESTRICT quanto como NO ACTION.
        // Ambas preservam a semântica restritiva exigida para registros financeiros.
        Assert.All(
            regrasExclusaoPagamentosPix,
            regra => Assert.True(
                regra is "RESTRICT" or "NO ACTION",
                $"A regra de exclusão '{regra}' não é restritiva."));
    }
}
