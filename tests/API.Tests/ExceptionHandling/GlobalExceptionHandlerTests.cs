using System.Text.Json;
using API.ExceptionHandling;
using Domain.Exceptions;
using Domain.Exceptions.Usuario;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace API.Tests.ExceptionHandling;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_QuandoIndicacaoNaoForEncontrada_DeveRetornarProblemDetails404()
    {
        var context = CriarContexto();
        var handler = CriarHandler();

        var tratado = await handler.TryHandleAsync(
            context,
            new IndicacaoNaoEncontradaException(),
            CancellationToken.None);

        Assert.True(tratado);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        var problemDetails = await LerProblemDetailsAsync(context);
        Assert.Equal(StatusCodes.Status404NotFound, problemDetails.Status);
    }

    [Fact]
    public async Task TryHandleAsync_QuandoRegraDeDominioForViolada_DeveRetornarProblemDetails422()
    {
        var context = CriarContexto();
        var handler = CriarHandler();

        var tratado = await handler.TryHandleAsync(
            context,
            new DomainException("A operação não é permitida."),
            CancellationToken.None);

        Assert.True(tratado);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        var problemDetails = await LerProblemDetailsAsync(context);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problemDetails.Status);
    }

    [Fact]
    public async Task TryHandleAsync_QuandoVistoriaNaoForEncontrada_DeveRetornarProblemDetails404()
    {
        var context = CriarContexto();
        var handler = CriarHandler();

        var tratado = await handler.TryHandleAsync(
            context,
            new VistoriaNaoEncontradaException(),
            CancellationToken.None);

        Assert.True(tratado);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        var problemDetails = await LerProblemDetailsAsync(context);
        Assert.Equal("Vistoria não encontrada", problemDetails.Title);
    }

    [Fact]
    public async Task TryHandleAsync_QuandoUsuarioNaoForEncontrado_DeveRetornarProblemDetails404()
    {
        var context = CriarContexto();
        var handler = CriarHandler();

        var tratado = await handler.TryHandleAsync(
            context,
            new UsuarioNaoEncontradoException(),
            CancellationToken.None);

        Assert.True(tratado);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        var problemDetails = await LerProblemDetailsAsync(context);
        Assert.Equal("Usuário não encontrado", problemDetails.Title);
    }

    private static DefaultHttpContext CriarContexto()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static GlobalExceptionHandler CriarHandler() => new(NullLogger<GlobalExceptionHandler>.Instance);

    private static async Task<ProblemDetails> LerProblemDetailsAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return (await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body))!;
    }
}
