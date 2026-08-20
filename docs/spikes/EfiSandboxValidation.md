# Spike: validação Efí em homologação

**Data:** 2026-08-20
**Escopo:** validação técnica isolada. Não implementa Pix, cashback, `PagamentoPix`, gateway, webhook da aplicação ou integração de produção.

## Ambiente e segurança

- Ambiente solicitado e validado: homologação, com `EFI_SANDBOX=true`.
- Host configurado para o diagnóstico REST: `https://pix-h.api.efipay.com.br`.
- Variáveis verificadas sem revelar valores: `EFI_CLIENT_ID`, `EFI_CLIENT_SECRET`, `EFI_CERTIFICATE_PATH`, `EFI_PIX_KEY` e `EFI_SANDBOX`.
- Certificado `.p12` existente, carregável sem senha e com chave privada.
- O certificado permaneceu fora do repositório; `.gitignore` agora cobre `.p12` e `.pfx`.
- Nenhum token OAuth, segredo, certificado, chave Pix completa ou payload sensível foi registrado.

## Resultado da autenticação

1. A POC `net9.0` chamou a consulta segura da SDK `EfiPay` 2.0.4: `PixSendList`, que internamente registra `GET /v2/gn/pix/enviados`.
2. A SDK devolveu `Unauthorized` (código `401`) e não expõe a causa raiz.
3. Para diagnóstico autorizado, a POC preparou OAuth direto via `HttpClient`, com `HttpClientHandler.ClientCertificates` e TLS padrão, para `POST /oauth/token` na homologação.
4. O handshake mTLS falhou localmente no Windows antes de existir resposta HTTP, com falha de credenciais SSPI ao apresentar o certificado carregado com `EphemeralKeySet`.

Não foi desabilitada a validação TLS, não houve chamada à produção e não houve resposta OAuth, token, `expires_in` ou escopo retornado.

## Operações deliberadamente não executadas

Como OAuth/mTLS não foi concluído, esta tarefa não executou:

- consulta de webhook;
- envio Pix de confirmação;
- repetição de `idEnvio` para idempotência;
- consulta do envio;
- cenário de rejeição em homologação;
- requisição REST `v3` por `HttpClient`.

Assim, nenhum dinheiro foi movimentado, nem mesmo em homologação.

## SDK v2 e REST v3

- A SDK 2.0.4 registra `PixSend` como `PUT /v2/gn/pix/:idEnvio`.
- A documentação atual da Efí recomenda `PUT /v3/gn/pix/:idEnvio`; informa também que a rota v2 continua funcional, mas recomenda a v3 pelas melhorias de resposta.
- Não foi possível confirmar uma chamada efetiva à rota v2, porque a autenticação falhou antes da requisição de negócio.

## Conclusão

**Classificação: ADIAR EFÍ.**

Ainda não há evidência suficiente para escolher a SDK ou `HttpClient` para a futura integração. O próximo diagnóstico deve ocorrer somente após resolver o carregamento/apresentação mTLS do certificado no processo .NET do Windows e obter OAuth com sucesso. Depois disso, a comparação deverá usar o envio sandbox oficialmente documentado, com favorecido de homologação, webhook previamente verificado e identificadores idempotentes distintos por implementação.

## Decisões de produto registradas

- Cashback será elegível após `VistoriaConcluida`, sem pagamento automático inicial.
- Um Administrador aprovará manualmente o pagamento.
- Uma indicação poderá gerar no máximo um cashback.
- Falha do Pix não cancela o cashback; o pagamento deverá ser idempotente.
- Cashback e `PagamentoPix` serão conceitos separados, com snapshot futuro da chave Pix do beneficiário.
- Valor do cashback, preço e comissão continuam pendentes.

Nenhuma dessas decisões foi implementada nesta tarefa.
