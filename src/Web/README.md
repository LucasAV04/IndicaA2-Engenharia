# IndicA2 • Administração web

React + TypeScript + Vite, React Router, TanStack Query, React Hook Form e Zod. CSS próprio, sem biblioteca visual. Node 24 LTS recomendado (mínimo 22.12). Instale com `npm ci`.

## Execução local

1. Configure privadamente a API: `ConnectionStrings__DefaultConnection`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__Key`, `Jwt__ExpirationMinutes` e `DadosPixEncryption__Key` (Base64 de 32 bytes). Não use valores de produção.
2. Execute `dotnet run --project src/API --launch-profile http` na raiz. Porta padrão: 5209.
3. Em `src/Web`, execute `npm ci` e `npm run dev`. Abra http://localhost:5173.
4. O proxy Vite encaminha `/api` à API local. `.env.example` documenta `API_PROXY_TARGET`; arquivos `.env` reais não são versionados.
5. Use uma conta administrativa já provisionada pelo processo vigente. O painel cria somente usuários comuns; não promove roles e não provisiona senha administrativa.

O perfil HTTP local deve ser usado sem porta HTTPS configurada para que o proxy receba as respostas locais. Em produção, a implantação futura deverá servir Web/API sob a mesma origem HTTPS. Não há deployment neste PR.

## Sessão e segurança

Login em `POST /api/auth/login`. Token em memória e sessionStorage somente até logout/expiração; nenhum localStorage, refresh token ou cookie fictício. 401 limpa a sessão e o cache; 403 leva a acesso negado. A API valida a policy Administrador independentemente do frontend. sessionStorage exige prevenção contínua de XSS; não há HTML arbitrário ou renderização de mensagens externas. ProblemDetails é traduzido por status para mensagens controladas, sem reproduzir detail/payload sensível.

Dados Pix retornam somente `chaveMascarada`; o formulário de substituição sempre começa vazio. Usuário existente sem chave retorna 204; usuário inexistente, 404. A chave original nunca é recuperada ou incluída nas tabelas. O envio do novo valor ocorre somente na requisição de cadastro/substituição, sem logs.

## Rotas e fluxo

- `/login`, `/acesso-negado`: sessão e autorização.
- `/`: resumo operacional, valores em BRL, datas pt-BR.
- `/usuarios`: criar/editar cliente e gerenciar Dados Pix; código gerado pelo backend.
- `/indicacoes`: criar administrativamente por código, vincular usuário/vistoria e consultar origem/consentimento. A conclusão do vínculo é automática.
- `/tipos-planta`: cadastrar, renomear e desativar tipos; sem exclusão física. Desative o preço antes do tipo.
- `/precos-vistoria`: tabela ativa, publicação de versões, histórico e simulação oficial do backend.
- `/vistorias`: cliente, catálogo ativo, área, pacote e agendamento; prévia oficial e criação com snapshot. Registros legados mantêm texto histórico. Realizar/concluir/cancelar não recalcula preço.
- `/pagamentos-vistoria`: selecionar vistoria calculada e conferir valor histórico; gerar cobrança ou solicitar cancelamento coordenado. Não existe confirmação manual. O navegador envia somente `vistoriaId`, nunca valor.
- `/cobrancas-pix-vistoria`: gerar cobrança/link, rotacionar link, consultar auditoria e solicitar reemissão explicitamente para cobrança terminal permitida.
- `/pagar#TOKEN`: página pública mínima, QR local, copia e cola e consulta periódica limitada; token removido da URL e não persistido no navegador.
- `/cashbacks`: visualizar snapshot de 20% gerado/aprovado na conclusão da vistoria; cancelamento conforme regras existentes.
- `/pagamentos-pix`: acompanhar ordem automática e cancelar quando permitido; sem criação ou envio manual.
- `/indicar/:codigo`: formulário público sem identidade da indicadora, consentimento explícito e protocolo opaco. Uma chave aleatória por envio é reutilizada nas falhas de rede; não há reenvio automático ao atualizar a página.
- `/minha-conta`: portal autenticado de usuário comum, link, listas próprias mascaradas, Dados Pix e notificações internas. Queries segregadas por usuário; logout/troca de sessão limpam cache.
- `/alertas`: notificações administrativas. Dashboard apresenta pendências de Dados Pix, ordens e falhas separadamente.

