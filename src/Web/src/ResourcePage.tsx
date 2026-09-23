import { useState } from 'react'
import { useQueries } from '@tanstack/react-query'
import { api } from './api'
import { resources, resourceDependencies, type ResourceKey, type Operation } from './resources'
import type { Registro } from './types'
import { ErrorBox, Modal } from './components'
import OperationForm from './OperationForm'
import DadosPixPanel from './DadosPixPanel'
import VistoriaForm from './VistoriaForm'

export default function ResourcePage({ resourceKey }: { resourceKey: ResourceKey }) {
  const resource = resources[resourceKey]
  const keys = [resourceKey, ...resourceDependencies[resourceKey]]
  const queries = useQueries({ queries: keys.map(key => ({ queryKey: [key], queryFn: () => api<Registro[]>('/' + key) })) })
  const lists = Object.fromEntries(keys.map((key, i) => [key, queries[i].data || []])) as Record<ResourceKey, Registro[]>
  const query = queries[keys.indexOf(resourceKey)]
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [operation, setOperation] = useState<{ op: Operation; row?: Registro } | null>(null)
  const [pixUser, setPixUser] = useState<Registro | null>(null)
  const [success, setSuccess] = useState(false)
  const selectorQueries = (operation?.op.fields || []).flatMap(f => f.source ? [queries[keys.indexOf(f.source)]] : [])
  const rows = lists[resourceKey].filter(r => (status === '' || r.status === Number(status)) &&
    resource.columns.some(c => c.value(r, lists).toLocaleLowerCase('pt-BR').includes(search.toLocaleLowerCase('pt-BR'))))
  return <>
    <div className="page-heading"><div><p className="eyebrow">Operação administrativa</p><h1>{resource.title}</h1><p>{resource.description}</p></div><button className="primary" onClick={() => setOperation({ op: resource.create })}>{resource.create.label}</button></div>
    {success && <div className="success" role="status">Operação concluída.<button aria-label="Dispensar notificação" onClick={() => setSuccess(false)}>×</button></div>}
    <section className="panel"><div className="filters"><div><label htmlFor="busca">Buscar</label><input id="busca" placeholder="Nome, código ou informação da lista" value={search} onChange={e => setSearch(e.target.value)} /></div><div><label htmlFor="status">Status</label><select id="status" value={status} onChange={e => setStatus(e.target.value)}><option value="">Todos</option>{resource.statuses.map((s, i) => s !== '—' && <option key={i} value={i}>{s}</option>)}</select></div></div>
    {query.isPending ? <p role="status" className="empty">Carregando registros…</p> : query.isError ? <ErrorBox retry={() => void query.refetch()} /> : !rows.length ? <p className="empty">Nenhum registro encontrado.</p> : <div className="table-wrap"><table><caption className="sr-only">{resource.title}</caption><thead><tr>{resource.columns.map(c => <th key={c.label}>{c.label}</th>)}<th>Status</th><th>Ações</th></tr></thead><tbody>{rows.map(r => <tr key={r.id}>{resource.columns.map(c => <td key={c.label}>{c.value(r, lists)}</td>)}<td><span className={'badge status-' + r.status}>{resource.statuses[r.status] || 'Desconhecido'}</span></td><td className="actions">{resource.actions.map(a => <button key={a.label} disabled={a.allowed && !a.allowed(r)} onClick={() => setOperation({ op: a, row: r })}>{a.label}</button>)}{resourceKey === 'usuarios' && <button onClick={() => setPixUser(r)}>Dados Pix</button>}</td></tr>)}</tbody></table></div>}
    <p className="table-count">{rows.length} registro(s)</p></section>
    {operation && <Modal title={operation.op.label} close={() => setOperation(null)}>{selectorQueries.some(q => q.isError) ? <ErrorBox message="Não foi possível carregar os seletores. Atualize a página." /> : selectorQueries.some(q => q.isPending) ? <p role="status">Carregando seletores…</p> : resourceKey === 'vistorias' && operation.op === resource.create ? <VistoriaForm usuarios={lists.usuarios} done={() => { setOperation(null); setSuccess(true) }} /> : <OperationForm operation={operation.op} row={operation.row} lists={lists} invalidateKeys={[[resourceKey], ['dashboard']]} done={() => { setOperation(null); setSuccess(true) }} />}</Modal>}
    {pixUser && <DadosPixPanel usuario={pixUser} close={() => setPixUser(null)} />}
  </>
}
