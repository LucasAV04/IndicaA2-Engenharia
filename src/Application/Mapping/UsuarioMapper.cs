using Application.DTOs.Usuario;
using Domain.Entities;
namespace Application.Mapping
{
    public static class UsuarioMApper
    {
        public static UsuarioResponseDto ToResponseDto(this Usuario usuario)
        {
            return new UsuarioResponseDto
            {
                Id = usuario.Id,
                Nome = usuario.Nome,
                Email = usuario.Email,
                Telefone = usuario.Telefone,
                Status = usuario.Status,
                TipoUsuario = usuario.TipoUsuario,
                EmailConfirmado = usuario.EmailConfirmado,
                CreatedAt = usuario.CreatedAt,
                UpdatedAt = usuario.UpdatedAt
            };
        }

        public static IReadOnlyCollection<UsuarioResponseDto> ToResponseDto(
            this IEnumerable<Usuario> usuarios)
        {
            return usuarios
                .Select(usuario => usuario.ToResponseDto())
                .ToList()
                .AsReadOnly();
        }
    }
}
