import { randomBytes } from 'node:crypto'
import { readFile, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { test, expect, type BrowserContext, type Page } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

const origem = process.env.INDICAA2_UI_ORIGIN ?? 'https://localhost:18443'
const cliente = 'Maria Indicadora Teste'
const indicada = 'Ana Indicada Teste'
const tipo = 'Planta fictícia do aceite'
const clienteEmail = 'cliente-aceite@example.invalid'
const chavePix = 'pix-cliente-aceite@example.invalid'
const larguras = [320, 360, 390, 768, 1280, 1440]

function arquivoCliente() {
  const file = process.env.INDICAA2_UI_ADMIN_FILE
  if (!file) throw new Error('Perfil descartável obrigatório.')
  return join(dirname(file), 'cliente-ui.json')
}

async function admin() {
  const file = process.env.INDICAA2_UI_ADMIN_FILE
  if (!file) throw new Error('Execute UiSmoke com um perfil descartável.')
  const { Email, Password } = JSON.parse(await readFile(file, 'utf8')) as Record<string, string>
  if (!Email?.endsWith('@example.invalid') || !Password) throw new Error('Fixture inválida.')
  return { email: Email, senha: Password }
}

async function login(page: Page, email: string, senha: string) {
  await page.getByLabel('E-mail', { exact: true }).fill(email)
  await page.getByLabel('Senha', { exact: true }).fill(senha)
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
}

async function axe(page: Page, tela: string) {
  const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(violations.filter(v => v.impact === 'serious' || v.impact === 'critical').map(v => v.id), tela).toEqual([])
}

async function conferirMarca(page: Page, variante: string) {
  const imagem = page.getByRole('img', { name: 'A2 Engenharia & Diagnóstico', exact: true })
  await expect(imagem).toBeVisible()
  // Após redimensionar, a imagem anterior pode continuar decodificada enquanto a nova carrega.
  await expect.poll(() => imagem.evaluate((img: HTMLImageElement, variante) =>
    img.complete && img.currentSrc !== '' && new URL(img.currentSrc).pathname.includes(variante), variante),
  ).toBe(true)
  const resultado = await imagem.evaluate(async (img: HTMLImageElement) => {
    await img.decode()
    const url = new URL(img.currentSrc)
    return { mesmaOrigem: url.origin === window.location.origin, caminho: url.pathname, largura: img.naturalWidth }
  })
  expect(resultado.mesmaOrigem).toBe(true)
  expect(resultado.caminho).toContain(variante)
  expect(resultado.largura).toBeGreaterThan(0)
}

const falhasNavegador = new WeakMap<BrowserContext, string[]>()
const requestsExternas = new WeakMap<BrowserContext, string[]>()
test.beforeEach(async ({ context, page }) => {
  const falhas: string[] = []
  falhasNavegador.set(context, falhas)
  const ouvir = (tab: Page) => {
    tab.on('pageerror', () => falhas.push('erro de execução'))
    tab.on('console', mensagem => {
      if (mensagem.type() !== 'error') return
      const texto = mensagem.text().trim()
      // A política de produção bloqueia a sondagem opcional de eval do Zod, que segue sem JIT.
      const sondagemZod = texto === "Note that 'script-src' was not explicitly set, so 'default-src' is used as a fallback."
        || (texto.includes("Evaluating a string as JavaScript violates") && texto.includes("'unsafe-eval'"))
      // A jornada exercita respostas HTTP de erro deliberadas. Ativos são conferidos abaixo.
      if (!sondagemZod && !texto.startsWith('Failed to load resource:')) falhas.push('erro no console')
    })
  }
  ouvir(page)
  context.on('page', ouvir)
  context.on('response', resposta => {
    if (resposta.status() >= 400 && ['image', 'stylesheet', 'script'].includes(resposta.request().resourceType()))
      falhas.push('falha ao servir ativo')
  })
  const externas: string[] = []
  requestsExternas.set(context, externas)
  await context.route('**/*', route => {
    if (new URL(route.request().url()).origin === origem) return route.continue()
    externas.push('externa')
    return route.abort()
  })

})

test.afterEach(({ context }) => {
  expect(requestsExternas.get(context)).toHaveLength(0)
  expect(falhasNavegador.get(context)).toEqual([])
})

test('login centralizado com reflow em telas baixas e zoom', async ({ page }) => {
  test.skip(process.env.INDICAA2_UI_PHASE === 'restart')
  const tamanhos = [[1920, 991], [1920, 1080], [1366, 768], [1280, 720],
    [768, 1024], [390, 844], [360, 800], [320, 568]]
  await page.goto('/login')
  const cartao = page.locator('.login-card')
  const entrar = page.getByRole('button', { name: 'Entrar', exact: true })
  const recuperar = page.getByRole('link', { name: 'Esqueci minha senha' })
  await conferirMarca(page, 'horizontal-fundo-escuro')
  const icones = await page.locator('link[rel="icon"]').evaluateAll(elementos =>
    elementos.map(elemento => ({ href: (elemento as HTMLLinkElement).href, tamanho: elemento.getAttribute('sizes') })))
  expect(icones.map(icone => icone.tamanho)).toEqual(['32x32', '192x192'])
  for (const icone of icones) {
    expect(new URL(icone.href).origin).toBe(origem)
    const resposta = await page.request.get(icone.href)
    expect(resposta.ok()).toBe(true)
    expect(resposta.headers()['content-type']).toContain('image/png')
  }
  for (const [width, height] of tamanhos) {
    await page.setViewportSize({ width, height })
    const medidas = await cartao.evaluate(el => {
      const r = el.getBoundingClientRect()
      const raiz = document.documentElement
      return {
        horizontal: raiz.scrollWidth - raiz.clientWidth,
        vertical: raiz.scrollHeight - raiz.clientHeight,
        centroX: (r.left + r.right - raiz.clientWidth) / 2,
        centroY: (r.top + r.bottom - raiz.clientHeight) / 2,
        topo: r.top,
        colunas: getComputedStyle(el).gridTemplateColumns.split(' ').length,
      }
    })
    expect(medidas.horizontal).toBeLessThanOrEqual(0)
    expect(Math.abs(medidas.centroX)).toBeLessThanOrEqual(1)
    expect(medidas.topo).toBeGreaterThan(0)
    expect(medidas.colunas).toBe(width > 760 ? 2 : 1)
    if (width >= 768) expect(medidas.vertical).toBeLessThanOrEqual(0)
    if (medidas.vertical <= 0) expect(Math.abs(medidas.centroY)).toBeLessThanOrEqual(1)
  }

  await page.setViewportSize({ width: 640, height: 420 })
  await page.evaluate(() => { document.documentElement.style.zoom = '2' })
  const dimensoes = await page.evaluate(() => {
    const raiz = document.documentElement
    return { zoom: getComputedStyle(raiz).zoom, horizontal: raiz.scrollWidth - raiz.clientWidth, vertical: raiz.scrollHeight - raiz.clientHeight }
  })
  expect(dimensoes.zoom).toBe('2')
  expect(dimensoes.horizontal).toBeLessThanOrEqual(0)
  expect(dimensoes.vertical).toBeGreaterThan(0)
  await page.getByLabel('Senha', { exact: true }).focus()
  await page.keyboard.press('Tab')
  await expect(entrar).toBeFocused()
  await expect(entrar).toBeInViewport()
  await page.keyboard.press('Tab')
  await expect(recuperar).toBeFocused()
  await expect(recuperar).toBeInViewport()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: 'Esqueci minha senha' })).toBeVisible()
})

