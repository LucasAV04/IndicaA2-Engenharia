using Application.DTOs.Indicacao;
using Application.Interfaces.Services;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Exceptions.Usuario;
using Domain.Interfaces;

namespace Application.Services
{
    public sealed class IndicacaoService : IIndicacaoService
    {
        private readonly IIndicacaoRepository _indicacaoRepository;
        private readonly IUsuarioRepository _usuarioRepository;
        private readonly IVistoriaRepository _vistoriaRepository;

        public IndicacaoService(
            IIndicacaoRepository indicacaoRepository,
            IUsuarioRepository usuarioRepository,
            IVistoriaRepository vistoriaRepository)
        {
            _indicacaoRepository = indicacaoRepository;
            _usuarioRepository = usuarioRepository;
            _vistoriaRepository = vistoriaRepository;
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

        public async Task<IndicacaoResponseDto> CriarPorCodigoAsync(
            CreateIndicacaoPorCodigoDto dto,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var codigoIndicacao = Usuario.NormalizarCodigoIndicacao(dto.CodigoIndicacao);
            var usuarioIndicador = await _usuarioRepository.ObterPorCodigoIndicacaoAsync(
                codigoIndicacao,
                cancellationToken);

            if (usuarioIndicador is null)
                throw new CodigoIndicacaoNaoEncontradoException();

            if (usuarioIndicador.TipoUsuario != TipoUsuario.Usuario ||
                usuarioIndicador.CodigoIndicacao is null ||
                !string.Equals(
                    Usuario.NormalizarCodigoIndicacao(usuarioIndicador.CodigoIndicacao),
                    codigoIndicacao,
                    StringComparison.Ordinal))
            {
                throw new DomainException(
                    "O código de indicação encontrado não pertence a um usuário comum válido.");
            }

            var indicacao = new Indicacao(
                usuarioIndicador.Id,
                dto.NomeIndicada,
                dto.TelefoneIndicada,
                codigoIndicacao);

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

            var vistoria = await ObterVistoriaOuLancarExceptionAsync(
                dto.VistoriaId,
                cancellationToken);

            if (indicacao.UsuarioIndicadoId is null)
            {
                throw new DomainException(
                    "A indicação deve possuir um usuário indicado vinculado antes de associar uma vistoria.");
            }

            if (vistoria.UsuarioId != indicacao.UsuarioIndicadoId.Value)
                throw new DomainException("A vistoria pertence a um usuário diferente do usuário indicado.");

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

            if (indicacao.VistoriaId is not Guid vistoriaId)
            {
                indicacao.MarcarVistoriaConcluida();
                return;
            }

            var vistoria = await ObterVistoriaOuLancarExceptionAsync(vistoriaId, cancellationToken);
            if (vistoria.Status != StatusVistoria.Concluida)
                throw new DomainException("A vistoria vinculada ainda não foi concluída.");

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

        private async Task<Vistoria> ObterVistoriaOuLancarExceptionAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            var vistoria = await _vistoriaRepository.ObterPorIdAsync(id, cancellationToken);
            return vistoria ?? throw new VistoriaNaoEncontradaException();
        }

        #endregion
    }
}
