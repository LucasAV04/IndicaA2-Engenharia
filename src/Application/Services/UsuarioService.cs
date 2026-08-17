using Application.DTOs.Usuario;
using Application.Interfaces.Security;
using Application.Interfaces.Services;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Exceptions.Senha;
using Domain.Exceptions.Usuario;
using Domain.Interfaces;

namespace IndicA2.Application.Services;

public sealed class UsuarioService : IUsuarioService
{
    private const int MaximoTentativasGeracaoCodigoIndicacao = 5;

    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICodigoIndicacaoGenerator _codigoIndicacaoGenerator;

    public UsuarioService(
        IUsuarioRepository usuarioRepository,
        IPasswordHasher passwordHasher,
        ICodigoIndicacaoGenerator codigoIndicacaoGenerator)
    {
        _usuarioRepository = usuarioRepository;
        _passwordHasher = passwordHasher;
        _codigoIndicacaoGenerator = codigoIndicacaoGenerator;
    }

    #region Consultas

    public async Task<UsuarioResponseDto> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        (await ObterUsuarioOuLancarExceptionAsync(id, cancellationToken)).ToResponseDto();

    public async Task<UsuarioResponseDto> ObterPorCodigoIndicacaoAsync(
        string codigoIndicacao,
        CancellationToken cancellationToken = default)
    {
        var codigoNormalizado = Usuario.NormalizarCodigoIndicacao(codigoIndicacao);
        var usuario = await _usuarioRepository.ObterPorCodigoIndicacaoAsync(codigoNormalizado, cancellationToken);

        return (usuario ?? throw new UsuarioNaoEncontradoException()).ToResponseDto();
    }

    public async Task<IReadOnlyCollection<UsuarioResponseDto>> ObterTodosAsync(
        CancellationToken cancellationToken = default) =>
        (await _usuarioRepository.ObterTodosAsync(cancellationToken)).ToResponseDto();

    #endregion

    #region Comandos

    public async Task<UsuarioResponseDto> CriarAsync(CreateUsuarioDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var emailNormalizado = dto.Email
            .Trim()
            .ToLowerInvariant();

        if (await _usuarioRepository.ExistePorEmailAsync(
                emailNormalizado,
                cancellationToken: cancellationToken))
        {
            throw new UsuarioJaExisteException();
        }

        var senhaHash = _passwordHasher.HashPassword(dto.Senha);
        var codigoIndicacao = await GerarCodigoIndicacaoDisponivelAsync(cancellationToken);

        var usuario = new Usuario(
            dto.Nome,
            emailNormalizado,
            senhaHash,
            dto.Telefone,
            TipoUsuario.Usuario,
            codigoIndicacao);

        await _usuarioRepository.AdicionarAsync(
            usuario,
            cancellationToken);

        return usuario.ToResponseDto();
    }

    public async Task AtualizarAsync(UpdateUsuarioDto dto, CancellationToken cancellationToken = default)
    {
        var emailNormalizado = dto.Email.Trim().ToLowerInvariant();
        var usuario = await ObterUsuarioOuLancarExceptionAsync(dto.Id, cancellationToken);

        if (await _usuarioRepository.ExistePorEmailAsync(
                emailNormalizado,
                ignorarUsuarioId: usuario.Id,
                cancellationToken: cancellationToken))
            throw new UsuarioJaExisteException();

        usuario.AlterarNome(dto.Nome);
        usuario.AlterarEmail(dto.Email);
        usuario.AlterarTelefone(dto.Telefone);

        await _usuarioRepository.AtualizarAsync(usuario, cancellationToken);
    }

    public async Task AlterarSenhaAsync(AlterarSenhaUsuarioDto dto, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(dto.NovaSenha, dto.ConfirmarSenha, StringComparison.Ordinal))
            throw new SenhaNaoConfereException();

        var usuario = await ObterUsuarioOuLancarExceptionAsync(dto.UsuarioId, cancellationToken);
        if (!_passwordHasher.VerifyPassword(dto.SenhaAtual, usuario.SenhaHash))
            throw new SenhaAtualIncorretaException();

        usuario.AlterarSenha(_passwordHasher.HashPassword(dto.NovaSenha));
        await _usuarioRepository.AtualizarAsync(usuario, cancellationToken);
    }

    #endregion

    #region Métodos Privados

    private async Task<Usuario> ObterUsuarioOuLancarExceptionAsync(Guid id, CancellationToken cancellationToken)
    {
        var usuario = await _usuarioRepository.ObterPorIdAsync(id, cancellationToken);
        return usuario ?? throw new UsuarioNaoEncontradoException();
    }

    private async Task<string> GerarCodigoIndicacaoDisponivelAsync(CancellationToken cancellationToken)
    {
        for (var tentativa = 0; tentativa < MaximoTentativasGeracaoCodigoIndicacao; tentativa++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var codigoIndicacao = Usuario.NormalizarCodigoIndicacao(_codigoIndicacaoGenerator.Gerar());
            var usuarioExistente = await _usuarioRepository.ObterPorCodigoIndicacaoAsync(codigoIndicacao, cancellationToken);

            if (usuarioExistente is null)
                return codigoIndicacao;
        }

        throw new InvalidOperationException(
            "Não foi possível gerar um código de indicação único após cinco tentativas.");
    }

    #endregion
}
