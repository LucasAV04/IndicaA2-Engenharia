import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api, send } from './api'
import { ErrorBox, Modal } from './components'
import { moneyPreciso, utcDate } from './format'
import { precoSchema, type TipoPlanta, type Preco } from './precificacao'
import { useSimulacao, ResumoCalculo } from './SimulacaoFields'

export default function PrecosPage() {
  const tipos = useQuery({ queryKey: ['tipos-planta'], queryFn: () => api<TipoPlanta[]>('/tipos-planta') })
  const precos = useQuery({ queryKey: ['precos-vistoria'], queryFn: () => api<Preco[]>('/precos-vistoria') })
  const [selecionado, setSelecionado] = useState<TipoPlanta | null>(null)
  const [tipoSim, setTipoSim] = useState(''), [area, setArea] = useState(''), [pacote, setPacote] = useState('0')
  const sim = useSimulacao(tipoSim, area, pacote)
  return <><div className="page-heading"><div><h1>Tabela de preços</h1><p>Publicar cria uma nova versão. Nenhuma tarifa comercial é fornecida pelo sistema.</p></div></div>
    {tipos.isPending || precos.isPending ? <p role="status">Carregando preços…</p> : tipos.isError || precos.isError ? <ErrorBox /> : !tipos.data?.length ? <p>Cadastre o primeiro tipo em Tipos de planta; depois configure seu preço.</p> : <section className="panel table-wrap"><table><caption>Preços por tipo</caption><thead><tr><th>Tipo</th><th>Preço/m²</th><th>Versão</th><th>Configuração</th></tr></thead><tbody>{tipos.data.map(t => { const p = precos.data?.find(x => x.tipoPlantaId === t.id); return <tr key={t.id}><td>{t.nome}{!t.ativo && ' (inativo)'}</td><td>{p ? moneyPreciso(p.precoM2) : 'Preço pendente'}</td><td>{p?.versao ?? '—'}</td><td><button onClick={() => setSelecionado(t)}>Histórico e preços de {t.nome}</button></td></tr> })}</tbody></table></section>}
    <section className="panel"><h2>Simular vistoria</h2><div className="field"><label htmlFor="simTipo">Tipo para simulação</label><select id="simTipo" value={tipoSim} onChange={e => setTipoSim(e.target.value)}><option value="">Selecione</option>{tipos.data?.filter(t => t.ativo).map(t => <option key={t.id} value={t.id}>{t.nome}</option>)}</select></div><div className="field"><label htmlFor="simArea">Área para simulação (m²)</label><input id="simArea" type="number" min="0.01" step="0.01" value={area} onChange={e => setArea(e.target.value)} /></div><div className="field"><label htmlFor="simPacote">Pacote para simulação</label><select id="simPacote" value={pacote} onChange={e => setPacote(e.target.value)}><option value="0">Simples</option><option value="1">Total</option></select></div>{sim.isFetching ? <p role="status">Calculando…</p> : sim.isError ? <ErrorBox message="Preço indisponível. Configure um preço ativo e verifique a área." /> : sim.data && <ResumoCalculo calculo={sim.data} />}</section>
    {selecionado && <Modal title={'Preços de ' + selecionado.nome} close={() => setSelecionado(null)}><Historico tipo={selecionado} /></Modal>}
  </>
}
function Historico({ tipo }: { tipo: TipoPlanta }) {
  const client = useQueryClient()
  const q = useQuery({ queryKey: ['historico-precos', tipo.id], queryFn: () => api<Preco[]>('/precos-vistoria/por-tipo/' + tipo.id + '/historico') })
  const invalidate = () => Promise.all([['precos-vistoria'], ['tipos-planta'], ['historico-precos', tipo.id], ['simulacao-vistoria', tipo.id], ['dashboard']].map(queryKey => client.invalidateQueries({ queryKey })))
  const desativar = useMutation({ mutationFn: (id: string) => send('/precos-vistoria/por-tipo/' + tipo.id + '/' + id + '/desativar', 'PATCH'), onSuccess: invalidate })
  return <>{q.isPending ? <p role="status">Carregando histórico…</p> : q.isError ? <ErrorBox retry={() => void q.refetch()} /> : <><p>Versões históricas não podem ser editadas ou excluídas.</p>{q.data?.length ? <ul>{q.data.map(p => <li key={p.id}>{p.nomeTipoPlanta} — versão {p.versao}: {moneyPreciso(p.precoM2)}/m²; Total + {p.modalidade === 0 ? moneyPreciso(p.acrescimo) : p.acrescimo + '%'} — {utcDate(p.createdAt)} — {p.ativo ? 'Ativo' : 'Inativo'} {p.ativo && <button disabled={desativar.isPending} onClick={() => desativar.mutate(p.id)}>Desativar preço</button>}</li>)}</ul> : <p>Nenhum preço cadastrado.</p>}{desativar.isError && <ErrorBox />}{tipo.ativo ? <PrecoForm tipoId={tipo.id} versao={q.data?.[0]?.versao ?? 0} done={invalidate} /> : <p>Tipo inativo: não é possível publicar preço.</p>}</>}</>
}
function PrecoForm({ tipoId, versao, done }: { tipoId: string; versao: number; done: () => Promise<unknown> }) {
  const { register, handleSubmit, reset, formState: { errors } } = useForm<{ precoM2: string; modalidade: '0' | '1'; acrescimo: string }>({ resolver: zodResolver(precoSchema), defaultValues: { modalidade: '0', acrescimo: '0', precoM2: '' } })
  const mutation = useMutation({ mutationFn: (v: { precoM2: string; modalidade: string; acrescimo: string }) => send('/precos-vistoria/por-tipo/' + tipoId, 'POST', { ...v, modalidade: Number(v.modalidade), versaoEsperada: versao }), onSuccess: async () => { await done(); reset() } })
  return <form onSubmit={handleSubmit(v => mutation.mutate(v))}><h3>{versao ? 'Publicar nova versão' : 'Cadastrar primeiro preço'}</h3><div className="field"><label htmlFor="precoM2">Preço por m²</label><input id="precoM2" type="number" step="0.0001" {...register('precoM2')} /></div><div className="field"><label htmlFor="modalidade">Acréscimo do pacote Total</label><select id="modalidade" {...register('modalidade')}><option value="0">Valor fixo</option><option value="1">Percentual</option></select></div><div className="field"><label htmlFor="acrescimo">Valor do acréscimo</label><input id="acrescimo" type="number" step="0.0001" {...register('acrescimo')} /></div>{Object.values(errors).map((e, i) => <p key={i} role="alert">{e.message}</p>)}{mutation.isError && <ErrorBox message="Não foi possível publicar. Verifique os valores e atualize o histórico em caso de conflito." />}<button className="primary" disabled={mutation.isPending}>Publicar preço</button></form>
}
