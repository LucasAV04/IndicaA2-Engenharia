import { fireEvent, render, screen, within, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import AppErrorBoundary from '../AppErrorBoundary'
import PrimeirosPassos from '../PrimeirosPassos'
import OperationForm from '../OperationForm'
import { estadosPorRelatorio, estadosPix, estadosEmail, rotulo } from '../estados'
import type { Dashboard } from '../types'

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

const resumo: Dashboard = {
  tiposCadastrados: 0, tiposComPrecoAtivo: 0, tiposSemConfiguracao: 0, ultimaVersaoPreco: null,
  ultimoTipoPreco: null, totalUsuarios: 1, usuariosAtivos: 1, receitaConfirmada: 0, pagamentosPendentes: 0,
  cashbackDisponivel: 0, cashbackPago: 0, pixPendenteProcessando: 0, pixConcluido: 0,
  falhasPix: 0, falhasDefinitivasPix: 0, calculadoEmUtc: '2026-11-16T00:00:00Z',
  indicacoes: {}, vistorias: {}, pagamentosVistoria: {}, cashbacks: {}, pagamentosPix: {},
}

it('lista os seis passos pendentes e seus links sem clientes fictícias', () => {
  render(<MemoryRouter><PrimeirosPassos dados={resumo} cliente={false} /></MemoryRouter>)
  const section = within(screen.getByRole('region', { name: 'Primeiros passos' }))
  expect(section.getAllByText('Pendente')).toHaveLength(6)
  expect(section.getAllByRole('link').map(a => a.getAttribute('href'))).toEqual([
    '/tipos-planta', '/precos-vistoria', '/usuarios', '/vistorias', '/pagamentos-vistoria', '/indicacoes',
  ])
})

it('deriva conclusão parcial pelo catálogo', () => {
  render(<MemoryRouter><PrimeirosPassos dados={{ ...resumo, tiposCadastrados: 1 }} cliente={false} /></MemoryRouter>)
  expect(screen.getAllByText('Concluído')).toHaveLength(1)
  expect(screen.getAllByText('Pendente')).toHaveLength(5)
})

it('permite abrir ou recolher os passos quando já há registros', async () => {
  const completo = { ...resumo, tiposCadastrados: 1, tiposComPrecoAtivo: 1, vistorias: { Agendada: 1 },
    pagamentosVistoria: { Pendente: 1 }, indicacoes: { Pendente: 1 }, cashbacks: { Pago: 1 } }
  render(<MemoryRouter><PrimeirosPassos dados={completo} cliente /></MemoryRouter>)
  const sumario = screen.getByText('Orientação para começar')
  const detalhes = sumario.closest('details')!
  await userEvent.click(sumario)
  expect(detalhes.open).toBe(true)
  expect(within(detalhes).getAllByText('Concluído')).toHaveLength(6)
  await userEvent.click(sumario)
  expect(detalhes.open).toBe(false)
})

it('mantém os estados separados por domínio e qualifica estados novos', () => {
  expect(rotulo(estadosPix, 2)).toBe('Concluído')
  expect(rotulo(estadosEmail, 2)).toBe('Enviado')
  expect(rotulo(estadosPorRelatorio[2], 2)).toBe('Pago')
  expect(rotulo(estadosPorRelatorio[2], 500)).toBe('Estado não reconhecido')
})

it('preserva formulário e foca o aviso quando a API rejeita, e impede envio duplicado', async () => {
  let resolver: ((response: Response) => void) | undefined
  const fetcher = vi.fn(() => new Promise<Response>(resolve => { resolver = resolve }))
  vi.stubGlobal('fetch', fetcher)
  const done = vi.fn()
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
    <OperationForm lists={{}} invalidateKeys={[]} done={done} operation={{
      label: 'Salvar', method: 'POST', path: () => '/usuarios', fields: [{ name: 'nome', label: 'Nome' }],
    }} />
  </QueryClientProvider>)
  const nome = screen.getByLabelText('Nome')
  await userEvent.type(nome, 'Nome fictício')
  const form = screen.getByRole('button', { name: 'Salvar' }).closest('form')!
  fireEvent.submit(form)
  fireEvent.submit(form)
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(1))
  resolver?.(new Response(JSON.stringify({ detail: 'MARCADOR_FICTICIO' }), { status: 409 }))
  expect(await screen.findByRole('alert')).toHaveFocus()
  expect(nome).toHaveValue('Nome fictício')
  expect(screen.queryByText('MARCADOR_FICTICIO')).not.toBeInTheDocument()
  fireEvent.submit(form)
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2))
  resolver?.(new Response(null, { status: 204 }))
  await waitFor(() => expect(done).toHaveBeenCalledTimes(1))
})

it('error boundary oferece recuperação sem expor a exceção', () => {
  vi.spyOn(console, 'error').mockImplementation(() => undefined)
  function Quebra(): never { throw new Error('MARCADOR_FICTICIO') }
  render(<AppErrorBoundary><Quebra /></AppErrorBoundary>, { onCaughtError: () => undefined })
  expect(screen.getByRole('button', { name: 'Recarregar página' })).toBeVisible()
  expect(screen.queryByText('MARCADOR_FICTICIO')).not.toBeInTheDocument()
})
