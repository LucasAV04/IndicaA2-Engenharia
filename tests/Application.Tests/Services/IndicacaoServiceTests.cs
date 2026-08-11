using Application.DTOs.Indicacao;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Exceptions.Usuario;
using Domain.Interfaces;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class IndicacaoServiceTests
{
    [Fact]
    public async Task CriarAsync_QuandoIndicadorExiste_DeveConsultarPersistirERetornarDto()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var usuarioRepository = new Mock<IUsuarioRepository>();
        var indicadorId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        Indicacao? persistida = null;

        usuarioRepository
            .Setup(repository => repository.ObterPorIdAsync(indicadorId, cancellationToken))
            .ReturnsAsync(CriarUsuario());
        indicacaoRepository
            .Setup(repository => repository.AdicionarAsync(It.IsAny<Indicacao>(), cancellationToken))
            .Callback<Indicacao, CancellationToken>((indicacao, _) => persistida = indicacao)
            .Returns(Task.CompletedTask);

        var resultado = await CriarService(indicacaoRepository, usuarioRepository).CriarAsync(
            new CreateIndicacaoDto
            {
                UsuarioIndicadorId = indicadorId,
                NomeIndicada = "Ana Indicada",
                TelefoneIndicada = "11999999999",
                CodigoIndicacaoUsado = "a2-123"
            },
            cancellationToken);

        Assert.NotNull(persistida);
        Assert.Equal(indicadorId, resultado.UsuarioIndicadorId);
        Assert.Equal("A2-123", resultado.CodigoIndicacaoUsado);
        Assert.Equal(StatusIndicacao.Pendente, resultado.Status);
        usuarioRepository.Verify(repository => repository.ObterPorIdAsync(indicadorId, cancellationToken), Times.Once);
        indicacaoRepository.Verify(repository => repository.AdicionarAsync(It.IsAny<Indicacao>(), cancellationToken), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_QuandoIndicadorNaoExiste_DeveLancarExcecaoENaoPersistir()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var usuarioRepository = new Mock<IUsuarioRepository>();
        var indicadorId = Guid.NewGuid();
        usuarioRepository
            .Setup(repository => repository.ObterPorIdAsync(indicadorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<UsuarioNaoEncontradoException>(() =>
            CriarService(indicacaoRepository, usuarioRepository).CriarAsync(CriarDto(indicadorId)));

        indicacaoRepository.Verify(repository => repository.AdicionarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CriarAsync_QuandoDtoNulo_DeveLancarArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CriarService(new Mock<IIndicacaoRepository>(), new Mock<IUsuarioRepository>()).CriarAsync(null!));
    }

    [Fact]
    public async Task ObterPorIdAsync_QuandoIndicacaoExiste_DeveRetornarDtoEPropagarToken()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var id = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var indicacao = CriarIndicacao();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(id, cancellationToken))
            .ReturnsAsync(indicacao);

        var resultado = await CriarService(indicacaoRepository, new Mock<IUsuarioRepository>())
            .ObterPorIdAsync(id, cancellationToken);

        Assert.Equal(indicacao.Id, resultado.Id);
        Assert.Equal(indicacao.NomeIndicada, resultado.NomeIndicada);
        indicacaoRepository.Verify(repository => repository.ObterPorIdAsync(id, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ObterPorIdAsync_QuandoIndicacaoNaoExiste_DeveLancarExcecaoEspecifica()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Indicacao?)null);

        await Assert.ThrowsAsync<IndicacaoNaoEncontradaException>(() =>
            CriarService(indicacaoRepository, new Mock<IUsuarioRepository>()).ObterPorIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Consultas_QuandoRepositoriesRetornamColecoes_DeveMapearResultadosInclusiveVazio()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var indicadorId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var indicacoes = new[] { CriarIndicacao(indicadorId), CriarIndicacao(indicadorId) };
        indicacaoRepository
            .Setup(repository => repository.ObterTodasAsync(cancellationToken))
            .ReturnsAsync(Array.Empty<Indicacao>());
        indicacaoRepository
            .Setup(repository => repository.ObterPorUsuarioIndicadorIdAsync(indicadorId, cancellationToken))
            .ReturnsAsync(indicacoes);
        indicacaoRepository
            .Setup(repository => repository.ObterPorStatusAsync(StatusIndicacao.Pendente, cancellationToken))
            .ReturnsAsync(indicacoes);
        var service = CriarService(indicacaoRepository, new Mock<IUsuarioRepository>());

        var todas = await service.ObterTodasAsync(cancellationToken);
        var porIndicador = await service.ObterPorUsuarioIndicadorIdAsync(indicadorId, cancellationToken);
        var porStatus = await service.ObterPorStatusAsync(StatusIndicacao.Pendente, cancellationToken);

        Assert.Empty(todas);
        Assert.Equal(2, porIndicador.Count);
        Assert.All(porIndicador, dto => Assert.Equal(indicadorId, dto.UsuarioIndicadorId));
        Assert.Equal(2, porStatus.Count);
        Assert.All(porStatus, dto => Assert.Equal(StatusIndicacao.Pendente, dto.Status));
        indicacaoRepository.Verify(repository => repository.ObterTodasAsync(cancellationToken), Times.Once);
        indicacaoRepository.Verify(repository => repository.ObterPorUsuarioIndicadorIdAsync(indicadorId, cancellationToken), Times.Once);
        indicacaoRepository.Verify(repository => repository.ObterPorStatusAsync(StatusIndicacao.Pendente, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task VincularUsuarioIndicadoAsync_QuandoDadosExistem_DeveVincularEAtualizar()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var usuarioRepository = new Mock<IUsuarioRepository>();
        var indicacao = CriarIndicacao();
        var usuarioIndicadoId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, cancellationToken))
            .ReturnsAsync(indicacao);
        usuarioRepository
            .Setup(repository => repository.ObterPorIdAsync(usuarioIndicadoId, cancellationToken))
            .ReturnsAsync(CriarUsuario());
        indicacaoRepository
            .Setup(repository => repository.AtualizarAsync(indicacao, cancellationToken))
            .Returns(Task.CompletedTask);

        await CriarService(indicacaoRepository, usuarioRepository).VincularUsuarioIndicadoAsync(
            new VincularUsuarioIndicadoDto { IndicacaoId = indicacao.Id, UsuarioIndicadoId = usuarioIndicadoId },
            cancellationToken);

        Assert.Equal(usuarioIndicadoId, indicacao.UsuarioIndicadoId);
        indicacaoRepository.Verify(repository => repository.ObterPorIdAsync(indicacao.Id, cancellationToken), Times.Once);
        usuarioRepository.Verify(repository => repository.ObterPorIdAsync(usuarioIndicadoId, cancellationToken), Times.Once);
        indicacaoRepository.Verify(repository => repository.AtualizarAsync(indicacao, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task VincularUsuarioIndicadoAsync_QuandoIndicacaoOuUsuarioNaoExistem_NaoDeveAtualizar()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var usuarioRepository = new Mock<IUsuarioRepository>();
        var dto = new VincularUsuarioIndicadoDto { IndicacaoId = Guid.NewGuid(), UsuarioIndicadoId = Guid.NewGuid() };
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(dto.IndicacaoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Indicacao?)null);
        var service = CriarService(indicacaoRepository, usuarioRepository);

        await Assert.ThrowsAsync<IndicacaoNaoEncontradaException>(() => service.VincularUsuarioIndicadoAsync(dto));

        var indicacao = CriarIndicacao();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicacao);
        usuarioRepository
            .Setup(repository => repository.ObterPorIdAsync(dto.UsuarioIndicadoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<UsuarioNaoEncontradoException>(() => service.VincularUsuarioIndicadoAsync(
            new VincularUsuarioIndicadoDto { IndicacaoId = indicacao.Id, UsuarioIndicadoId = dto.UsuarioIndicadoId }));

        indicacaoRepository.Verify(repository => repository.AtualizarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VincularUsuarioIndicadoAsync_QuandoAutoIndicacaoOuSegundoVinculo_DeveDeixarEntidadeBloquear()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var usuarioRepository = new Mock<IUsuarioRepository>();
        var indicacao = CriarIndicacao();
        var indicadoId = Guid.NewGuid();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicacao);
        usuarioRepository
            .Setup(repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CriarUsuario());
        var service = CriarService(indicacaoRepository, usuarioRepository);

        await Assert.ThrowsAsync<DomainException>(() => service.VincularUsuarioIndicadoAsync(
            new VincularUsuarioIndicadoDto { IndicacaoId = indicacao.Id, UsuarioIndicadoId = indicacao.UsuarioIndicadorId }));

        indicacao.VincularUsuarioIndicado(indicadoId);
        await Assert.ThrowsAsync<DomainException>(() => service.VincularUsuarioIndicadoAsync(
            new VincularUsuarioIndicadoDto { IndicacaoId = indicacao.Id, UsuarioIndicadoId = Guid.NewGuid() }));

        indicacaoRepository.Verify(repository => repository.AtualizarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VincularVistoriaAsync_QuandoVistoriaExisteEUsuarioCorresponde_DeveVincularAtualizarEPropagarToken()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var usuarioRepository = new Mock<IUsuarioRepository>();
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var indicacao = CriarIndicacao();
        var usuarioIndicadoId = Guid.NewGuid();
        var vistoriaId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        indicacao.VincularUsuarioIndicado(usuarioIndicadoId);
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, cancellationToken))
            .ReturnsAsync(indicacao);
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(vistoriaId, cancellationToken))
            .ReturnsAsync(CriarVistoria(usuarioIndicadoId));
        indicacaoRepository
            .Setup(repository => repository.AtualizarAsync(indicacao, cancellationToken))
            .Returns(Task.CompletedTask);

        await CriarService(indicacaoRepository, usuarioRepository, vistoriaRepository).VincularVistoriaAsync(
            new VincularVistoriaDto { IndicacaoId = indicacao.Id, VistoriaId = vistoriaId },
            cancellationToken);

        Assert.Equal(vistoriaId, indicacao.VistoriaId);
        indicacaoRepository.Verify(repository => repository.AtualizarAsync(indicacao, cancellationToken), Times.Once);
        vistoriaRepository.Verify(repository => repository.ObterPorIdAsync(vistoriaId, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task VincularVistoriaAsync_QuandoVistoriaNaoExiste_DeveLancarVistoriaNaoEncontradaException()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var indicacao = CriarIndicacao();
        var vistoriaId = Guid.NewGuid();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicacao);
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(vistoriaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vistoria?)null);

        await Assert.ThrowsAsync<VistoriaNaoEncontradaException>(() =>
            CriarService(indicacaoRepository, new Mock<IUsuarioRepository>(), vistoriaRepository).VincularVistoriaAsync(
                new VincularVistoriaDto { IndicacaoId = indicacao.Id, VistoriaId = vistoriaId }));

        indicacaoRepository.Verify(repository => repository.AtualizarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VincularVistoriaAsync_QuandoIndicacaoNaoPossuiUsuarioIndicado_DeveLancarDomainException()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var indicacao = CriarIndicacao();
        var vistoriaId = Guid.NewGuid();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicacao);
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(vistoriaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CriarVistoria());

        await Assert.ThrowsAsync<DomainException>(() =>
            CriarService(indicacaoRepository, new Mock<IUsuarioRepository>(), vistoriaRepository).VincularVistoriaAsync(
                new VincularVistoriaDto { IndicacaoId = indicacao.Id, VistoriaId = vistoriaId }));

        indicacaoRepository.Verify(repository => repository.AtualizarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VincularVistoriaAsync_QuandoVistoriaPertenceAOutroUsuario_DeveLancarDomainException()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var indicacao = CriarIndicacao();
        indicacao.VincularUsuarioIndicado(Guid.NewGuid());
        var vistoriaId = Guid.NewGuid();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicacao);
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(vistoriaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CriarVistoria());

        await Assert.ThrowsAsync<DomainException>(() =>
            CriarService(indicacaoRepository, new Mock<IUsuarioRepository>(), vistoriaRepository).VincularVistoriaAsync(
                new VincularVistoriaDto { IndicacaoId = indicacao.Id, VistoriaId = vistoriaId }));

        indicacaoRepository.Verify(repository => repository.AtualizarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarcarVistoriaConcluidaAsync_QuandoVistoriaRealEstaConcluida_DeveConcluirEAtualizar()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var indicacao = CriarIndicacao();
        var vistoria = CriarVistoria();
        vistoria.MarcarRealizada();
        vistoria.Concluir();
        indicacao.VincularVistoria(vistoria.Id);
        var cancellationToken = new CancellationTokenSource().Token;
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, cancellationToken))
            .ReturnsAsync(indicacao);
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(vistoria.Id, cancellationToken))
            .ReturnsAsync(vistoria);
        indicacaoRepository
            .Setup(repository => repository.AtualizarAsync(indicacao, cancellationToken))
            .Returns(Task.CompletedTask);

        await CriarService(indicacaoRepository, new Mock<IUsuarioRepository>(), vistoriaRepository)
            .MarcarVistoriaConcluidaAsync(indicacao.Id, cancellationToken);

        Assert.Equal(StatusIndicacao.VistoriaConcluida, indicacao.Status);
        indicacaoRepository.Verify(repository => repository.AtualizarAsync(indicacao, cancellationToken), Times.Once);
        vistoriaRepository.Verify(repository => repository.ObterPorIdAsync(vistoria.Id, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task MarcarVistoriaConcluidaAsync_QuandoVistoriaNaoExiste_DeveLancarVistoriaNaoEncontradaException()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var indicacao = CriarIndicacao();
        var vistoriaId = Guid.NewGuid();
        indicacao.VincularVistoria(vistoriaId);
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicacao);
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(vistoriaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vistoria?)null);

        await Assert.ThrowsAsync<VistoriaNaoEncontradaException>(() =>
            CriarService(indicacaoRepository, new Mock<IUsuarioRepository>(), vistoriaRepository)
                .MarcarVistoriaConcluidaAsync(indicacao.Id));
    }

    [Fact]
    public async Task MarcarVistoriaConcluidaAsync_QuandoVistoriaAindaNaoFoiConcluida_DeveLancarDomainException()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var indicacao = CriarIndicacao();
        var vistoria = CriarVistoria();
        indicacao.VincularVistoria(vistoria.Id);
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicacao);
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(vistoria.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vistoria);

        await Assert.ThrowsAsync<DomainException>(() =>
            CriarService(indicacaoRepository, new Mock<IUsuarioRepository>(), vistoriaRepository)
                .MarcarVistoriaConcluidaAsync(indicacao.Id));

        indicacaoRepository.Verify(repository => repository.AtualizarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarcarVistoriaConcluidaAsync_QuandoEstadoInvalido_NaoDeveAtualizar()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var indicacao = CriarIndicacao();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicacao);

        await Assert.ThrowsAsync<DomainException>(() =>
            CriarService(indicacaoRepository, new Mock<IUsuarioRepository>()).MarcarVistoriaConcluidaAsync(indicacao.Id));

        indicacaoRepository.Verify(repository => repository.AtualizarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelarAsync_QuandoIndicacaoValida_DeveCancelarEAtualizarUmaVez()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var indicacao = CriarIndicacao();
        var cancellationToken = new CancellationTokenSource().Token;
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(indicacao.Id, cancellationToken))
            .ReturnsAsync(indicacao);
        indicacaoRepository
            .Setup(repository => repository.AtualizarAsync(indicacao, cancellationToken))
            .Returns(Task.CompletedTask);

        await CriarService(indicacaoRepository, new Mock<IUsuarioRepository>())
            .CancelarAsync(indicacao.Id, cancellationToken);

        Assert.Equal(StatusIndicacao.Cancelada, indicacao.Status);
        indicacaoRepository.Verify(repository => repository.AtualizarAsync(indicacao, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_QuandoJaCanceladaOuConcluida_DeveRespeitarRegrasDaEntidade()
    {
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var cancelada = CriarIndicacao();
        cancelada.Cancelar();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(cancelada.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelada);
        var service = CriarService(indicacaoRepository, new Mock<IUsuarioRepository>());

        await service.CancelarAsync(cancelada.Id);
        indicacaoRepository.Verify(repository => repository.AtualizarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);

        var concluida = CriarIndicacao();
        concluida.VincularVistoria(Guid.NewGuid());
        concluida.MarcarVistoriaConcluida();
        indicacaoRepository
            .Setup(repository => repository.ObterPorIdAsync(concluida.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(concluida);

        await Assert.ThrowsAsync<DomainException>(() => service.CancelarAsync(concluida.Id));
        indicacaoRepository.Verify(repository => repository.AtualizarAsync(It.IsAny<Indicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IndicacaoService CriarService(
        Mock<IIndicacaoRepository> indicacaoRepository,
        Mock<IUsuarioRepository> usuarioRepository,
        Mock<IVistoriaRepository>? vistoriaRepository = null) =>
        new(indicacaoRepository.Object, usuarioRepository.Object, (vistoriaRepository ?? new Mock<IVistoriaRepository>()).Object);

    private static CreateIndicacaoDto CriarDto(Guid usuarioIndicadorId) => new()
    {
        UsuarioIndicadorId = usuarioIndicadorId,
        NomeIndicada = "Ana Indicada",
        TelefoneIndicada = "11999999999",
        CodigoIndicacaoUsado = "A2-123"
    };

    private static Indicacao CriarIndicacao(Guid? usuarioIndicadorId = null) =>
        new(usuarioIndicadorId ?? Guid.NewGuid(), "Ana Indicada", "11999999999", "A2-123");

    private static Vistoria CriarVistoria(Guid? usuarioId = null) => new(
        usuarioId ?? Guid.NewGuid(),
        "Apartamento",
        70m,
        PacoteVistoria.Simples,
        DateTime.UtcNow);

    private static Usuario CriarUsuario() =>
        new("Usuário", "usuario@a2.com", "hash");
}