test('jornada operacional real e acessibilidade', async ({ page }, info) => {
  test.skip(process.env.INDICAA2_UI_PHASE === 'restart')
  test.setTimeout(130_000)
  const { email, senha } = await admin()
  const clienteSenha = randomBytes(24).toString('base64url')
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Acesse sua conta' })).toBeVisible()
  await axe(page, 'Login')
  await login(page, email, randomBytes(24).toString('base64url'))
  await expect(page.getByText('Não foi possível entrar. Confira suas credenciais e tente novamente.')).toBeVisible()
  await login(page, email, senha)
  await expect(page.getByRole('heading', { name: 'Primeiros passos' })).toBeVisible()
  const passos = page.getByRole('region', { name: 'Primeiros passos' })
  await expect(passos.getByText('Pendente', { exact: true })).toHaveCount(6)
  await conferirMarca(page, 'vertical-fundo-escuro')
  await expect(page.getByText('Administração', { exact: true })).toBeVisible()
  const desktop = page.viewportSize()!
  await page.setViewportSize({ width: 320, height: 568 })
  const menuAdmin = page.getByRole('button', { name: 'Alternar navegação' })
  await menuAdmin.focus()
  await page.keyboard.press('Enter')
  await conferirMarca(page, 'horizontal-fundo-escuro')
  await expect(page.getByRole('link', { name: 'Visão geral', exact: true })).toBeVisible()
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320)
  await page.getByRole('link', { name: 'Visão geral', exact: true }).focus()
  await page.keyboard.press('Escape')
  await expect(menuAdmin).toBeFocused()
  await page.setViewportSize(desktop)
  await axe(page, 'Visão geral')
  const documentos = await page.evaluate(() => performance.timeOrigin)

  await passos.getByRole('link', { name: 'Cadastrar ou localizar uma cliente' }).click()
  await page.getByRole('button', { name: 'Novo usuário' }).click()
  const novo = page.getByRole('dialog')
  await novo.getByLabel('Nome', { exact: true }).fill(cliente)
  await novo.getByLabel('E-mail', { exact: true }).fill(clienteEmail)
  await novo.getByLabel('Telefone', { exact: true }).fill('79999999999')
  await expect(novo.getByLabel('Telefone', { exact: true })).toHaveValue('(79) 99999-9999')
  await novo.getByLabel('Senha inicial', { exact: true }).fill(clienteSenha)
  await novo.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: clienteEmail })).toBeVisible()
  const codigo = await page.getByRole('row').filter({ hasText: clienteEmail }).getByRole('cell').nth(3).innerText()
  expect(codigo).toMatch(/^[A-Z0-9]{8}$/)

  await page.getByRole('link', { name: 'Indicações', exact: true }).click()
  await page.getByRole('button', { name: 'Nova indicação' }).click()
  await novo.getByLabel('Código de indicação').fill(codigo)
  await novo.getByLabel('Nome da pessoa indicada').fill(indicada)
  await novo.getByLabel('Telefone da pessoa indicada').fill('(79) 3333-4444')
  await novo.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: indicada })).toBeVisible()

  await page.getByRole('link', { name: 'Usuários', exact: true }).click()
  await page.getByRole('button', { name: 'Novo usuário' }).click()
  await novo.getByLabel('Nome', { exact: true }).fill(indicada)
  await novo.getByLabel('E-mail', { exact: true }).fill('ana+aceite@example.invalid')
  await novo.getByLabel('Senha inicial').fill(randomBytes(24).toString('base64url'))
  await novo.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: indicada })).toBeVisible()

  await page.getByRole('link', { name: 'Indicações', exact: true }).click()
  await page.getByRole('row').filter({ hasText: indicada }).getByRole('button', { name: 'Vincular usuário' }).click()
  await novo.getByLabel('Usuário indicado').selectOption({ label: indicada + ' • ana+aceite@example.invalid' })
  await novo.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(novo).toHaveCount(0)

  await page.getByRole('link', { name: 'Tipos de planta', exact: true }).click()
  await page.getByRole('button', { name: 'Novo tipo' }).click()
  await page.getByLabel('Nome do tipo').fill(tipo)
  await page.getByRole('button', { name: 'Salvar tipo' }).click()
  await expect(page.getByRole('row').filter({ hasText: tipo })).toBeVisible()
  await page.getByRole('link', { name: 'Tabela de preços', exact: true }).click()
  await page.getByRole('button', { name: 'Histórico e preços de ' + tipo }).click()
  await page.getByLabel('Preço por m²').fill('10')
  await page.getByRole('button', { name: 'Publicar preço' }).click()
  await expect(page.getByRole('dialog').getByText(/versão 1/)).toBeVisible()
  await page.keyboard.press('Escape')

  await page.getByRole('link', { name: 'Vistorias', exact: true }).click()
  await page.getByRole('button', { name: 'Nova vistoria' }).click()
  const vistoria = page.getByRole('dialog')
  await vistoria.getByLabel('Cliente', { exact: true }).selectOption({ label: cliente })
  await vistoria.getByLabel('Tipo de planta', { exact: true }).selectOption({ label: tipo })
  await vistoria.getByLabel('Área (m²)').fill('25')
  await vistoria.getByLabel('Data agendada').fill('2099-01-01T10:00')
  await expect(vistoria.getByText(/250,00/).first()).toBeVisible()
  await vistoria.getByRole('button', { name: 'Salvar', exact: true }).click()
  const registro = page.getByRole('row').filter({ hasText: cliente })
  await expect(registro).toContainText(/250,00/)
  await expect(registro).toContainText('Agendada')

  await page.getByRole('button', { name: 'Nova vistoria' }).click()
  await vistoria.getByLabel('Cliente', { exact: true }).selectOption({ label: indicada })
  await vistoria.getByLabel('Tipo de planta', { exact: true }).selectOption({ label: tipo })
  await vistoria.getByLabel('Área (m²)').fill('25')
  await vistoria.getByLabel('Data agendada').fill('2026-11-28T19:00')
  await expect(vistoria.getByText(/250,00/).first()).toBeVisible()
  await vistoria.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: indicada })).toContainText('28/11/2026 às 19:00')
  await page.getByRole('link', { name: 'Indicações', exact: true }).click()
  const linhaIndicada = page.getByRole('row').filter({ hasText: indicada })
  await linhaIndicada.getByRole('button', { name: 'Vincular vistoria' }).click()
  const opcaoVistoria = novo.getByRole('option', { name: new RegExp(indicada + '.*19:00') })
  const vistoriaId = await opcaoVistoria.getAttribute('value')
  if (!vistoriaId) throw new Error('Vistoria de aceite indisponível.')
  await novo.getByLabel('Vistoria').selectOption(vistoriaId)
  await novo.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(linhaIndicada).toContainText('Vistoria vinculada')

  await page.getByRole('link', { name: 'Relatórios', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Relatórios' })).toBeVisible()
  const downloadPromise = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Exportar CSV' }).click()
  const download = await downloadPromise
  expect(download.suggestedFilename()).toBe('relatorio-indicaa2.csv')
  const stream = await download.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of stream!) chunks.push(Buffer.from(chunk))
  expect(Buffer.concat(chunks).toString('utf8').trim()).toBe('id,dataUtc,status,valor')
  await download.delete()
  await axe(page, 'Relatórios')
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(documentos)

  const publica = await page.context().newPage()
  await publica.setViewportSize({ width: 360, height: 780 })
  await publica.goto('/indicar/' + codigo)
  await expect(publica.getByLabel('Nome da pessoa indicada')).toBeVisible()
  await conferirMarca(publica, 'horizontal-fundo-escuro')
  await axe(publica, 'Indicação pública móvel')
  await publica.close()

  for (const largura of [360, 768, 1280]) {
    await page.setViewportSize({ width: largura, height: 820 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(largura)
  }
  await page.setViewportSize({ width: 360, height: 780 })
  const menu = page.getByRole('button', { name: 'Alternar navegação' })
  await menu.focus()
  await page.keyboard.press('Enter')
  await page.getByRole('link', { name: 'Visão geral', exact: true }).click()
  await expect(menu).toHaveAttribute('aria-expanded', 'false')
  await expect(page.getByRole('main')).toBeFocused()
  await page.getByRole('button', { name: 'Sair', exact: true }).click()
  await page.reload()
  await expect(page).toHaveURL(/\/login$/)
  await page.goto('/relatorios')
  await expect(page).toHaveURL(/\/login$/)
  await login(page, clienteEmail, clienteSenha)
  await expect(page).toHaveURL(/\/minha-conta$/)
  await expect(page.getByRole('heading', { name: 'Olá, ' + cliente })).toBeVisible()
  const aviso = page.locator('.notificacoes li').filter({ hasText: 'A indicação de Ana foi vinculada a uma vistoria.' })
  await expect(aviso.getByText('Agendamento atual: 28/11/2026 às 19:00')).toBeVisible()
  await expect(aviso.getByText('Não lida', { exact: true })).toBeVisible()
  await aviso.getByRole('button', { name: 'Marcar como lida' }).click()
  await expect(aviso.getByText('Lida', { exact: true })).toBeVisible()
  await page.reload()
  await expect(aviso.getByText('Lida', { exact: true })).toBeVisible()
  await expect(aviso.getByRole('link', { name: 'Ver minhas indicações' })).toHaveAttribute('href', '#indicacoes')
  await expect(page.getByText('Nenhum cashback registrado.')).toBeVisible()
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write'], { origin: origem })
  await page.getByRole('button', { name: 'Copiar link' }).click()
  await expect(page.getByText('Link copiado.')).toBeVisible()
  const linkCopiado = await page.evaluate(() => navigator.clipboard.readText())
  expect(new URL(linkCopiado).pathname).toBe('/indicar/' + codigo)
  const publicoCliente = await page.context().newPage()
  await publicoCliente.goto(linkCopiado)
  await expect(publicoCliente.getByRole('heading', { name: 'Indicar uma pessoa' })).toBeVisible()
  for (const width of larguras) {
    await publicoCliente.setViewportSize({ width, height: 820 })
    await expect.poll(() => publicoCliente.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  }
  await publicoCliente.close()
  const menuCliente = page.getByRole('button', { name: 'Menu', exact: true })
  await menuCliente.focus()
  await page.keyboard.press('Enter')
  await expect(menuCliente).toHaveAttribute('aria-expanded', 'true')
  await page.keyboard.press('Tab')
  await page.keyboard.press('Escape')
  await expect(menuCliente).toBeFocused()
  await expect(menuCliente).toHaveAttribute('aria-expanded', 'false')
  const cadastrarPix = page.getByRole('button', { name: 'Cadastrar Dados Pix' })
  await cadastrarPix.click()
  const dialogoPix = page.getByRole('dialog')
  await expect(dialogoPix).toBeVisible()
  await axe(page, 'Dados Pix em diálogo')
  await page.keyboard.press('Escape')
  await expect(cadastrarPix).toBeFocused()
  await cadastrarPix.click()
  await dialogoPix.getByLabel('Tipo de chave').selectOption('2')
  await dialogoPix.getByLabel('Nova chave Pix').fill(chavePix)
  await dialogoPix.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByText('Dados Pix salvos.')).toBeVisible()
  await expect(page.getByText('••••••••', { exact: true })).toBeVisible()
  await expect(page.getByRole('main')).not.toContainText(chavePix)
  await expect(page.getByRole('heading', { name: 'Minhas notificações' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Segurança', exact: true })).toBeVisible()
  await conferirMarca(page, 'horizontal-fundo-claro')
  await axe(page, 'Minha conta móvel')
  for (const width of larguras) {
    await page.setViewportSize({ width, height: 820 })
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  }
  await page.setViewportSize({ width: 1280, height: 820 })
  await axe(page, 'Minha conta desktop')
  await page.getByRole('link', { name: 'Segurança', exact: true }).click()
  await expect(page.locator('#seguranca')).toBeFocused()
  await page.reload()
  await expect(page).toHaveURL(/\/minha-conta#seguranca$/)
  await conferirMarca(page, 'horizontal-fundo-claro')
  await expect(page.getByText('••••••••', { exact: true })).toBeVisible()
  await page.goto('/relatorios')
  await expect(page).toHaveURL(/\/minha-conta$/)
  await writeFile(arquivoCliente(), JSON.stringify({ senha: clienteSenha }), { flag: 'wx', mode: 0o600 })
  info.annotations.push({ type: 'axe', description: 'Nenhuma violação serious ou critical nas páginas verificadas.' })
})

test('dados fictícios após reiniciar a API', async ({ page }) => {
  test.skip(process.env.INDICAA2_UI_PHASE !== 'restart')
  const { email, senha } = await admin()
  await page.goto('/login')
  await login(page, email, senha)
  await expect(page.getByRole('heading', { name: 'Resumo operacional' })).toBeVisible()
  await conferirMarca(page, 'vertical-fundo-escuro')
  await page.getByRole('link', { name: 'Vistorias', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: cliente })).toContainText(/250,00/)
  await page.getByRole('button', { name: 'Sair', exact: true }).click()
  const { senha: senhaCliente } = JSON.parse(await readFile(arquivoCliente(), 'utf8')) as { senha: string }
  await login(page, clienteEmail, senhaCliente)
  await expect(page).toHaveURL(/\/minha-conta$/)
  await conferirMarca(page, 'horizontal-fundo-claro')
  await expect(page.getByText('••••••••', { exact: true })).toBeVisible()
  await expect(page.getByRole('main')).not.toContainText(chavePix)
  await page.getByRole('button', { name: 'Sair', exact: true }).click()
  await page.goto('/minha-conta')
  await expect(page).toHaveURL(/\/login$/)
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('indicaa2.session'))).toBeNull()

  await login(page, clienteEmail, senhaCliente)
  await expect(page).toHaveURL(/\/minha-conta$/)
  const outraSessao = await page.context().newPage()
  await outraSessao.goto('/login')
  await login(outraSessao, clienteEmail, senhaCliente)
  await expect(outraSessao).toHaveURL(/\/minha-conta$/)
  const novaSenha = randomBytes(24).toString('base64url')
  await page.getByLabel('Senha atual').fill(senhaCliente)
  await page.getByLabel('Nova senha').fill(novaSenha)
  await page.getByRole('button', { name: 'Alterar senha' }).click()
  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByText('Senha alterada. Entre novamente.')).toBeVisible()
  await outraSessao.reload()
  await expect(outraSessao).toHaveURL(/\/login$/)
  await outraSessao.close()
})

test('estados visuais do portal em Chromium com zoom e texto longo', async ({ page }) => {
  test.skip(process.env.INDICAA2_UI_PHASE === 'restart')
  const { senha } = JSON.parse(await readFile(arquivoCliente(), 'utf8')) as { senha: string }
  const data = '2026-01-10T12:00:00Z'
  const nome = 'Cliente fictícia ' + 'NomeExtenso'.repeat(12)
  await page.route('**/api/minha-conta', async route => {
    const resposta = await route.fetch()
    await route.fulfill({ response: resposta, json: { ...await resposta.json(), nome } })
  })
  let liberar!: () => void
  const aguardar = new Promise<void>(resolve => { liberar = resolve })
  let consulta = 0
  await page.route('**/api/minha-conta/indicacoes', async route => {
    consulta++
    if (consulta === 1) {
      await aguardar
      await route.fulfill({ status: 503, json: {} })
      return
    }
    await route.fulfill({ json: [{ nome: 'PessoaFictícia'.repeat(10), telefoneMascarado: '••••00', status: 3, criadaEm: data }] })
  })
  await page.route('**/api/minha-conta/cashbacks', route => route.fulfill({ json: [
    { valor: 13, status: 0, situacaoPix: null, criadoEm: data },
    { valor: 25, status: 1, situacaoPix: 4, criadoEm: data },
    { valor: 29, status: 2, situacaoPix: 2, criadoEm: data },
  ] }))
  await page.setViewportSize({ width: 640, height: 940 })
  await page.goto('/login')
  await login(page, clienteEmail, senha)
  await expect(page.getByRole('heading', { name: 'Olá, ' + nome })).toBeVisible()
  await expect(page.getByText('Carregando indicações…')).toBeVisible()
  liberar()
  const indicacoes = page.getByRole('region', { name: 'Minhas indicações' })
  await indicacoes.getByRole('button', { name: 'Tentar novamente' }).click({ timeout: 20_000 })
  await expect(indicacoes.getByText('Cancelada', { exact: true })).toBeVisible()
  await expect(page.getByText('Falha definitiva', { exact: true })).toBeVisible()
  await page.evaluate(() => { document.documentElement.style.zoom = '2' })
  await expect.poll(() => page.evaluate(() => {
    const raiz = document.documentElement
    return raiz.scrollWidth - raiz.clientWidth
  })).toBeLessThanOrEqual(0)
  await axe(page, 'Portal preenchido com zoom de 200%')
  await page.unrouteAll({ behavior: 'wait' })
})

test('as páginas cabem no viewport sem rolagem horizontal da página', async ({ page }) => {
  test.skip(process.env.INDICAA2_UI_PHASE === 'restart')
  test.setTimeout(95_000)
  const { email, senha } = await admin()
  await page.goto('/login')
  await login(page, email, senha)
  await expect(page.getByRole('heading', { name: 'Resumo operacional' })).toBeVisible()
  const telas = ['/', '/usuarios', '/tipos-planta', '/precos-vistoria', '/vistorias', '/indicacoes',
    '/pagamentos-vistoria', '/cobrancas-pix-vistoria', '/cashbacks', '/pagamentos-pix', '/alertas',
    '/relatorios', '/minha-conta', '/esqueci-senha', '/confirmar-email', '/redefinir-senha', '/pagar']
  for (const width of [360, 768, 1280]) {
    await page.setViewportSize({ width, height: 820 })
    for (const tela of telas) {
      await page.goto(tela, { waitUntil: 'domcontentloaded' })
      await expect(page.getByRole('main').getByRole('heading').first()).toBeVisible()
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth), {
        message: "Viewport de "+width+" px na rota "+tela,
      }).toBeLessThanOrEqual(width)
    }
  }
  await page.goto('/')
  await page.getByRole('button', { name: 'Sair', exact: true }).click()
  const telasCliente = ['/login', '/esqueci-senha', '/redefinir-senha', '/confirmar-email', '/pagar']
  for (const width of larguras) {
    await page.setViewportSize({ width, height: 820 })
    for (const tela of telasCliente) {
      await page.goto(tela, { waitUntil: 'domcontentloaded' })
      await expect(page.getByRole('main').getByRole('heading').first()).toBeVisible()
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
    }
  }
  for (const [rota, titulo] of [['/esqueci-senha', 'Recuperação'], ['/redefinir-senha', 'Redefinição'], ['/confirmar-email', 'Confirmação'], ['/pagar', 'Pagamento']]) {
    await page.goto(rota)
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await conferirMarca(page, 'horizontal-fundo-escuro')
    await axe(page, titulo)
  }
  const fragmento = randomBytes(32).toString('hex')
  await page.goto('/pagar#' + fragmento)
  await expect(page).toHaveURL(/\/pagar$/)
  await expect(page.getByRole('main')).not.toContainText(fragmento)
})
