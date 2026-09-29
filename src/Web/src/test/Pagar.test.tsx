import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import PagarPage from '../PagarPage'

const token = 'A'.repeat(64)
const ativa = { valor: 12.34, status: 2, venceEm: '2026-09-25T13:00:00Z', pixCopiaECola: 'CODIGO_FICTICIO', confirmada: false }
let fetchMock: ReturnType<typeof vi.fn>
beforeEach(() => {
  vi.useFakeTimers()
  window.history.replaceState(null, '', '/pagar#' + token)
  fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(ativa)))
  vi.stubGlobal('fetch', fetchMock)
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
})
afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals() })
async function montar() { let result: ReturnType<typeof render>; await act(async () => { result = render(<PagarPage />) }); return result! }

it('remove fragmento e envia token somente no header sem armazenamento', async () => {
  const storage = vi.spyOn(Storage.prototype, 'setItem')
  await montar()
  expect(window.location.hash).toBe('')
  expect(fetchMock).toHaveBeenCalledWith('/api/public/cobranca-pix-vistoria', expect.objectContaining({ headers: { Authorization: 'PaymentLink ' + token }, cache: 'no-store', referrerPolicy: 'no-referrer' }))
  expect(storage).not.toHaveBeenCalled()
  expect(document.body.textContent).not.toContain(token)
})
it('QR é SVG local e valor é exibido em BRL', async () => {
  const { container } = await montar()
  expect(container.querySelector('svg')).not.toBeNull()
  expect(container.querySelector('img')).toBeNull()
  expect(screen.getByRole('heading', { level: 2 }).textContent).toMatch(/R\$\s*12,34/)
  expect(screen.getByLabelText('Pix copia e cola')).toHaveAttribute('readonly')
  expect(fetchMock).toHaveBeenCalledTimes(1)
})
it('não consulta token inválido', async () => {
  window.history.replaceState(null, '', '/pagar#invalido')
  await montar()
  expect(fetchMock).not.toHaveBeenCalled()
  expect(window.location.hash).toBe('')
})
it('faz polling controlado e encerra ao confirmar', async () => {
  fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(ativa))).mockResolvedValueOnce(new Response(JSON.stringify({ ...ativa, status: 5, pixCopiaECola: null, confirmada: true })))
  await montar()
  await act(async () => { await vi.advanceTimersByTimeAsync(10000) })
  expect(fetchMock).toHaveBeenCalledTimes(2)
  expect(screen.getByText('Pagamento confirmado')).toBeInTheDocument()
  expect(screen.queryByLabelText('Pix copia e cola')).not.toBeInTheDocument()
  await act(async () => { await vi.advanceTimersByTimeAsync(60000) })
  expect(fetchMock).toHaveBeenCalledTimes(2)
})
it('não sobrepõe consultas enquanto resposta está pendente', async () => {
  let resolver!: (r: Response) => void
  fetchMock.mockReturnValue(new Promise<Response>(resolve => { resolver = resolve }))
  const mounted = await montar()
  try {
    await act(async () => { await vi.advanceTimersByTimeAsync(60000) })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  } finally {
    mounted.unmount()
    await act(async () => { resolver(new Response(JSON.stringify(ativa))) })
  }
})
it('ocultar página suspende consulta e desmontar cancela requisição', async () => {
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
  const mounted = await montar()
  await act(async () => { await vi.advanceTimersByTimeAsync(20000) })
  expect(fetchMock).not.toHaveBeenCalled()
  mounted.unmount()
  expect(vi.getTimerCount()).toBe(0)
})
it('erro não expõe body nem provoca retry ilimitado', async () => {
  fetchMock.mockResolvedValue(new Response('SEGREDO_FICTICIO', { status: 500 }))
  await montar()
  expect(screen.getByRole('alert')).not.toHaveTextContent('SEGREDO_FICTICIO')
  await act(async () => { await vi.advanceTimersByTimeAsync(60000) })
  expect(fetchMock).toHaveBeenCalledTimes(1)
})
it('copia código por ação explícita', async () => {
  const copy = vi.fn().mockResolvedValue(undefined)
  vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText: copy } })
  await montar()
  expect(copy).not.toHaveBeenCalled()
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Copiar código Pix' })) })
  expect(copy).toHaveBeenCalledWith('CODIGO_FICTICIO')
  expect(screen.getByText('Código copiado.')).toBeInTheDocument()
})
