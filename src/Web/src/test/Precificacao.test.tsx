import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import App from '../App'
import { saveSession } from '../api'
import { precoSchema, tipoSchema, areaSchema } from '../precificacao'

const tipoId = '06becc5d-69fd-4676-a0c0-2463e5f7e434'
const usuarioId = 'ca9a2547-d31f-4462-828b-241eae947831'
const tipo = { id: tipoId, nome: 'Planta fictícia', ativo: true, possuiPrecoAtivo: true }
const preco = { id: 'preco1', tipoPlantaId: tipoId, nomeTipoPlanta: tipo.nome, precoM2: 2.1234, modalidade: 0, acrescimo: 5, versao: 1, ativo: true, createdAt: '2026-09-23T12:00:00Z' }
let data: Record<string, unknown>, errors: Record<string, number>, calls: { path: string; method: string; body: Record<string, unknown> }[]
beforeEach(() => {
  saveSession({ accessToken: 'token-ficticio', usuarioId, nome: 'Admin', email: 'admin@example.invalid', tipoUsuario: 2, expiresAtUtc: '2099-01-01T00:00:00Z' })
  data = { '/api/tipos-planta': [tipo], '/api/precos-vistoria': [preco], [`/api/precos-vistoria/por-tipo/${tipoId}/historico`]: [preco], '/api/usuarios': [{ id: usuarioId, nome: 'Cliente fictício' }], '/api/vistorias': [] }
  errors = {}; calls = []
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => {
    const method = init.method || 'GET', body = init.body ? JSON.parse(String(init.body)) : {}
    calls.push({ path, method, body })
    if (errors[path]) return new Response(JSON.stringify({ detail: 'SEGREDO_FICTICIO' }), { status: errors[path] })
    if (path.endsWith('/simular')) return new Response(JSON.stringify({ ...preco, precoId: preco.id, valorBase: 21.234, valorFinal: 26.23, simulacao: true }))
    return new Response(method === 'GET' ? JSON.stringify(data[path] ?? []) : null, { status: method === 'GET' ? 200 : 204 })
  }))
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
})
it('preserva quatro casas do preço na exibição', async () => {
  mount('/precos-vistoria')
  expect(await screen.findByText(/2,1234/)).toBeInTheDocument()
})

it('campos do catálogo são acessíveis por teclado e label', async () => {
  mount('/tipos-planta')
  await userEvent.click(screen.getByRole('button', { name: 'Novo tipo' }))
  screen.getByLabelText('Nome do tipo').focus()
  await userEvent.keyboard('Nome fictício')
  expect(screen.getByLabelText('Nome do tipo')).toHaveValue('Nome fictício')
  await userEvent.tab()
  expect(screen.getByRole('button', { name: 'Salvar tipo' })).toHaveFocus()
})

afterEach(() => vi.unstubAllGlobals())
function mount(path: string) { const client = new QueryClient({ defaultOptions: { queries: { retry: false } } }); render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}><App /></MemoryRouter></QueryClientProvider>); return client }
async function historico() { mount('/precos-vistoria'); await userEvent.click(await screen.findByRole('button', { name: /Histórico e preços/ })); await screen.findByRole('heading', { name: 'Publicar nova versão' }) }

