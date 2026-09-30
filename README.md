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

Workers Pix continuam desabilitados por padrão. O módulo de recebimento usa cobrança imediata, inbox mTLS e consulta autenticada antes de confirmar o pagamento. Não há confirmação manual, devolução automática, notificações ou implantação em produção. Validação local: 744 testes rápidos, 214 integrações MySQL e 77 testes frontend aprovados; detalhes e limites em Implementacoes.md. Nenhuma chamada Efí/OAuth/Pix real faz parte da validação local.

## Recebimento de vistoria

Com o recebimento desabilitado, pagamentos sem cobrança mantêm o cancelamento legado; qualquer histórico de cobrança impede contornar a coordenação Pix. Com o módulo habilitado, cobrança ativa exige remoção confirmada antes do cancelamento.

A página pública retoma consultas após rede/timeout/5xx com backoff de 10 a 60 segundos e respeita `Retry-After` em 429 (segundos ou data HTTP). Preserva os dados anteriores, pausa em aba oculta e encerra para link inválido ou cobrança terminal. Tokens e corpos de erro não são exibidos nem logados.

Rejeição de criação comprovada por código de validação documentado finaliza a cobrança como falha definitiva; a administração pode reemitir explicitamente após corrigir a configuração, com nova identidade e histórico preservado. Erros desconhecidos 400/422 e bloqueios 401 persistente/403 não comprovam ausência financeira: ficam auditáveis e fora da seleção automática. Não existe liberação automática desses bloqueios ao trocar credenciais; sua recuperação exige análise operacional e autorização específica. 401 permite apenas uma renovação de token por invocação e preserva txid. GET e2e 404 é ausência temporária, reagendada sem divergência automática. Dashboard separa cobranças divergentes de eventos divergentes.

O módulo exige migration 015 e configuração privada explícita. `RecebimentoPix__Habilitado=false` e `RecebimentoPix__ProcessamentoWorker__Habilitado=false` são os padrões. Quando desabilitado, não carrega certificados, não resolve provider operacional nem inicia polling.

Configuração habilitada: base Efí de homologação, credenciais/P12 externos, chave recebedora externa, CA cliente oficial externa, URL HTTPS do webhook e URL pública HTTPS. `INDICA2_COBRANCA_PIX_ENCRYPTION_KEY` deve conter Base64 de 32 bytes independentes da chave de Dados Pix. Consulte `.env.example` apenas como inventário; ele não é carregado automaticamente pelo .NET.

O webhook `/api/webhooks/efi/pix` aceita somente certificado cliente da conexão TLS validado pela CA configurada. Nesta implantação o TLS deve chegar ao Kestrel (direto ou passthrough); headers de certificado de proxy não são aceitos. Não configure bypass mTLS. O cadastro do webhook é administrativo explícito, nunca executado no startup.

A página `/pagar#TOKEN` remove o fragmento imediatamente e transmite o token somente no header `PaymentLink`, sem armazenamento persistente. O token é devolvido uma única vez na geração/rotação; no banco há somente hash e validade. QR é gerado localmente. Webhook sozinho nunca confirma pagamento: é necessária evidência obtida pela consulta autenticada e aplicada na transação financeira.
