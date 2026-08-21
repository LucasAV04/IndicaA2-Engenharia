using Domain.Entities;
using Domain.Enums;

namespace Infrastructure.Tests.Integration;

internal static class IntegrationTestData
{
    public static Usuario CriarUsuario(
        string? email = null,
        TipoUsuario tipoUsuario = TipoUsuario.Usuario,
        string? codigoIndicacao = null)
    {
        var codigoParaCriacao = tipoUsuario == TipoUsuario.Usuario
            ? codigoIndicacao ?? Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()
            : null;

        return new Usuario(
            "Usuario de Integracao",
            email ?? $"usuario-{Guid.NewGuid():N}@exemplo.com",
            "hash-de-teste",
            "11999999999",
            tipoUsuario,
            codigoParaCriacao);
    }

    public static Vistoria CriarVistoria(Guid usuarioId, decimal areaM2 = 72.35m) =>
        new(
            usuarioId,
            "Apartamento",
            areaM2,
            PacoteVistoria.Total,
            new DateTime(2026, 9, 15, 14, 30, 0, DateTimeKind.Unspecified));

    public static PagamentoVistoria CriarPagamentoVistoria(Guid vistoriaId, decimal valor = 499.90m) =>
        new(vistoriaId, valor);
}
