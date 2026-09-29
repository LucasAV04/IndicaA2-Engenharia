import { useEffect, useRef, useState } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { money, utcDate } from './format'

type Publica = { valor: number; status: number; venceEm: string | null; pixCopiaECola: string | null; confirmada: boolean }
const estados = ['Preparando cobrança', 'Preparando cobrança', 'Aguardando pagamento', 'Aguardando conciliação', 'Conferindo recebimento', 'Pagamento confirmado', 'Cobrança expirada', 'Remoção pendente', 'Cobrança removida', 'Cobrança indisponível', 'Conferência administrativa necessária']
export default function PagarPage() {
  const token = useRef<string | null>(null)
  const [data, setData] = useState<Publica | null>(null)
  const [error, setError] = useState(false)
  const [copiado, setCopiado] = useState(false)
  useEffect(() => {
    if (token.current === null) {
      token.current = window.location.hash.slice(1)
      window.history.replaceState(null, '', window.location.pathname)
    }
    if (!/^[a-fA-F0-9]{64}$/.test(token.current)) return
    let disposed = false
    let timer: ReturnType<typeof setTimeout> | undefined
    const controller = new AbortController()
    async function consultar() {
      if (disposed) return
      if (document.visibilityState === 'hidden') { timer = setTimeout(() => void consultar(), 10000); return }
      try {
        const response = await fetch('/api/public/cobranca-pix-vistoria', { headers: { Authorization: 'PaymentLink ' + token.current }, signal: controller.signal, cache: 'no-store', referrerPolicy: 'no-referrer' })
        if (!response.ok) throw new Error('indisponivel')
        const value: Publica = await response.json()
        if (disposed) return
        setData(value); setError(false)
        if ([0, 1, 2, 3, 4, 7].includes(value.status)) timer = setTimeout(() => void consultar(), 10000)
      } catch { if (!disposed) setError(true) }
    }
    void consultar()
    return () => { disposed = true; controller.abort(); clearTimeout(timer) }
  }, [])
  async function copiar() {
    if (!data?.pixCopiaECola) return
    try { await navigator.clipboard.writeText(data.pixCopiaECola); setCopiado(true) } catch { setError(true) }
  }
  return <main className="panel"><h1>Pagamento de vistoria</h1><p>A2 Engenharia & Diagnóstico</p>
    {error ? <p role="alert">Não foi possível consultar este link. Solicite orientação à A2.</p> : !data ? <p role="status">Consulte um link de pagamento válido fornecido pela A2.</p> : <>
      <h2>{money(data.valor)}</h2><p>Vencimento: {utcDate(data.venceEm)}</p><p role="status">{estados[data.status] || 'Conferência necessária'}</p>
      {data.status === 2 && data.pixCopiaECola && <><QRCodeSVG value={data.pixCopiaECola} title="QR Code Pix da vistoria" size={240} marginSize={4} />
        <label htmlFor="codigoPix">Pix copia e cola</label><textarea id="codigoPix" readOnly value={data.pixCopiaECola} />
        <button onClick={() => void copiar()}>Copiar código Pix</button><p aria-live="polite">{copiado ? 'Código copiado.' : ''}</p></>}
    </>}
  </main>
}
