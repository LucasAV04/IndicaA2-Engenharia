namespace Application.Recebimentos;

public sealed class RecebimentoPixOptions
{
    public bool Habilitado { get; set; }
    public int ExpiracaoSegundos { get; set; } = 3600;
    public string UrlPublica { get; set; } = "";
    public string ChaveRecebedora { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    public string WebhookCaPath { get; set; } = "";
    public WorkerRecebimentoOptions ProcessamentoWorker { get; set; } = new();

    public void Validar()
    {
        if (!Habilitado)
        {
            if (ProcessamentoWorker.Habilitado) throw new InvalidOperationException("Worker exige recebimento habilitado.");
            return;
        }
        if (ExpiracaoSegundos is < 60 or > 86400 || ProcessamentoWorker.IntervaloSegundos is < 5 or > 3600
            || ProcessamentoWorker.TamanhoLote is < 1 or > 100)
            throw new InvalidOperationException("Limites do recebimento inválidos.");
        foreach (var url in new[] { UrlPublica, WebhookUrl })
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("URL HTTPS de recebimento inválida.");
        if (WebhookUrl.TrimEnd('/').EndsWith("/pix", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(ChaveRecebedora) || string.IsNullOrWhiteSpace(WebhookCaPath))
            throw new InvalidOperationException("Configuração de recebimento incompleta.");
    }
    public void ExigirHabilitado()
    {
        if (!Habilitado) throw new InvalidOperationException("Recebimento Pix desabilitado.");
    }
}
public sealed class WorkerRecebimentoOptions
{
    public bool Habilitado { get; set; }
    public int IntervaloSegundos { get; set; } = 30;
    public int TamanhoLote { get; set; } = 20;
}
