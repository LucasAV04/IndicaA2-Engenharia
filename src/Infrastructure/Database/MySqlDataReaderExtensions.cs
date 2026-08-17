using System.Data;
using MySqlConnector;

namespace Infrastructure.Database;

internal static class MySqlDataReaderExtensions
{
    public static Guid ObterGuid(this MySqlDataReader reader, string nomeColuna)
    {
        var ordinal = reader.GetOrdinal(nomeColuna);
        if (reader.IsDBNull(ordinal))
            throw new DataException($"O GUID persistido na coluna '{nomeColuna}' e obrigatorio.");

        return ConverterGuid(reader.GetValue(ordinal), nomeColuna);
    }

    public static Guid? ObterGuidOpcional(this MySqlDataReader reader, string nomeColuna)
    {
        var ordinal = reader.GetOrdinal(nomeColuna);
        return reader.IsDBNull(ordinal)
            ? null
            : ConverterGuid(reader.GetValue(ordinal), nomeColuna);
    }

    private static Guid ConverterGuid(object valor, string nomeColuna)
    {
        var guid = valor switch
        {
            Guid valorGuid => valorGuid,
            string texto when Guid.TryParse(texto, out var valorGuid) => valorGuid,
            _ => throw new DataException($"O GUID persistido na coluna '{nomeColuna}' e invalido.")
        };

        return guid != Guid.Empty
            ? guid
            : throw new DataException($"O GUID persistido na coluna '{nomeColuna}' e invalido.");
    }
}
