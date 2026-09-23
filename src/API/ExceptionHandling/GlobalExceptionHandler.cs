using Domain.Exceptions;
using Domain.Exceptions.Usuario;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace API.ExceptionHandling;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            PrecificacaoException p => (
                p.Codigo == "tipo_ausente" ? 404 : 409,
                "Configuração de precificação",
                p.Codigo switch
                {
                    "preco_ausente" => "Configure um preço ativo para o tipo antes de criar ou simular a vistoria.",
                    "tipo_inativo" => "Selecione um tipo de planta ativo.",
                    "tipo_ausente" => "Tipo de planta não encontrado.",
                    "nome_duplicado" => "Já existe um tipo com esse nome.",
                    "desative_preco_primeiro" => "Desative primeiro o preço ativo deste tipo.",
                    _ => "A versão foi alterada. Atualize o histórico antes de publicar."
                }),
            CashbackNaoEncontradoException => (
                StatusCodes.Status404NotFound,
                "Cashback não encontrado",
                exception.Message),
            PagamentoVistoriaNaoEncontradoException => (
                StatusCodes.Status404NotFound,
                "Pagamento da vistoria não encontrado",
                exception.Message),
            PagamentoPixNaoEncontradoException => (
                StatusCodes.Status404NotFound,
                "Pagamento Pix não encontrado",
                exception.Message),
            CodigoIndicacaoNaoEncontradoException => (
                StatusCodes.Status404NotFound,
                "Código de indicação não encontrado",
                exception.Message),
            IndicacaoNaoEncontradaException => (
                StatusCodes.Status404NotFound,
                "Indicação não encontrada",
                exception.Message),
            VistoriaNaoEncontradaException => (
                StatusCodes.Status404NotFound,
                "Vistoria não encontrada",
                exception.Message),
            UsuarioNaoEncontradoException => (
                StatusCodes.Status404NotFound,
                "Usuário não encontrado",
                exception.Message),
            CredenciaisInvalidasException => (
                StatusCodes.Status401Unauthorized,
                "Credenciais inválidas",
                exception.Message),
            UsuarioSemAcessoException => (
                StatusCodes.Status403Forbidden,
                "Acesso negado",
                exception.Message),
            DomainException => (
                StatusCodes.Status422UnprocessableEntity,
                "Regra de domínio violada",
                exception.Message),
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "Requisição inválida",
                exception.Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Erro interno do servidor",
                "Ocorreu um erro inesperado ao processar a solicitação.")
        };

        var path = httpContext.Request.Path;
        if (path.StartsWithSegments("/api/precos-vistoria") || path.StartsWithSegments("/api/tipos-planta") || path.StartsWithSegments("/api/vistorias"))
        {
            if (exception is not PrecificacaoException)
                detail = status < 500 ? "Verifique os campos e o estado da configuração de precificação." : "Não foi possível concluir a operação.";
            logger.LogWarning("Falha de vistoria/precificação: {Tipo}, status {Status}.", exception.GetType().Name, status);
        }
        else if (httpContext.Request.Path.Value?.Contains("/dados-pix", StringComparison.OrdinalIgnoreCase) == true)
        {
            detail = "Não foi possível concluir a operação de Dados Pix. Verifique o tipo e o formato da chave.";
            logger.LogWarning("Falha de Dados Pix: {Tipo}, status {Status}.", exception.GetType().Name, status);
        }
        else if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Erro inesperado durante o processamento da solicitação.");
        }
        else
        {
            logger.LogWarning(exception, "Falha tratada durante o processamento da solicitação.");
        }

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{status}"
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
