using Application.DTOs.Indicacao;
using Application.Interfaces.Services;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services
{
    public sealed class IndicacaoService : IIndicacaoService
    {
        private readonly IIndicacaoRepository _indicacaoRepository;
        private readonly IUsuarioRepository _usuarioRepository;

        public IndicacaoService(
            IIndicacaoRepository indicacaoRepository,
            IUsuarioRepository usuarioRepository)
        {
            _indicacaoRepository = indicacaoRepository;
            _usuarioRepository = usuarioRepository;
        }

        #region Consultas

        public async Task<IndicacaoResponseDto> ObterPorIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            (await ObterIndicacaoOuLancarExceptionAsync(id, cancellationToken)).ToResponseDto();

        public async Task<IReadOnlyCollection<IndicacaoResponseDto>> ObterTodasAsync(
            CancellationToken cancellationToken = default) =>
            (await _indicacaoRepository.ObterTodasAsync(cancellationToken)).ToResponseDto();

        public async Task<IReadOnlyCollection<IndicacaoResponseDto>> ObterPorUsuarioIndicadorIdAsync(
            Guid usuarioIndicadorId,
            CancellationToken cancellationToken = default) =>
            (await _indicacaoRepository
                .ObterPorUsuarioIndicadorIdAsync(usuarioIndicadorId, cancellationToken))
            .ToResponseDto();

        public async Task<IReadOnlyCollection<IndicacaoResponseDto>> ObterPorStatusAsync(
            StatusIndicacao status,
            CancellationToken cancellationToken = default) =>
            (await _indicacaoRepository.ObterPorStatusAsync(status, cancellationToken)).ToResponseDto();

        #endregion

        #region Comandos

        public async Task<IndicacaoResponseDto> CriarAsync(
            CreateIndicacaoDto dto,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            await ObterUsuarioOuLancarExceptionAsync(dto.UsuarioIndicadorId, cancellationToken);

            var indicacao = new Indicacao(
                dto.UsuarioIndicadorId,
                dto.NomeIndicada,
                dto.TelefoneIndicada,
                dto.CodigoIndicacaoUsado);

            await _indicacaoRepository.AdicionarAsync(indicacao, cancellationToken);

            return indicacao.ToResponseDto();
        }

        public async Task VincularUsuarioIndicadoAsync(
            VincularUsuarioIndicadoDto dto,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var indicacao = await ObterIndicacaoOuLancarExceptionAsync(
                dto.IndicacaoId,
                cancellationToken);

            await ObterUsuarioOuLancarExceptionAsync(dto.UsuarioIndicadoId, cancellationToken);

            if (indicacao.UsuarioIndicadorId == dto.UsuarioIndicadoId)
                throw new DomainException("Um usuário não pode indicar a si mesmo.");

            indicacao.VincularUsuarioIndicado(dto.UsuarioIndicadoId);

            await _indicacaoRepository.AtualizarAsync(indicacao, cancellationToken);
        }

        public async Task VincularVistoriaAsync(
            VincularVistoriaDto dto,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var indicacao = await ObterIndicacaoOuLancarExceptionAsync(
                dto.IndicacaoId,
                cancellationToken);

            indicacao.VincularVistoria(dto.VistoriaId);

            await _indicacaoRepository.AtualizarAsync(indicacao, cancellationToken);
        }

        public async Task MarcarVistoriaConcluidaAsync(
            Guid indicacaoId,
            CancellationToken cancellationToken = default)
        {
            var indicacao = await ObterIndicacaoOuLancarExceptionAsync(
                indicacaoId,
                cancellationToken);

            indicacao.MarcarVistoriaConcluida();

            await _indicacaoRepository.AtualizarAsync(indicacao, cancellationToken);
        }

        public async Task CancelarAsync(
            Guid indicacaoId,
            CancellationToken cancellationToken = default)
        {
            var indicacao = await ObterIndicacaoOuLancarExceptionAsync(
                indicacaoId,
                cancellationToken);

            if (indicacao.Status is StatusIndicacao.Cancelada)
                return;

            indicacao.Cancelar();

            await _indicacaoRepository.AtualizarAsync(indicacao, cancellationToken);
        }

        #endregion

        #region Métodos Privados

        private async Task<Indicacao> ObterIndicacaoOuLancarExceptionAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            var indicacao = await _indicacaoRepository.ObterPorIdAsync(id, cancellationToken);
            return indicacao ?? throw new IndicacaoNaoEncontradaException();
        }

        private async Task<Usuario> ObterUsuarioOuLancarExceptionAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            var usuario = await _usuarioRepository.ObterPorIdAsync(id, cancellationToken);
            return usuario ?? throw new UsuarioNaoEncontradoException();
        }

        #endregion
    }
}
