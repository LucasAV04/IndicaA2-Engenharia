# Spike: compatibilidade da SDK Efí com .NET 9

**Data:** 2026-08-18
**Escopo:** POC técnico isolado. Não implementa Pix, cashback, pagamentos, preços, webhook, credenciais ou integração de produção.

## Objetivo

Verificar se a SDK oficial .NET da Efí pode ser restaurada, compilada, carregada e inspecionada por um projeto `net9.0`, preservando as camadas de produção do IndicA2 sem dependência direta da Efí.

## SDK avaliada

- Pacote oficial: [`EfiPay` 2.0.4](https://www.nuget.org/packages/EfiPay/2.0.4).
- Repositório oficial: [`efipay/sdk-dotnet-apis-efi`](https://github.com/efipay/sdk-dotnet-apis-efi).
- A documentação e o repositório oficial indicam teste com .NET 8.0. O pacote contém asset `lib/net8.0`, consumido pelo POC `net9.0` por compatibilidade de framework.
- Não foi identificada versão estável posterior a `2.0.4` no NuGet durante a execução deste spike.

## Resultado de compatibilidade

| Verificação | Resultado |
| --- | --- |
| Target do POC | `net9.0` |
| Restore | Sucesso, sem `NU1202`, `NU1701`, downgrade ou conflito de assembly |
| Build | Sucesso, 0 avisos e 0 erros |
| Carregamento | Assembly `Efipay`, versão `2.0.4.0` |
| Instanciação local | Sucesso com valores fictícios e sem certificado |
| Requisição à Efí | Não executada |
| Vulnerabilidades NuGet | Nenhuma reportada pelas fontes configuradas |

## Dependências observadas

Dependência direta:

- `EfiPay` 2.0.4.

Transitivas relevantes:

- `Newtonsoft.Json` 13.0.3;
- `RestSharp` 112.1.0;
- `Microsoft.NETCore.Platforms` 1.1.0;
- `Microsoft.NETCore.Targets` 1.1.0;
- `System.IO`, `System.Runtime`, `System.Text.Encoding` e `System.Threading.Tasks` 4.3.0.

O POC não apresentou alerta de vulnerabilidade nas fontes NuGet consultadas. A presença de dependências de compatibilidade antigas deve ser reavaliada quando houver uma integração real.

## API Pix encontrada

A classe `Efipay.EfiPay` herda de `DynamicObject`: os endpoints não aparecem como métodos CLR públicos convencionais, mas são resolvidos dinamicamente por nome. O POC instanciou o cliente com valores fictícios e inspecionou o mapa interno de endpoints, sem invocar nenhum deles.

| Operação | Método dinâmico | Rota registrada pela SDK 2.0.4 |
| --- | --- | --- |
| Enviar Pix | `PixSend` | `PUT /v2/gn/pix/:idEnvio` |
| Consultar por E2E ID | `PixSendDetail` | `GET /v2/gn/pix/enviados/:e2eid` |
| Consultar por ID de envio | `PixSendDetailId` | `GET /v2/gn/pix/enviados/id-envio/:idEnvio` |
| Listar envios | `PixSendList` | `GET /v2/gn/pix/enviados` |
| Configurar webhook | `PixConfigWebhook` | `PUT /v2/webhook/:chave` |

Portanto, a SDK expõe suporte específico para **envio Pix**, e não somente para cobrança Pix. Isso é o tipo de operação necessário para um futuro pagamento de cashback.

## Ressalva de contrato da API

A documentação atual da Efí para envio Pix informa `PUT /v3/gn/pix/:idEnvio`, enquanto a SDK 2.0.4 registra `PixSend` em `/v2/gn/pix/:idEnvio`. A compatibilidade técnica com .NET 9 foi comprovada, mas esse descompasso de rota exige validação futura em sandbox antes de escolher a SDK para produção.

Não foi feita chamada de sandbox, autenticação ou operação financeira neste spike.

## Certificado e mTLS

- A documentação oficial da SDK requer certificado Pix no formato `.p12` para chamadas às APIs Efí.
- A SDK recebe o **caminho** do certificado; ela não expõe configuração para conteúdo em memória nem senha de certificado.
- No código-fonte da versão 2.0.4, o certificado é carregado com senha vazia por `X509Certificate2` quando um endpoint Pix é invocado.
- O certificado é configurado em `RestSharp` como certificado de cliente, caracterizando mTLS.
- O construtor não acessa o arquivo nem autentica. A validação de existência do arquivo e a autenticação acontecem na chamada dinâmica do endpoint.

Nenhum certificado, senha, `client_id`, `client_secret` ou token real foi utilizado ou versionado.

## Conclusão

**Classificação: COMPATÍVEL COM RESSALVAS.**

O pacote oficial `EfiPay` 2.0.4 restaura, compila, carrega e instancia em `net9.0` sem avisos ou conflitos críticos. A SDK disponibiliza os nomes dinâmicos necessários a envio e consulta de Pix. Contudo, ela é oficialmente testada até .NET 8 e sua rota interna de envio (`v2`) diverge da documentação atual da Efí (`v3`).

## Recomendação arquitetural

Quando o módulo financeiro for formalmente iniciado, manter a Application dependente apenas de `IPixGateway`; a Efí deve ficar em um adapter de Infrastructure. A decisão entre adapter da SDK e `HttpClient` direto deve ocorrer após sandbox validar a rota atual, autenticação mTLS, certificado e idempotência.

Se a SDK não atender ao contrato atual da Efí na sandbox, o provedor Efí não deve ser descartado: o adapter pode consumir a API oficial com `HttpClient`, sem expor a Efí ao Domain ou à Application.

## Decisões futuras preservadas, sem implementação

- Cashback nasce após `Indicacao` atingir `VistoriaConcluida`, mas não envia Pix automaticamente.
- O pagamento inicial será aprovado manualmente por administrador; o beneficiário será o `UsuarioIndicadorId`.
- Cashback e `PagamentoPix` serão conceitos separados; uma indicação poderá gerar no máximo um cashback.
- Valor do cashback, preço e comissão continuam pendentes.
- O futuro pagamento deverá guardar snapshot da chave Pix e possuir identificador idempotente próprio; falha no Pix não cancela o direito ao cashback.
- Nenhuma entidade, enum, chave Pix, migration, gateway ou endpoint foi criado por este spike.

## Reprodução

```text
dotnet restore poc/EfiNet9Compatibility/EfiNet9Compatibility.csproj
dotnet build poc/EfiNet9Compatibility/EfiNet9Compatibility.csproj --no-restore
dotnet run --project poc/EfiNet9Compatibility/EfiNet9Compatibility.csproj --no-build --no-restore
dotnet list poc/EfiNet9Compatibility/EfiNet9Compatibility.csproj package --include-transitive
dotnet list poc/EfiNet9Compatibility/EfiNet9Compatibility.csproj package --vulnerable --include-transitive
```
