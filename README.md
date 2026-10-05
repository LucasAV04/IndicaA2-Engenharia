# IndicA2

Backend .NET 9 / MySQL 8 e painel administrativo React + TypeScript em `src/Web`.

- [Execução do painel](src/Web/README.md)
- [Implementações e contratos vigentes](docs/Implementacoes.md)
- [Histórico de mudanças](docs/Changelog.md)
- [Exemplos HTTP seguros](src/API/API.http)

## Catálogo e precificação

O administrador cadastra Tipos de planta e publica versões na Tabela de preços. Não existem seeds de tipos ou tarifas: alguém da A2 deve fornecer os valores comerciais. Novas vistorias exigem catálogo/preço ativos e têm cálculo decimal no backend, com snapshot histórico atômico. Registros legados não são convertidos ou recalculados. A migration 014 deve ser aplicada pelo processo de administração do schema antes de usar o módulo.

A especificação técnica citada nos documentos históricos é um anexo externo fornecido pelo proprietário, não um arquivo em `sources/`. Decisões efetivamente implementadas estão em `docs/Implementacoes.md`. Alguns `.md` históricos têm formato Word e não devem ser convertidos automaticamente.

## Validação

```powershell
dotnet build IndicaA2.slnx
dotnet test IndicaA2.slnx --no-build --no-restore --filter "Category!=MySqlIntegration&FullyQualifiedName!~EfiPixSandboxIntegrationTests&FullyQualifiedName!~EfiPixTlsDiagnosticTests"
pwsh -NoProfile -File ./scripts/Invoke-MySqlIntegrationTests.ps1 -RequireMySql
```

MySQL exige conexão privada de testes em `INDICA2_TEST_MYSQL_CONNECTION`, sem Database, autorizada somente para bancos descartáveis `indicaa2_test_`. Nunca use banco de produção nem registre credenciais. O script falha fechado sem configuração/preflight. Testes externos Efí não pertencem à suíte rápida. Frontend: `npm ci`, `npm run lint`, `npm test -- --run`, `npm run build`, com TZ America/Sao_Paulo.

Workers continuam desabilitados por padrão. O módulo de recebimento usa cobrança imediata, inbox mTLS e consulta autenticada antes de confirmar o pagamento. Não há confirmação manual, devolução automática ou implantação em produção. A validação de 791 testes rápidos, 226 integrações e 93 testes frontend é o registro histórico do PR #37; resultados da nova jornada são registrados separadamente em Implementacoes.md. Nenhuma chamada Efí/OAuth/Pix real faz parte da validação local.

## Jornada automática da indicação

A confirmação interna da criação da vistoria chega à proprietária, inclusive sem indicação; o vínculo também notifica a indicadora, sem duplicar o evento da proprietária. Cashback pago notifica ambas e a administração na mesma transação financeira. A captação pública mantém somente chave idempotente/protocolo em sessionStorage por código: reload restaura o protocolo ou permite retry com a mesma chave após perda de resposta. “Cadastrar outra indicação” inicia novo formulário vazio e consentimento desmarcado. Nenhum nome/telefone é persistido no navegador.

`/indicar/:codigo` capta indicação com consentimento explícito e chave de idempotência, sem criar conta. `/minha-conta` oferece portal próprio com dados mascarados, link de indicação e notificações internas. O administrador vincula usuário e vistoria; após pagamento confirmado e realização, a conclusão cria/aprova o cashback de 20% e prepara a ordem Pix na mesma transação. Sem Dados Pix, mantém cashback disponível e registra aviso; o worker de preparação pode recuperar no próximo tick, sem chamar provider. A aplicação financeira existente marca Cashback/indicação como pagos e notifica atomicamente.

