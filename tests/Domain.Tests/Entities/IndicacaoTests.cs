using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class IndicacaoTests
{
    [Fact]
    public void Construtor_QuandoDadosValidos_DeveCriarIndicacaoPendente()
    {
        var indicadorId = Guid.NewGuid();

        var indicacao = CriarIndicacao(indicadorId);

        Assert.Equal(indicadorId, indicacao.UsuarioIndicadorId);
        Assert.Equal(StatusIndicacao.Pendente, indicacao.Status);
        Assert.Null(indicacao.UsuarioIndicadoId);
        Assert.Null(indicacao.VistoriaId);
    }

    [Theory]
    [InlineData("indicador")]
    [InlineData("nome")]
    [InlineData("telefone")]
    [InlineData("codigo")]
    public void Construtor_QuandoDadoObrigatorioInvalido_DeveLancarDomainException(string campo)
    {
        var indicadorId = campo == "indicador" ? Guid.Empty : Guid.NewGuid();
        var nome = campo == "nome" ? " " : "Ana Indicada";
        var telefone = campo == "telefone" ? " " : "11999999999";
        var codigo = campo == "codigo" ? " " : "A2-123";

        Assert.Throws<DomainException>(() => new Indicacao(indicadorId, nome, telefone, codigo));
    }

    [Fact]
    public void Construtor_QuandoDadosPossuemEspacos_DeveNormalizarDadosDeContatoECodigo()
    {
        var indicacao = new Indicacao(Guid.NewGuid(), "  Ana Indicada  ", " 11999999999 ", " a2-abc ");

        Assert.Equal("Ana Indicada", indicacao.NomeIndicada);
        Assert.Equal("11999999999", indicacao.TelefoneIndicada);
        Assert.Equal("A2-ABC", indicacao.CodigoIndicacaoUsado);
    }

    [Fact]
    public void VincularUsuarioIndicado_QuandoUsuarioValido_DeveVincularEAtualizarData()
    {
        var indicacao = CriarIndicacao();
        var dataAnterior = indicacao.UpdatedAt;
        var indicadoId = Guid.NewGuid();

        indicacao.VincularUsuarioIndicado(indicadoId);

        Assert.Equal(indicadoId, indicacao.UsuarioIndicadoId);
        Assert.True(indicacao.UpdatedAt >= dataAnterior);
    }

    [Fact]
    public void VincularUsuarioIndicado_QuandoIdVazio_DeveLancarDomainException()
    {
        var indicacao = CriarIndicacao();

        Assert.Throws<DomainException>(() => indicacao.VincularUsuarioIndicado(Guid.Empty));
    }

    [Fact]
    public void VincularUsuarioIndicado_QuandoAutoIndicacao_DeveLancarDomainException()
    {
        var indicadorId = Guid.NewGuid();
        var indicacao = CriarIndicacao(indicadorId);

        Assert.Throws<DomainException>(() => indicacao.VincularUsuarioIndicado(indicadorId));
    }

    [Fact]
    public void VincularUsuarioIndicado_QuandoJaExisteVinculo_DeveLancarDomainException()
    {
        var indicacao = CriarIndicacao();
        indicacao.VincularUsuarioIndicado(Guid.NewGuid());

        Assert.Throws<DomainException>(() => indicacao.VincularUsuarioIndicado(Guid.NewGuid()));
    }

    [Fact]
    public void VincularVistoria_QuandoVistoriaValida_DeveVincularAtualizarStatusEData()
    {
        var indicacao = CriarIndicacao();
        var dataAnterior = indicacao.UpdatedAt;
        var vistoriaId = Guid.NewGuid();

        indicacao.VincularVistoria(vistoriaId);

        Assert.Equal(vistoriaId, indicacao.VistoriaId);
        Assert.Equal(StatusIndicacao.VistoriaVinculada, indicacao.Status);
        Assert.True(indicacao.UpdatedAt >= dataAnterior);
    }

    [Fact]
    public void VincularVistoria_QuandoIdVazioOuJaVinculada_DeveLancarDomainException()
    {
        var indicacao = CriarIndicacao();
        Assert.Throws<DomainException>(() => indicacao.VincularVistoria(Guid.Empty));

        indicacao.VincularVistoria(Guid.NewGuid());
        Assert.Throws<DomainException>(() => indicacao.VincularVistoria(Guid.NewGuid()));
    }

    [Fact]
    public void VincularVistoria_QuandoIndicacaoCancelada_DeveLancarDomainException()
    {
        var indicacao = CriarIndicacao();
        indicacao.Cancelar();

        Assert.Throws<DomainException>(() => indicacao.VincularVistoria(Guid.NewGuid()));
    }

    [Fact]
    public void MarcarVistoriaConcluida_QuandoVistoriaVinculada_DeveConcluirEAtualizarData()
    {
        var indicacao = CriarIndicacao();
        indicacao.VincularVistoria(Guid.NewGuid());
        var dataAnterior = indicacao.UpdatedAt;

        indicacao.MarcarVistoriaConcluida();

        Assert.Equal(StatusIndicacao.VistoriaConcluida, indicacao.Status);
        Assert.True(indicacao.UpdatedAt >= dataAnterior);
    }

    [Fact]
    public void MarcarVistoriaConcluida_QuandoPendenteOuCancelada_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() => CriarIndicacao().MarcarVistoriaConcluida());

        var cancelada = CriarIndicacao();
        cancelada.Cancelar();
        Assert.Throws<DomainException>(() => cancelada.MarcarVistoriaConcluida());
    }

    [Fact]
    public void Cancelar_QuandoPendenteOuVistoriaVinculada_DeveCancelar()
    {
        var pendente = CriarIndicacao();
        pendente.Cancelar();
        Assert.Equal(StatusIndicacao.Cancelada, pendente.Status);

        var vinculada = CriarIndicacao();
        vinculada.VincularVistoria(Guid.NewGuid());
        vinculada.Cancelar();
        Assert.Equal(StatusIndicacao.Cancelada, vinculada.Status);
    }

    [Fact]
    public void Cancelar_QuandoJaCancelada_DeveSerIdempotenteSemAlterarDataNovamente()
    {
        var indicacao = CriarIndicacao();
        indicacao.Cancelar();
        var dataCancelamento = indicacao.UpdatedAt;

        indicacao.Cancelar();

        Assert.Equal(StatusIndicacao.Cancelada, indicacao.Status);
        Assert.Equal(dataCancelamento, indicacao.UpdatedAt);
    }

    [Fact]
    public void Cancelar_QuandoVistoriaConcluida_DeveLancarDomainException()
    {
        var indicacao = CriarIndicacao();
        indicacao.VincularVistoria(Guid.NewGuid());
        indicacao.MarcarVistoriaConcluida();

        Assert.Throws<DomainException>(() => indicacao.Cancelar());
    }

    private static Indicacao CriarIndicacao(Guid? usuarioIndicadorId = null) =>
        new(usuarioIndicadorId ?? Guid.NewGuid(), "Ana Indicada", "11999999999", "A2-123");
}