Fluxo: indicação pública ou administrativa → vínculo ao usuário/vistoria → pagamento confirmado → realização/conclusão → Cashback disponível e ordem Pix automáticos → motor Pix existente → Cashback/indicação pagos atomicamente. Sem Dados Pix, a conclusão permanece válida e o cadastro torna o cashback elegível ao próximo tick de preparação. Seletores usam registros da API; o backend é a autoridade financeira.

O formulário público não cria conta. Reset de senha, promoção de role, canais externos, envio manual e retry financeiro continuam fora do escopo. O recebimento possui webhook seguro separado, nunca cadastrado pelo frontend automaticamente. Criar ordem não significa pagá-la. Todos os workers permanecem desabilitados por padrão.

Configure `PublicWeb__BaseUrl` com a origem HTTPS pública aprovada. Links nunca usam `Host` da requisição. `CashbackPagamentoPreparacaoWorker__Habilitado=false` é o padrão; quando habilitado, intervalo de 5–3600 segundos e lote de 1–100, sequencial e sem provider. A migration 016 deve ser aplicada pelo processo administrativo, nunca pelo startup. Texto de consentimento versão `2026-10-01` precisa de revisão jurídica/comercial antes de produção; esta entrega não habilita canais externos.

## Precificação

Aplique a migration 014 pelo processo administrativo de schema antes de usar estas páginas. O sistema inicia sem tipos e sem preços: alguém da A2 precisa cadastrar o catálogo e informar tarifas comerciais. Exemplos da especificação externa não são preços reais nem seeds.

Base = preço/m² × área. Simples usa a base; Total adiciona o fixo ou a porcentagem configurada. Somente o backend calcula, em decimal, arredondando apenas o valor final para duas casas (AwayFromZero). Inputs decimais são enviados como texto decimal, sem cálculo financeiro em JavaScript. Área aceita duas casas; preço/acréscimo, quatro. Simulação não grava dados e pode mudar até a confirmação: a criação definitiva usa a versão ativa e devolve o snapshot persistido.

Novas vistorias enviam `tipoPlantaId`, nunca texto livre nem valor final. Tipos inativos não aparecem no seletor. Sem catálogo ou preço ativo, o formulário orienta e bloqueia a criação. Renomear/desativar não modifica histórico. Dashboard conta somente tipos ativos: cadastrados, com preço ativo e sem configuração; vazio retorna zero.

Recebimento Pix e webhook foram validados localmente com providers falsos e MySQL descartável; resultados e limites em Implementacoes.md. Notificações internas fazem parte da jornada; canais externos e produção permanecem fora da entrega. Os testes usam dados fictícios e não fazem chamadas Efí/OAuth/Pix reais. QR local usa `qrcode.react` 4.2.0 fixado no lockfile; nenhum código Pix é enviado a serviços de QR externos.

O pagamento deriva de `Precificacao.ValorFinal` persistido, mesmo se o preço for substituído/desativado ou o tipo renomeado depois. Vistorias legadas ficam indisponíveis no seletor e são rejeitadas pela API com 409; regularização futura está fora do escopo. Campos financeiros extras no payload retornam 400. A FK composta da migration 014 protege a identidade preço/tipo/versão do snapshot. Nenhum pagamento antigo é alterado.

## Validação

```sh
npm ci
npm run lint
npm run test -- --run
npm run build
```

Vitest/Testing Library exercitam os fluxos com mocks na fronteira fetch. Nenhum teste Web executa MySQL, Efí, OAuth ou Pix. CI separa backend rápido, frontend e MySQL 8 descartável. A listagem é integral com filtros locais neste MVP; paginação e observabilidade completa ficam para evolução. Não use o painel para produção sem a revisão e implantação específicas.
