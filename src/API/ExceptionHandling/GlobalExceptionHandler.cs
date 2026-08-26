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

        if (status >= StatusCodes.Status500InternalServerError)
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