Migration 016 é aditiva e não é executada no startup. Configure `PublicWeb__BaseUrl` HTTPS e, somente quando autorizado, `CashbackPagamentoPreparacaoWorker__Habilitado` (padrão false), `IntervaloSegundos` (60) e `TamanhoLote` (20). Não existem seeds comerciais. WhatsApp, e-mail, SMS, push externo e validação jurídica do consentimento continuam pendentes para produção. Exemplos seguros e inventário de configuração estão em `src/API/API.http` e `.env.example`.

Validação local atual da jornada e correção: 423 direcionados (incluindo seis preflight), 838 testes rápidos, 106 de frontend e 255 integrações MySQL em 20 classes aprovados, sem falhos/ignorados. Build aprovado com quatro warnings preexistentes; migrations 001–016 validadas somente em banco descartável, nenhum banco novo remanescente. Resultados históricos, comandos, limites e matriz de cobertura estão em `docs/Implementacoes.md`. CI será confirmado no PR, sem habilitar workers ou provider real.

## Recebimento de vistoria

Com o recebimento desabilitado, pagamentos sem cobrança mantêm o cancelamento legado; qualquer histórico de cobrança impede contornar a coordenação Pix. Com o módulo habilitado, cobrança ativa exige remoção confirmada antes do cancelamento.

A página pública retoma consultas após rede/timeout/5xx com backoff de 10 a 60 segundos e respeita `Retry-After` em 429 (segundos ou data HTTP). Preserva os dados anteriores, pausa em aba oculta e encerra para link inválido ou cobrança terminal. Tokens e corpos de erro não são exibidos nem logados.

Rejeição de criação comprovada por código de validação documentado finaliza a cobrança como falha definitiva; a administração pode reemitir explicitamente após corrigir a configuração, com nova identidade e histórico preservado. Erros desconhecidos 400/422 e bloqueios 401 persistente/403 não comprovam ausência financeira: ficam auditáveis e fora da seleção automática. Não existe liberação automática desses bloqueios ao trocar credenciais; sua recuperação exige análise operacional e autorização específica. 401 permite apenas uma renovação de token por invocação e preserva txid. GET/PATCH cobrança reconhecem HTTP 400 com `nome=cobranca_nao_encontrada`; GET e2e reconhece `nome=pix_nao_encontrado` como ausência temporária, reagendada sem divergência. HTTP 404 é aceito defensivamente. Mensagens textuais nunca decidem estados. Com remoção solicitada, Preparada/Indeterminada consulta o mesmo txid sem novo PUT; ausência autenticada conclui remoção e cancelamento atomicamente. Dashboard separa cobranças divergentes de eventos divergentes.

O módulo exige migration 015 e configuração privada explícita. `RecebimentoPix__Habilitado=false` e `RecebimentoPix__ProcessamentoWorker__Habilitado=false` são os padrões. Quando desabilitado, não carrega certificados, não resolve provider operacional nem inicia polling.

Configuração habilitada: base Efí de homologação, credenciais/P12 externos, chave recebedora externa, CA cliente oficial externa, URL HTTPS do webhook e URL pública HTTPS. `INDICA2_COBRANCA_PIX_ENCRYPTION_KEY` deve conter Base64 de 32 bytes independentes da chave de Dados Pix. Consulte `.env.example` apenas como inventário; ele não é carregado automaticamente pelo .NET.

O webhook `/api/webhooks/efi/pix` aceita somente certificado cliente da conexão TLS validado pela CA configurada. Nesta implantação o TLS deve chegar ao Kestrel (direto ou passthrough); headers de certificado de proxy não são aceitos. Não configure bypass mTLS. O cadastro do webhook é administrativo explícito, nunca executado no startup.

A página `/pagar#TOKEN` remove o fragmento imediatamente e transmite o token somente no header `PaymentLink`, sem armazenamento persistente. O token é devolvido uma única vez na geração/rotação; no banco há somente hash e validade. QR é gerado localmente. Webhook sozinho nunca confirma pagamento: é necessária evidência obtida pela consulta autenticada e aplicada na transação financeira.
