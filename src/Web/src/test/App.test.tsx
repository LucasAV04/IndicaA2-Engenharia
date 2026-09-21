import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { saveSession } from '../api'
import { date, money } from '../format'
import type { Dashboard, Registro, Sessao } from '../types'

const session: Sessao = { accessToken: 'token-ficticio', usuarioId: 'admin', nome: 'Administradora', email: 'admin@example.invalid', tipoUsuario: 2, expiresAtUtc: '2099-01-01T00:00:00Z' }
const base = { id: 'registro-1', createdAt: '2026-09-01T12:00:00Z', updatedAt: '2026-09-01T12:00:00Z', status: 0 }
const usuario: Registro = { ...base, id: 'user-1', nome: 'Maria Cliente', email: 'maria@example.invalid', codigoIndicacao: 'ABC12345', status: 1 }
const dashboard: Dashboard = { totalUsuarios: 1, usuariosAtivos: 1, receitaConfirmada: 120.50, pagamentosPendentes: 20, cashbackDisponivel: 24.10, cashbackPago: 0, pixPendenteProcessando: 24.10, pixConcluido: 0, falhasPix: 0, falhasDefinitivasPix: 0, calculadoEmUtc: base.createdAt, indicacoes: { Pendente: 1 }, vistorias: { Agendada: 1 }, pagamentosVistoria: { Confirmado: 1 }, cashbacks: { Disponivel: 1 }, pagamentosPix: { Pendente: 1 } }
let data: Record<string, unknown>
let errors: Record<string, number>
let requests: { path: string; method: string; body: unknown }[]
const response = (value: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })

beforeEach(() => {
  saveSession(null)
  data = { '/api/usuarios': [usuario], '/api/admin/dashboard': dashboard }
  errors = {}; requests = []
  vi.stubGlobal('fetch', vi.fn(async (input: string, init: RequestInit = {}) => {
    const method = init.method || 'GET'
    requests.push({ path: input, method, body: init.body ? JSON.parse(String(init.body)) : undefined })
    if (errors[input]) return response({ title: 'segredo-token-chave', detail: 'senha-privada' }, errors[input])
    if (input === '/api/auth/login') return response(session)
    if (method !== 'GET') return response(null, 204)
    return response(data[input] ?? [])
  }))
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
})
afterEach(() => vi.unstubAllGlobals())
function mount(path = '/', authenticated = true) {
  if (authenticated) saveSession(session)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}><App /></MemoryRouter></QueryClientProvider>)
}
async function fill(label: string, value: string) { await userEvent.type(screen.getByLabelText(label, { exact: true }), value) }

describe('Sessão administrativa', () => {
  it('login válido envia credenciais e abre painel de administrador', async () => {
    mount('/login', false)
    await fill('E-mail', 'admin@example.invalid'); await fill('Senha', 'ficticia')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByRole('heading', { name: 'Resumo operacional' })).toBeInTheDocument()
    expect(requests.find(r => r.path === '/api/auth/login')?.body).toEqual({ email: 'admin@example.invalid', senha: 'ficticia' })
    expect(sessionStorage.getItem('indicaa2.session')).toContain('token-ficticio')
    expect(localStorage.length).toBe(0)
  })
  it('login inválido não cria sessão nem mostra conteúdo do provider', async () => {
    errors['/api/auth/login'] = 401; mount('/login', false)
    await fill('E-mail', 'admin@example.invalid'); await fill('Senha', 'incorreta')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível entrar')
    expect(sessionStorage.getItem('indicaa2.session')).toBeNull()
    expect(document.body).not.toHaveTextContent('senha-privada')
  })
  it('usuário comum autenticado vai para acesso negado', async () => {
    saveSession({ ...session, tipoUsuario: 1 }); mount('/', false)
    expect(await screen.findByRole('heading', { name: 'Acesso negado' })).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })
  it('rota protegida sem sessão volta ao login', () => {
    mount('/cashbacks', false)
    expect(screen.getByRole('heading', { name: 'Acesse a administração' })).toBeInTheDocument()
  })
  it('logout remove sessão e dados do painel', async () => {
    mount(); await screen.findByText('R$ 120,50')
    await userEvent.click(screen.getByRole('button', { name: 'Sair' }))
    expect(await screen.findByRole('button', { name: 'Entrar' })).toBeInTheDocument()
    expect(sessionStorage.getItem('indicaa2.session')).toBeNull()
    expect(screen.queryByText('R$ 120,50')).not.toBeInTheDocument()
  })
  it('401 tardio encerra sessão', async () => {
    mount(); await screen.findByText('R$ 120,50')
    errors['/api/admin/dashboard'] = 401
    await userEvent.click(screen.getByRole('button', { name: 'Atualizar resumo' }))
    expect(await screen.findByRole('button', { name: 'Entrar' })).toBeInTheDocument()
    expect(sessionStorage.getItem('indicaa2.session')).toBeNull()
  })
  it('403 leva ao acesso negado sem expor a resposta', async () => {
    errors['/api/admin/dashboard'] = 403; mount()
    expect(await screen.findByRole('heading', { name: 'Acesso negado' })).toBeInTheDocument()
    expect(document.body).not.toHaveTextContent('segredo-token-chave')
  })
})

