using Application.Interfaces.Providers;
using Domain.Enums;
using Infrastructure.Providers;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// Contrato externo opcional. Só efetua chamada à Efí quando todas as variáveis
/// de sandbox, inclusive uma referência de consulta, forem fornecidas pelo ambiente.
/// </summary>
public sealed class EfiPixSandboxIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public EfiPixSandboxIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ConsultarAsync_QuandoSandboxEstaConfigurado_DeveUsarOAuthEMtlsSemEnviarPix()
    {
        var clientId = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CLIENT_SECRET");
        var certificatePath = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CERTIFICATE_PATH");
        var certificatePassword = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CERTIFICATE_PASSWORD");
        var chavePixPagador = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CHAVE_PIX_PAGADOR");
        var referencia = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_QUERY_ID_ENVIO");

        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret)
            || string.IsNullOrWhiteSpace(certificatePath)
            || string.IsNullOrWhiteSpace(chavePixPagador)
            || !Guid.TryParseExact(referencia, "N", out var pagamentoPixId))
        {
            throw SkipException.ForSkip(
                "Teste real de consulta sandbox ignorado: variáveis INDICA2_EFI_SANDBOX obrigatórias ausentes no processo atual.");
        }

        var options = new EfiPixOptions
        {
            Environment = "Sandbox",
            BaseUrl = "https://pix-h.api.efipay.com.br",
            ClientId = clientId,
            ClientSecret = clientSecret,
            CertificatePath = certificatePath,
            CertificatePassword = certificatePassword,
            ChavePixPagador = chavePixPagador
        };

        using var handler = EfiPixHttpMessageHandlerFactory.Criar(options);
        using var client = new HttpClient(handler);
        var provider = new EfiPixProvider(client, options);

        var result = await provider.ConsultarAsync(new PixConsultaRequest(pagamentoPixId));

        Assert.Contains(result.Status, Enum.GetValues<StatusPixProvider>());
    }

    [Fact]
    public async Task EnviarAsync_QuandoSandboxEstaConfigurado_DeveEnviarPixDeHomologacaoComIdEnvioEstavel()
    {
        var clientId = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CLIENT_SECRET");
        var certificatePath = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CERTIFICATE_PATH");
        var certificatePassword = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CERTIFICATE_PASSWORD");
        var chavePixPagador = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CHAVE_PIX_PAGADOR");
        var chavePixFavorecido = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CHAVE_PIX_FAVORECIDO");

        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret)
            || string.IsNullOrWhiteSpace(certificatePath)
            || string.IsNullOrWhiteSpace(chavePixPagador)
            || string.IsNullOrWhiteSpace(chavePixFavorecido))
        {
            throw SkipException.ForSkip(
                "Teste real de envio sandbox ignorado: variáveis INDICA2_EFI_SANDBOX obrigatórias, incluindo INDICA2_EFI_SANDBOX_CHAVE_PIX_FAVORECIDO, ausentes no processo atual.");
        }

        var options = new EfiPixOptions
        {
            Environment = "Sandbox",
            BaseUrl = "https://pix-h.api.efipay.com.br",
            ClientId = clientId,
            ClientSecret = clientSecret,
            CertificatePath = certificatePath,
            CertificatePassword = certificatePassword,
            ChavePixPagador = chavePixPagador
        };

        var pagamentoPixId = Guid.NewGuid();
        // R$ 0,01 é o menor valor que a documentação oficial define como confirmação simulada em homologação.
        var request = new PixEnvioRequest(
            pagamentoPixId,
            0.01m,
            TipoChavePix.Email,
            chavePixFavorecido);

        Assert.Equal(pagamentoPixId.ToString("N"), request.ReferenciaIdempotente);
        var redator = new RedatorDiagnosticoSandbox(options, request.ChavePix);

        ObservadorStatusHttpMessageHandler observador;
        try
        {
            observador = new ObservadorStatusHttpMessageHandler(
                EfiPixHttpMessageHandlerFactory.Criar(options),
                redator,
                options,
                request);
        }
        catch (Exception exception)
        {
            FalharComDiagnostico(
                "criação do certificado/mTLS",
                exception,
                [],
                redator);
            return;
        }

        using (observador)
        using (var client = new HttpClient(observador))
        {
            var provider = new EfiPixProvider(client, options);

            var envio = await ExecutarComDiagnosticoAsync(
                "PUT /v3/gn/pix/{idEnvio}",
                () => provider.EnviarAsync(request),
                observador,
                redator);

            if (!observador.StatusCodes.Contains(HttpStatusCode.Created))
            {
                FalharSemRespostaAceita(observador, redator);
                return;
            }

            var consultaRequest = new PixConsultaRequest(pagamentoPixId);
            var consulta = await ExecutarComDiagnosticoAsync(
                "consulta posterior",
                () => provider.ConsultarAsync(consultaRequest),
                observador,
                redator);

            Assert.Equal(request.ReferenciaIdempotente, consultaRequest.ReferenciaIdempotente);
            Assert.DoesNotContain(HttpStatusCode.Unauthorized, observador.StatusCodes);
            Assert.DoesNotContain(HttpStatusCode.Forbidden, observador.StatusCodes);
            Assert.NotEqual(StatusPixProvider.FalhaConfirmada, envio.Status);
            Assert.Contains(envio.Status, Enum.GetValues<StatusPixProvider>());
            Assert.Contains(consulta.Status, Enum.GetValues<StatusPixProvider>());
            _output.WriteLine($"Códigos HTTP observados: {FormatarCodigosHttp(observador.StatusCodes)}.");
            _output.WriteLine($"Estrutura do payload PUT: {observador.EstruturaPayloadPut ?? "não observada"}.");
        }
    }

    private static async Task<T> ExecutarComDiagnosticoAsync<T>(
        string etapa,
        Func<Task<T>> acao,
        ObservadorStatusHttpMessageHandler observador,
        RedatorDiagnosticoSandbox redator)
    {
        try
        {
            return await acao();
        }
        catch (Exception exception)
        {
            FalharComDiagnostico(etapa, exception, observador.StatusCodes, redator);
            return default!;
        }
    }

    private static void FalharSemRespostaAceita(
        ObservadorStatusHttpMessageHandler observador,
        RedatorDiagnosticoSandbox redator)
    {
        if (observador.UltimaExcecao is not null)
        {
            FalharComDiagnostico(
                observador.UltimaEtapa ?? "PUT /v3/gn/pix/{idEnvio}",
                observador.UltimaExcecao,
                observador.StatusCodes,
                redator);
            return;
        }

        Assert.Fail(
            $"Sandbox Efí não retornou HTTP 201 no estágio {observador.UltimaEtapa ?? "PUT /v3/gn/pix/{idEnvio}"}. " +
            $"Códigos HTTP observados: {FormatarCodigosHttp(observador.StatusCodes)}. " +
            $"Diagnóstico PUT sanitizado: {observador.DiagnosticoPut ?? "não disponível"}. " +
            $"Estrutura do payload PUT: {observador.EstruturaPayloadPut ?? "não observada"}.");
    }

    private static void FalharComDiagnostico(
        string etapa,
        Exception exception,
        IReadOnlyCollection<HttpStatusCode> statusCodes,
        RedatorDiagnosticoSandbox redator)
    {
        var inner = exception.InnerException;
        var diagnostico =
            $"Falha sandbox Efí no estágio '{etapa}'. " +
            $"Exceção: {exception.GetType().Name}. " +
            $"Mensagem: {redator.Sanitizar(exception.Message)}. " +
            $"Inner: {(inner is null ? "nenhuma" : $"{inner.GetType().Name}: {redator.Sanitizar(inner.Message)}")}. " +
            $"Códigos HTTP observados: {FormatarCodigosHttp(statusCodes)}.";

        Assert.Fail(diagnostico);
    }

    private static string FormatarCodigosHttp(IEnumerable<HttpStatusCode> statusCodes)
    {
        var codigos = statusCodes.Select(statusCode => ((int)statusCode).ToString()).ToArray();
        return codigos.Length == 0 ? "nenhum" : string.Join(", ", codigos);
    }

    private sealed class ObservadorStatusHttpMessageHandler(
        HttpMessageHandler innerHandler,
        RedatorDiagnosticoSandbox redator,
        EfiPixOptions options,
        PixEnvioRequest envioRequest) : DelegatingHandler(innerHandler)
    {
        public List<HttpStatusCode> StatusCodes { get; } = [];
        public string? UltimaEtapa { get; private set; }
        public Exception? UltimaExcecao { get; private set; }
        public string? DiagnosticoPut { get; private set; }
        public string? EstruturaPayloadPut { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            UltimaEtapa = request.RequestUri?.AbsolutePath switch
            {
                "/oauth/token" => "OAuth",
                var path when path?.StartsWith("/v3/gn/pix/", StringComparison.Ordinal) == true => "PUT /v3/gn/pix/{idEnvio}",
                var path when path?.StartsWith("/v2/gn/pix/enviados/id-envio/", StringComparison.Ordinal) == true => "consulta posterior",
                _ => "requisição HTTP Efí"
            };

            try
            {
                if (UltimaEtapa == "PUT /v3/gn/pix/{idEnvio}")
                    EstruturaPayloadPut = await InspecionarEstruturaPayloadPutAsync(
                        request,
                        options,
                        envioRequest,
                        cancellationToken);

                var response = await base.SendAsync(request, cancellationToken);
                StatusCodes.Add(response.StatusCode);

                if (UltimaEtapa == "PUT /v3/gn/pix/{idEnvio}" && response.StatusCode != HttpStatusCode.Created)
                    DiagnosticoPut = await ExtrairDiagnosticoPutAsync(response, redator, cancellationToken);

                return response;
            }
            catch (Exception exception)
            {
                UltimaExcecao = exception;
                throw;
            }
        }

        private static async Task<string> InspecionarEstruturaPayloadPutAsync(
            HttpRequestMessage request,
            EfiPixOptions options,
            PixEnvioRequest envioRequest,
            CancellationToken cancellationToken)
        {
            if (request.Content is null)
                return "Content-Type=ausente; payload=ausente";

            var contentType = request.Content.Headers.ContentType?.ToString() ?? "ausente";
            var body = await request.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    return $"Content-Type={contentType}; payload JSON não é objeto";

                var camposRaiz = root.EnumerateObject().Select(property => property.Name).Order().ToArray();
                var camposExtras = camposRaiz.Except(["valor", "pagador", "favorecido"], StringComparer.Ordinal).ToArray();
                var camposAusentes = new[] { "valor", "pagador", "favorecido" }
                    .Where(campo => !root.TryGetProperty(campo, out _))
                    .ToArray();

                var valorComoString = root.TryGetProperty("valor", out var valor)
                    && valor.ValueKind == JsonValueKind.String;
                var valorFormatoInvariavel = valorComoString
                    && Regex.IsMatch(valor.GetString()!, @"^\d+\.\d{2}$");
                var pagadorComChave = PossuiSomenteChave(root, "pagador", out var chavePagador);
                var favorecidoComChave = PossuiSomenteChave(root, "favorecido", out var chaveFavorecido);
                var chavePagadorOrigemConfiguracao = string.Equals(
                    chavePagador,
                    options.ChavePixPagador,
                    StringComparison.Ordinal);
                var chaveFavorecidoOrigemRequest = string.Equals(
                    chaveFavorecido,
                    envioRequest.ChavePix,
                    StringComparison.Ordinal);

                return $"Content-Type={contentType}; campos raiz=[{string.Join(", ", camposRaiz)}]; " +
                    $"valor=json-string; formato decimal=duas casas com ponto (F2/cultura invariável):{valorFormatoInvariavel}; " +
                    $"pagador.chave={pagadorComChave}; origem pagador=INDICA2_EFI_SANDBOX_CHAVE_PIX_PAGADOR:{chavePagadorOrigemConfiguracao}; " +
                    $"favorecido.chave={favorecidoComChave}; origem favorecido=PixEnvioRequest:{chaveFavorecidoOrigemRequest}; " +
                    $"campos ausentes=[{string.Join(", ", camposAusentes)}]; campos extras=[{string.Join(", ", camposExtras)}]; " +
                    $"valor como string={valorComoString}";
            }
            catch (JsonException)
            {
                return $"Content-Type={contentType}; payload não é JSON válido";
            }
        }

        private static bool PossuiSomenteChave(JsonElement root, string nomePropriedade, out string? chave)
        {
            chave = null;
            if (!root.TryGetProperty(nomePropriedade, out var objeto) || objeto.ValueKind != JsonValueKind.Object)
                return false;

            var propriedades = objeto.EnumerateObject().ToArray();
            var chaveProperty = propriedades.SingleOrDefault(property => property.Name == "chave");
            if (propriedades.Length != 1 || chaveProperty.Value.ValueKind != JsonValueKind.String)
                return false;

            chave = chaveProperty.Value.GetString();
            return true;
        }

        private static async Task<string> ExtrairDiagnosticoPutAsync(
            HttpResponseMessage response,
            RedatorDiagnosticoSandbox redator,
            CancellationToken cancellationToken)
        {
            if (response.Content is null)
                return "corpo de resposta ausente";

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
                return "corpo de resposta vazio";

            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return "corpo JSON sem objeto de diagnóstico";

                var campos = new List<string>();
                AdicionarCampoDiagnostico(document.RootElement, "nome", campos, redator);
                AdicionarCampoDiagnostico(document.RootElement, "mensagem", campos, redator);
                AdicionarCampoDiagnostico(document.RootElement, "title", campos, redator);
                AdicionarCampoDiagnostico(document.RootElement, "detail", campos, redator);
                AdicionarCampoDiagnostico(document.RootElement, "status", campos, redator);
                AdicionarCampoDiagnostico(document.RootElement, "error", campos, redator);
                AdicionarCampoDiagnostico(document.RootElement, "error_description", campos, redator);

                if (document.RootElement.TryGetProperty("violacoes", out var violacoes)
                    && violacoes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var violacao in violacoes.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                    {
                        AdicionarCampoDiagnostico(violacao, "razao", campos, redator, "violacoes[].");
                        AdicionarCampoDiagnostico(violacao, "propriedade", campos, redator, "violacoes[].");
                    }
                }

                return campos.Count == 0
                    ? "corpo JSON sem campos diagnósticos permitidos"
                    : string.Join("; ", campos);
            }
            catch (JsonException)
            {
                return "corpo de resposta não é JSON; conteúdo omitido";
            }
        }

        private static void AdicionarCampoDiagnostico(
            JsonElement objeto,
            string nome,
            List<string> campos,
            RedatorDiagnosticoSandbox redator,
            string prefixo = "")
        {
            if (!objeto.TryGetProperty(nome, out var valor))
                return;

            var texto = valor.ValueKind switch
            {
                JsonValueKind.String => valor.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => valor.ToString(),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(texto))
                campos.Add($"{prefixo}{nome}={redator.Sanitizar(texto)}");
        }
    }

    private sealed class RedatorDiagnosticoSandbox
    {
        private readonly string?[] _valoresSensiveis;

        public RedatorDiagnosticoSandbox(EfiPixOptions options, string? chavePixFavorecido)
        {
            _valoresSensiveis =
            [
                options.ClientId,
                options.ClientSecret,
                options.CertificatePath,
                options.CertificatePassword,
                options.ChavePixPagador,
                chavePixFavorecido
            ];
        }

        public string Sanitizar(string? mensagem)
        {
            var resultado = mensagem ?? string.Empty;

            foreach (var valor in _valoresSensiveis
                         .Where(valor => !string.IsNullOrWhiteSpace(valor))
                         .Select(valor => valor!))
                resultado = resultado.Replace(valor, "[redigido]", StringComparison.Ordinal);

            resultado = Regex.Replace(resultado, @"(?i)\b(bearer|basic)\s+[^\s]+", "$1 [redigido]");
            resultado = Regex.Replace(
                resultado,
                @"(?i)\b(access_token|client_secret|authorization|password|chave)\b\s*[:=]\s*[^\s,;]+",
                "$1=[redigido]");
            resultado = Regex.Replace(resultado, @"\b\d{11,14}\b", "[redigido]");
            resultado = Regex.Replace(resultado, @"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b", "[redigido]");
            resultado = Regex.Replace(resultado, @"\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b", "[redigido]");
            resultado = Regex.Replace(resultado, @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[redigido]");
            resultado = Regex.Replace(resultado, @"(?<!\d)\+?55\s?\d{2}\s?9?\d{4}-?\d{4}(?!\d)", "[redigido]");
            resultado = Regex.Replace(resultado, @"(?i)\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b", "[redigido]");

            return resultado.Length <= 500 ? resultado : resultado[..500] + "…";
        }
    }
}
