import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from './api'
import { ErrorBox } from './components'
import { money, utcDate } from './format'

type Cobranca = { id: string; pagamentoVistoriaId: string; valor: number; status: number; venceEm: string | null; confirmadoEm: string | null; vistoriaId?: string; cliente?: string; divergencia?: boolean }
type Auditoria = { id: string; tipo: number; codigo: string | null; startedAt: string; finishedAt: string | null }
const estados = ['Preparada', 'Criando', 'Ativa', 'Indeterminada', 'Confirmação pendente', 'Confirmada', 'Expirada', 'Remoção pendente', 'Removida', 'Falha definitiva', 'Divergência financeira']
export default function CobrancasPage() {
  const [params] = useSearchParams()
  const query = useQuery({ queryKey: ['cobrancas'], queryFn: () => api<Cobranca[]>('/cobrancas-pix-vistoria') })
  const [link, setLink] = useState('')
  const [copiado, setCopiado] = useState(false)
  const [erroCopia, setErroCopia] = useState(false)
  const [selecionada, setSelecionada] = useState<string | null>(null)
  const [gerando, setGerando] = useState(false)
  const [pagamento, setPagamento] = useState('')
  const pagamentos = useQuery({ queryKey: ['pagamentos-vistoria'], queryFn: () => api<{ id: string; vistoriaId: string; valor: number; status: number }[]>('/pagamentos-vistoria'), enabled: gerando })
  const historico = useQuery({ queryKey: ['auditoria-cobranca', selecionada], queryFn: () => api<Auditoria[]>('/cobrancas-pix-vistoria/' + selecionada + '/auditoria'), enabled: selecionada !== null })
  const client = useQueryClient()
  const rows = query.data?.filter(c => !params.get('pagamento') || c.pagamentoVistoriaId === params.get('pagamento'))
  const mutation = useMutation({ mutationFn: (id: string) => send<{ link: string }>('/cobrancas-pix-vistoria/' + id + '/link', 'POST'), onSuccess: result => { setLink(result.link); setCopiado(false); void client.invalidateQueries({ queryKey: ['cobrancas'] }) } })
  const reemitir = useMutation({ mutationFn: (id: string) => send('/cobrancas-pix-vistoria/por-pagamento/' + id, 'POST'), onSuccess: () => { void client.invalidateQueries({ queryKey: ['cobrancas'] }) } })
  const gerar = useMutation({ mutationFn: async () => {
    const cobranca = await send<Cobranca>('/cobrancas-pix-vistoria/por-pagamento/' + pagamento, 'POST')
    return send<{ link: string }>('/cobrancas-pix-vistoria/' + cobranca.id + '/link', 'POST')
  }, onSuccess: result => { setLink(result.link); setCopiado(false); setGerando(false); void client.invalidateQueries({ queryKey: ['cobrancas'] }); void client.invalidateQueries({ queryKey: ['dashboard'] }) } })
  return <><h1>Cobranças Pix de vistoria</h1><p>Resultados indeterminados aguardam conciliação. Nenhuma confirmação manual.</p>
    <button onClick={() => setGerando(!gerando)}>Gerar cobrança e link</button>
    {gerando && <section><p>O valor vem do pagamento persistido. Gerar um link invalida qualquer link anterior desta cobrança.</p>{pagamentos.isError ? <ErrorBox /> : pagamentos.isPending ? <p role="status">Carregando pagamentos…</p> : <form onSubmit={e => { e.preventDefault(); if (pagamento) gerar.mutate() }}><label htmlFor="pagamentoCobranca">Pagamento pendente</label><select id="pagamentoCobranca" value={pagamento} onChange={e => setPagamento(e.target.value)}><option value="">Selecione</option>{pagamentos.data.filter(p => p.status === 0).map(p => <option key={p.id} value={p.id}>Vistoria {p.vistoriaId} — {money(p.valor)}</option>)}</select><button disabled={!pagamento || gerar.isPending}>Confirmar geração</button></form>}</section>}
    {(mutation.isError || reemitir.isError || gerar.isError) && <ErrorBox />}
    {query.isError ? <ErrorBox /> : query.isPending ? <p role="status">Carregando cobranças…</p> : !rows?.length ? <p>Nenhuma cobrança cadastrada. Gere uma cobrança a partir de um pagamento de vistoria pendente.</p> : <table><caption>Cobranças e recebimentos</caption><thead><tr><th>Cliente / vistoria</th><th>Valor</th><th>Estado</th><th>Vencimento</th><th>Confirmado em</th><th>Ações</th></tr></thead><tbody>{rows.map(c => <tr key={c.id}><td>{c.cliente ?? 'Cliente'} — {c.vistoriaId ?? 'Vistoria vinculada'}</td><td>{money(c.valor)}</td><td>{estados[c.status]}{c.divergencia && <strong> — Divergência financeira</strong>}</td><td>{utcDate(c.venceEm)}</td><td>{utcDate(c.confirmadoEm)}</td><td><button disabled={mutation.isPending || [6, 8, 9, 10].includes(c.status)} onClick={() => { if (window.confirm('Gerar novo link e invalidar o anterior?')) mutation.mutate(c.id) }}>Rotacionar link</button><button onClick={() => setSelecionada(c.id)}>Ver auditoria</button>{[6, 8].includes(c.status) && <button disabled={reemitir.isPending} onClick={() => { if (window.confirm('Emitir nova cobrança para este pagamento?')) reemitir.mutate(c.pagamentoVistoriaId) }}>Reemitir cobrança</button>}</td></tr>)}</tbody></table>}
    {selecionada && <section aria-label="Auditoria da cobrança"><h2>Auditoria da cobrança</h2><button onClick={() => setSelecionada(null)}>Fechar auditoria</button>{historico.isError ? <ErrorBox /> : historico.isPending ? <p role="status">Carregando auditoria…</p> : <ul>{historico.data.map(a => <li key={a.id}>{a.codigo ?? 'Em andamento'} — {utcDate(a.startedAt)} — {utcDate(a.finishedAt)}</li>)}</ul>}</section>}
    {link && <section><p>Este link só é disponibilizado agora. A rotação invalida o anterior.</p><button onClick={() => { setErroCopia(false); void navigator.clipboard.writeText(link).then(() => setCopiado(true)).catch(() => setErroCopia(true)) }}>Copiar link de pagamento</button><p aria-live="polite">{copiado ? 'Link copiado.' : ''}</p>{erroCopia && <ErrorBox message="Não foi possível copiar o link. Verifique a permissão da área de transferência." />}<button onClick={() => { setLink(''); setCopiado(false); setErroCopia(false) }}>Fechar link</button></section>}
  </>
}
