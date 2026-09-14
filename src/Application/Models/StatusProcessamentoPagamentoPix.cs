namespace Application.Models;

/// <summary>
/// Resultado provider-agnostic de uma única etapa do processamento interno de Pagamento Pix.
/// </summary>
public enum StatusProcessamentoPagamentoPix
{
    Aplicado = 0,
    JaAplicado = 1,
    EnvioExecutadoAguardandoResultado = 2,
    ReconciliacaoExecutadaAguardandoResultado = 3,
    EnvioEmAndamento = 4,
    EnvioPendenteRecuperacao = 5,
    ConsultaEmAndamento = 6,
    AguardandoPoliticaRetry = 7,
    Terminal = 8,
    NaoAplicavel = 9
}
