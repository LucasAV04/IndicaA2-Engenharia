using API.ExceptionHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace API.Tests.ExceptionHandling;

public sealed class PrecificacaoSanitizacaoTests
{
    [Theory]
    [InlineData("/api/tipos-planta")]
    [InlineData("/api/precos-vistoria/simular")]
    [InlineData("/api/vistorias")]
    public async Task ExcecaoArbitrariaNaoVazaEmRespostaOuLogger(string path)
    {
        const string segredo = "SEGREDO_FICTICIO_NAO_LOGAR";
        var logger = new LoggerSeguro();
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        await new GlobalExceptionHandler(logger).TryHandleAsync(context, new InvalidOperationException(segredo), default);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(500, context.Response.StatusCode);
        Assert.DoesNotContain(segredo, body);
        Assert.DoesNotContain(segredo, string.Join(" ", logger.Textos));
        Assert.All(logger.Excecoes, Assert.Null);
        Assert.Single(logger.Textos);
    }

    private sealed class LoggerSeguro : ILogger<GlobalExceptionHandler>
    {
        public List<string> Textos { get; } = [];
        public List<Exception?> Excecoes { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Textos.Add(formatter(state, exception)); Excecoes.Add(exception); }
    }
}
