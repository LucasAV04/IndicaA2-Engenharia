using Domain.Exceptions;
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
            IndicacaoNaoEncontradaException => (
                StatusCodes.Status404NotFound,
                "Indicação não encontrada",
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
