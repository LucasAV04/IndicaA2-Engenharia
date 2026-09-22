using Application.DTOs.PagamentoPix;
using Application.Interfaces.Stores;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class PagamentoPixLeituraAdministrativaMySqlStore(MySqlConnectionFactory connectionFactory)
    : IPagamentoPixLeituraAdministrativaStore
{
    public async Task<IReadOnlyCollection<PagamentoPixResponseDto>> ObterTodosAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, cashback_id, usuario_beneficiario_id, valor, tipo_chave_pix,
                   status, quantidade_tentativas, created_at, updated_at
            FROM pagamentos_pix
            ORDER BY created_at DESC, id DESC;
            """;
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pagamentos = new List<PagamentoPixResponseDto>();
        while (await reader.ReadAsync(cancellationToken))
            pagamentos.Add(new PagamentoPixResponseDto
            {
                Id = reader.ObterGuid("id"),
                CashbackId = reader.ObterGuid("cashback_id"),
                UsuarioBeneficiarioId = reader.ObterGuid("usuario_beneficiario_id"),
                Valor = reader.GetDecimal("valor"),
                TipoChavePix = (TipoChavePix)reader.GetInt32("tipo_chave_pix"),
                Status = (StatusPagamentoPix)reader.GetInt32("status"),
                QuantidadeTentativas = reader.GetInt32("quantidade_tentativas"),
                CreatedAt = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
                UpdatedAt = DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
            });
        return pagamentos.AsReadOnly();
    }
}
