using Application.DTOs.Auth;
using Application.Interfaces.Security;
using Application.Interfaces.Services;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services;

public sealed class AuthService(IUsuarioRepository usuarioRepository, IPasswordHasher passwordHasher, IAccessTokenGenerator accessTokenGenerator) : IAuthService
{
    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Senha)) throw new CredenciaisInvalidasException();
        var usuario = await usuarioRepository.ObterPorEmailAsync(dto.Email.Trim().ToLowerInvariant(), cancellationToken);
        if (usuario is null || !passwordHasher.VerifyPassword(dto.Senha, usuario.SenhaHash)) throw new CredenciaisInvalidasException();
        if (usuario.Status is not StatusUsuario.Ativo) throw new UsuarioSemAcessoException();
        var token = accessTokenGenerator.Generate(usuario);
        usuario.RegistrarLogin();
        await usuarioRepository.AtualizarAsync(usuario, cancellationToken);
        return new LoginResponseDto { AccessToken = token.Token, ExpiresAtUtc = token.ExpiresAtUtc, UsuarioId = usuario.Id, Nome = usuario.Nome, Email = usuario.Email, TipoUsuario = usuario.TipoUsuario };
    }
}
