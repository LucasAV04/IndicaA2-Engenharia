namespace Application.Models;

public enum StatusPreparacaoReconciliacaoPagamentoPix
{
    ConsultaPreparada = 0,
    NaoAplicavel = 1,
    ConsultaEmAndamento = 2,
    ResultadoJaConclusivo = 3,
    EnvioEmAndamento = 4,
    EnvioPendenteRecuperacao = 5
}
