using Application.DTOs.Usuario;
using Application.Interfaces.Security;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services
{
    public sealed class UsuarioService(IUsuarioRepository usuarioRepository, IPasswordHasher passwordHasher) : IUsuarioService
    {
        public async Task<UsuarioResponseDto> CriarAsync(CreateUsuarioDto dto, CancellationToken cancellationToken = default)
        {
            if (await usuarioRepository.ExistePorEmailAsync(dto.Email, cancellationToken: cancellationToken))
                throw new UsuarioJaExisteException();

            var senhaHash = passwordHasher.HashPassword(dto.Senha);
            var usuario = new Usuario(dto.Nome, dto.Email, senhaHash, dto.Telefone, dto.TipoUsuario);

            await usuarioRepository.AdicionarAsync(usuario, cancellationToken);
            return usuario.ToResponseDto();
        }

        public async Task<UsuarioResponseDto> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            (await ObterUsuarioOuLancarExceptionAsync(id, cancellationToken)).ToResponseDto();

        public async Task<IReadOnlyCollection<UsuarioResponseDto>> ObterTodosAsync(CancellationToken cancellationToken = default) =>
            (await usuarioRepository.ObterTodosAsync(cancellationToken)).ToResponseDto();

        public async Task AtualizarAsync(UpdateUsuarioDto dto, CancellationToken cancellationToken = default)
        {
            var usuario = await ObterUsuarioOuLancarExceptionAsync(dto.Id, cancellationToken);

            if (await usuarioRepository.ExistePorEmailAsync(dto.Email, usuario.Id, cancellationToken))
                throw new UsuarioJaExisteException();

            usuario.AlterarNome(dto.Nome);
            usuario.AlterarEmail(dto.Email);
            usuario.AlterarTelefone(dto.Telefone);

            await usuarioRepository.AtualizarAsync(usuario, cancellationToken);
        }

        public async Task AlterarSenhaAsync(AlterarSenhaUsuarioDto dto, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(dto.NovaSenha, dto.ConfirmarSenha, StringComparison.Ordinal))
                throw new SenhaNaoConfereException();

            var usuario = await ObterUsuarioOuLancarExceptionAsync(dto.UsuarioId, cancellationToken);
            if (!passwordHasher.VerifyPassword(dto.SenhaAtual, usuario.SenhaHash))
                throw new SenhaAtualIncorretaException();

            usuario.AlterarSenha(passwordHasher.HashPassword(dto.NovaSenha));
            await usuarioRepository.AtualizarAsync(usuario, cancellationToken);
        }

        public async Task RemoverAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var usuario = await ObterUsuarioOuLancarExceptionAsync(id, cancellationToken);
            await usuarioRepository.RemoverAsync(usuario, cancellationToken);
        }

        private async Task<Usuario> ObterUsuarioOuLancarExceptionAsync(Guid id, CancellationToken cancellationToken)
        {
            var usuario = await usuarioRepository.ObterPorIdAsync(id, cancellationToken);
            return usuario ?? throw new UsuarioNaoEncontradoException();
        }
    }
}