it('rotas e menus de catálogo e preços', async () => { mount('/tipos-planta'); expect(await screen.findByText(tipo.nome)).toBeInTheDocument(); expect(screen.getByRole('link', { name: 'Tabela de preços' })).toHaveAttribute('href', '/precos-vistoria') })
it('catálogo vazio orienta cadastro', async () => { data['/api/tipos-planta'] = []; mount('/tipos-planta'); expect(await screen.findByText(/Cadastre o primeiro tipo/)).toBeInTheDocument() })
it('cadastra tipo com nome normalizado', async () => { mount('/tipos-planta'); await userEvent.click(screen.getByRole('button', { name: 'Novo tipo' })); await userEvent.type(screen.getByLabelText('Nome do tipo'), '  Novo fictício  '); await userEvent.click(screen.getByRole('button', { name: 'Salvar tipo' })); await waitFor(() => expect(calls.some(c => c.method === 'POST' && c.body.nome === 'Novo fictício')).toBe(true)) })
it('renomeia sem exclusão física', async () => { mount('/tipos-planta'); await userEvent.click(await screen.findByRole('button', { name: /Renomear/ })); const input = screen.getByLabelText('Nome do tipo'); await userEvent.clear(input); await userEvent.type(input, 'Renomeado'); await userEvent.click(screen.getByRole('button', { name: 'Salvar tipo' })); await waitFor(() => expect(calls.some(c => c.method === 'PUT')).toBe(true)); expect(calls.some(c => c.method === 'DELETE')).toBe(false) })
it('conflito ao desativar orienta remover preço ativo', async () => { errors[`/api/tipos-planta/${tipoId}/desativar`] = 409; mount('/tipos-planta'); await userEvent.click(await screen.findByRole('button', { name: /Desativar Planta/ })); await userEvent.click(screen.getByRole('button', { name: 'Confirmar desativação' })); expect(await screen.findByRole('alert')).toHaveTextContent('preço ativo'); expect(document.body).not.toHaveTextContent('SEGREDO_FICTICIO') })
it('mostra lista ativa e histórico imutável', async () => { await historico(); expect(screen.getByText(/Versões históricas não podem/)).toBeInTheDocument(); expect(screen.getByText(/versão 1:/)).toBeInTheDocument() })
it('cadastro primeiro preço não usa tarifa pré-preenchida', async () => { data[`/api/precos-vistoria/por-tipo/${tipoId}/historico`] = []; data['/api/precos-vistoria'] = []; mount('/precos-vistoria'); await userEvent.click(await screen.findByRole('button', { name: /Histórico e preços/ })); expect(await screen.findByRole('heading', { name: 'Cadastrar primeiro preço' })).toBeInTheDocument(); expect(screen.getByLabelText('Preço por m²')).toHaveValue(null) })
it.each(['0', '1'])('publica nova versão com modalidade %s', async modalidade => { await historico(); await userEvent.type(screen.getByLabelText('Preço por m²'), '3.1234'); await userEvent.selectOptions(screen.getByLabelText('Acréscimo do pacote Total'), modalidade); await userEvent.click(screen.getByRole('button', { name: 'Publicar preço' })); await waitFor(() => expect(calls.some(c => c.method === 'POST' && c.body.versaoEsperada === 1 && c.body.precoM2 === '3.1234' && c.body.modalidade === Number(modalidade))).toBe(true)) })
it('tipo inativo não oferece publicar preço', async () => { data['/api/tipos-planta'] = [{ ...tipo, ativo: false }]; mount('/precos-vistoria'); await userEvent.click(await screen.findByRole('button', { name: /Histórico e preços/ })); expect(await screen.findByText(/Tipo inativo:/)).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Publicar preço' })).not.toBeInTheDocument() })
it('desativa preço preservando histórico', async () => { await historico(); await userEvent.click(screen.getByRole('button', { name: 'Desativar preço' })); await waitFor(() => expect(calls.some(c => c.method === 'PATCH' && c.path.endsWith('/preco1/desativar'))).toBe(true)) })
it('valida Zod sem cálculo financeiro', () => { expect(tipoSchema.safeParse({ nome: ' ' }).success).toBe(false); expect(tipoSchema.safeParse({ nome: 'x'.repeat(151) }).success).toBe(false); expect(precoSchema.safeParse({ precoM2: '0', modalidade: '0', acrescimo: '0' }).success).toBe(false); expect(precoSchema.safeParse({ precoM2: '1', modalidade: '1', acrescimo: '10001' }).success).toBe(false); expect(areaSchema.safeParse('-1').success).toBe(false) })
it('simula usando backend e exibe BRL', async () => { mount('/precos-vistoria'); await screen.findByRole('button', { name: /Histórico e preços/ }); await userEvent.selectOptions(screen.getByLabelText('Tipo para simulação'), tipoId); fireEvent.change(screen.getByLabelText('Área para simulação (m²)'), { target: { value: '10' } }); expect(await screen.findByText(/Simulação do backend/)).toBeInTheDocument(); expect(screen.getByText(/26,23/)).toBeInTheDocument(); expect(calls.filter(c => c.method !== 'GET')).toHaveLength(1) })
it('sem preço apresenta erro seguro', async () => { errors['/api/precos-vistoria/simular'] = 409; mount('/precos-vistoria'); await screen.findByRole('button', { name: /Histórico e preços/ }); await userEvent.selectOptions(screen.getByLabelText('Tipo para simulação'), tipoId); fireEvent.change(screen.getByLabelText('Área para simulação (m²)'), { target: { value: '10' } }); expect(await screen.findByRole('alert')).toHaveTextContent('Preço indisponível'); expect(document.body).not.toHaveTextContent('SEGREDO_FICTICIO') })
it('nova vistoria sem campo manual e com catálogo ativo', async () => { data['/api/tipos-planta'] = [tipo, { ...tipo, id: usuarioId, nome: 'Inativo', ativo: false }]; mount('/vistorias'); await userEvent.click(screen.getByRole('button', { name: 'Nova vistoria' })); await screen.findByLabelText('Tipo de planta'); expect(screen.queryByRole('option', { name: 'Inativo' })).not.toBeInTheDocument(); expect(screen.queryByLabelText(/Valor final/)).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Salvar' })).toBeDisabled() })
it('vistoria envia ID área pacote sem valores calculados e preserva horário', async () => { mount('/vistorias'); await userEvent.click(screen.getByRole('button', { name: 'Nova vistoria' })); await screen.findByLabelText('Tipo de planta'); await userEvent.selectOptions(screen.getByLabelText('Cliente'), usuarioId); await userEvent.selectOptions(screen.getByLabelText('Tipo de planta'), tipoId); fireEvent.change(screen.getByLabelText('Área (m²)'), { target: { value: '10.25' } }); fireEvent.change(screen.getByLabelText('Data agendada'), { target: { value: '2026-09-23T14:30' } }); await waitFor(() => expect(screen.getByRole('button', { name: 'Salvar' })).toBeEnabled()); await userEvent.click(screen.getByRole('button', { name: 'Salvar' })); await waitFor(() => expect(calls.find(c => c.path === '/api/vistorias' && c.method === 'POST')?.body).toEqual({ usuarioId, tipoPlantaId: tipoId, areaM2: '10.25', pacote: 0, dataAgendada: '2026-09-23T14:30' })) })
it('mudança de área refaz somente simulação', async () => { mount('/precos-vistoria'); await screen.findByRole('button', { name: /Histórico e preços/ }); await userEvent.selectOptions(screen.getByLabelText('Tipo para simulação'), tipoId); fireEvent.change(screen.getByLabelText('Área para simulação (m²)'), { target: { value: '10' } }); await screen.findByText(/Simulação do backend/); const antes = calls.length; fireEvent.change(screen.getByLabelText('Área para simulação (m²)'), { target: { value: '11' } }); await waitFor(() => expect(calls.length).toBeGreaterThan(antes)); expect(calls.slice(antes).every(c => c.path.endsWith('/simular'))).toBe(true) })
it('falha do histórico não bloqueia catálogo ou simulação', async () => { errors[`/api/precos-vistoria/por-tipo/${tipoId}/historico`] = 500; mount('/precos-vistoria'); await userEvent.click(await screen.findByRole('button', { name: /Histórico e preços/ })); expect(await screen.findByRole('alert')).toBeInTheDocument(); expect(screen.getByLabelText('Área para simulação (m²)')).toBeInTheDocument() })
it('cache de módulos independentes não é invalidado', async () => { const client = mount('/tipos-planta'); const spy = vi.spyOn(client, 'invalidateQueries'); await userEvent.click(screen.getByRole('button', { name: 'Novo tipo' })); await userEvent.type(screen.getByLabelText('Nome do tipo'), 'Novo'); await userEvent.click(screen.getByRole('button', { name: 'Salvar tipo' })); await waitFor(() => expect(spy).toHaveBeenCalledTimes(2)); expect(spy.mock.calls.map(c => c[0]?.queryKey)).toEqual([['tipos-planta'], ['dashboard']]) })
it('catálogo vazio bloqueia criação com orientação', async () => { data['/api/tipos-planta'] = []; mount('/vistorias'); await userEvent.click(screen.getByRole('button', { name: 'Nova vistoria' })); expect(await screen.findByText(/Cadastre o primeiro tipo ativo/)).toBeInTheDocument() })
