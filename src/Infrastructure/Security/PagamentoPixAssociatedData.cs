using System.Globalization;
using System.Text;
using Domain.Enums;

namespace Infrastructure.Security;

internal static class PagamentoPixAssociatedData
{
    public const string ContextIdentifier = "PagamentoPix:v1";

    public static byte[] Criar(
        Guid id,
        Guid cashbackId,
        Guid usuarioBeneficiarioId,
        decimal valor,
        TipoChavePix tipoChavePix)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(ContextIdentifier);
        writer.Write(id.ToString("D"));
        writer.Write(cashbackId.ToString("D"));
        writer.Write(usuarioBeneficiarioId.ToString("D"));
        writer.Write(valor.ToString("F2", CultureInfo.InvariantCulture));
        writer.Write((int)tipoChavePix);
        writer.Flush();

        return stream.ToArray();
    }
}