describe('Resumo operacional', () => {
  it('apresenta carregamento enquanto HTTP aguarda', async () => {
    let resolve!: (response: Response) => void
    vi.mocked(fetch).mockImplementationOnce(() => new Promise<Response>(r => { resolve = r }))
    mount()
    expect(screen.getByText('Carregando resumo…')).toBeInTheDocument()
    await act(async () => resolve(response(dashboard)))
    expect(await screen.findByText('R$ 120,50')).toBeInTheDocument()
  })
  it('exibe totais e status obtidos da API', async () => {
    mount()
    expect(await screen.findByText('R$ 120,50')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Pix por status' })).toBeInTheDocument()
  })
  it('exibe estado vazio com valores zero reais', async () => {
    data['/api/admin/dashboard'] = { ...dashboard, totalUsuarios: 0, receitaConfirmada: 0 }
    mount(); expect(await screen.findByText(/Ainda não há usuários/)).toBeInTheDocument()
  })
  it('exibe erro sem renderizar ProblemDetails sensível', async () => {
    errors['/api/admin/dashboard'] = 500; mount()
    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível carregar')
    expect(document.body).not.toHaveTextContent('segredo-token-chave')
  })
})

describe('Fluxos administrativos via HTTP', () => {
  it('lista usuários e filtra por código e status', async () => {
    data['/api/usuarios'] = [usuario, { ...usuario, id: 'user-2', nome: 'João', codigoIndicacao: 'OUTRO123', status: 2 }]
    mount('/usuarios'); await screen.findByText('Maria Cliente')
    await fill('Buscar', 'ABC12345')
    expect(screen.queryByText('João')).not.toBeInTheDocument()
    await userEvent.selectOptions(screen.getByLabelText('Status'), '2')
    expect(screen.getByText('Nenhum registro encontrado.')).toBeInTheDocument()
  })
  it('edita usuário sem campo de senha ou role e envia o id da rota', async () => {
    mount('/usuarios'); await screen.findByText('Maria Cliente')
    await userEvent.click(screen.getByRole('button', { name: 'Editar' }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).queryByLabelText(/senha|role/i)).not.toBeInTheDocument()
    await userEvent.clear(screen.getByLabelText('Nome')); await fill('Nome', 'Maria Atualizada')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Salvar' }))
    await waitFor(() => expect(requests.some(r => r.method === 'PUT' && r.path === '/api/usuarios/user-1')).toBe(true))
    expect(requests.find(r => r.method === 'PUT')?.body).toMatchObject({ id: 'user-1', nome: 'Maria Atualizada' })
  })
  it('Dados Pix mostram só máscara e substituição começa vazia', async () => {
    data['/api/usuarios/user-1/dados-pix'] = { id: 'dp', usuarioId: 'user-1', tipoChavePix: 0, chaveMascarada: '••••8909', updatedAt: base.updatedAt }
    mount('/usuarios'); await screen.findByText('Maria Cliente')
    await userEvent.click(screen.getByRole('button', { name: 'Dados Pix' }))
    expect(await screen.findByText('••••8909')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Substituir chave' }))
    expect(screen.getByLabelText('Nova chave Pix')).toHaveValue('')
    expect(screen.getByLabelText('Nova chave Pix')).toHaveAttribute('type', 'password')
  })
  it('cria indicação por código, sem digitar GUID', async () => {
    mount('/indicacoes')
    await userEvent.click(screen.getByRole('button', { name: 'Nova indicação' }))
    await fill('Código de indicação', 'ABC12345'); await fill('Nome da pessoa indicada', 'Pessoa Fictícia'); await fill('Telefone da pessoa indicada', '11999999999')
    await userEvent.click(screen.getByRole('button', { name: 'Salvar' }))
    await waitFor(() => expect(requests.find(r => r.method === 'POST')?.path).toBe('/api/indicacoes/por-codigo'))
    expect(requests.find(r => r.method === 'POST')?.body).toMatchObject({ codigoIndicacao: 'ABC12345' })
  })
  it.each([
    ['/vistorias', { ...base, usuarioId: 'user-1', pacote: 0, areaM2: 50, tipoPlanta: 'Apartamento' }, 'Realizar', '/api/vistorias/registro-1/realizar'],
    ['/pagamentos-vistoria', { ...base, valor: 120.50 }, 'Confirmar pagamento', '/api/pagamentos-vistoria/registro-1/confirmar'],
    ['/cashbacks', { ...base, valor: 24.10, valorTotalPago: 120.50, percentual: 0.2, usuarioIndicadorId: 'user-1' }, 'Aprovar', '/api/cashbacks/registro-1/aprovar'],
  ])('%s executa transição permitida', async (path, row, button, target) => {
    data['/api' + path] = [row]; mount(path)
    await userEvent.click(await screen.findByRole('button', { name: button }))
    await userEvent.click(screen.getByRole('button', { name: 'Salvar' }))
    await waitFor(() => expect(requests.some(r => r.path === target && r.method === 'PATCH')).toBe(true))
  })
  it('cria pagamento Pix por cashback disponível, sem chave ou envio', async () => {
    data['/api/cashbacks'] = [{ ...base, id: 'cashback-1', status: 1, valor: 20, usuarioIndicadorId: 'user-1' }]
    mount('/pagamentos-pix')
    await userEvent.click(screen.getByRole('button', { name: 'Criar pagamento Pix' }))
    await waitFor(() => expect(screen.getByRole('option', { name: /R\$\s20,00/ })).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Cashback disponível'), 'cashback-1')
    await userEvent.click(screen.getByRole('button', { name: 'Salvar' }))
    await waitFor(() => expect(requests.some(r => r.path === '/api/pagamentos-pix/por-cashback/cashback-1' && r.method === 'POST')).toBe(true))
    expect(document.body).not.toHaveTextContent('Processar agora')
  })
  it('desabilita cancelamento incompatível e exige confirmação para o permitido', async () => {
    data['/api/vistorias'] = [{ ...base, status: 1, usuarioId: 'user-1' }]
    mount('/vistorias')
    expect(await screen.findByRole('button', { name: 'Cancelar' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Realizar' })).toBeDisabled()
  })
  it('cancelamento exige confirmação antes da requisição', async () => {
    data['/api/pagamentos-vistoria'] = [{ ...base, valor: 20 }]; mount('/pagamentos-vistoria')
    await userEvent.click(await screen.findByRole('button', { name: 'Cancelar' }))
    expect(requests.filter(r => r.method === 'PATCH')).toHaveLength(0)
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar cancelamento' }))
    await waitFor(() => expect(requests.filter(r => r.method === 'PATCH')).toHaveLength(1))
  })
  it('erro de comando exibe mensagem segura de ProblemDetails', async () => {
    data['/api/cashbacks'] = [{ ...base, valor: 20 }]; errors['/api/cashbacks/registro-1/aprovar'] = 422
    mount('/cashbacks'); await userEvent.click(await screen.findByRole('button', { name: 'Aprovar' }))
    await userEvent.click(screen.getByRole('button', { name: 'Salvar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('A operação não é permitida')
    expect(document.body).not.toHaveTextContent('senha-privada')
  })
  it('formata moeda, percentual e datas em pt-BR', () => {
    expect(money(1234.56)).toMatch(/1\.234,56/)
    expect(date('2026-09-01T12:00:00Z')).toMatch(/01\/09\/2026/)
  })
  it('formulário valida campos obrigatórios antes de enviar', async () => {
    mount('/usuarios'); await userEvent.click(screen.getByRole('button', { name: 'Novo usuário' }))
    fireEvent.submit(screen.getByRole('button', { name: 'Salvar' }).closest('form')!)
    expect(await screen.findAllByRole('alert')).not.toHaveLength(0)
    expect(requests.some(r => r.method === 'POST')).toBe(false)
  })
})
