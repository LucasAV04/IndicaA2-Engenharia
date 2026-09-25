import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { api, send } from './api'
import { ErrorBox, Modal } from './components'
import { tipoSchema, type TipoPlanta } from './precificacao'

export default function TiposPlantaPage() {
  const q = useQuery({ queryKey: ['tipos-planta'], queryFn: () => api<TipoPlanta[]>('/tipos-planta') })
  const [edit, setEdit] = useState<TipoPlanta | 'novo' | null>(null)
  const [desativar, setDesativar] = useState<TipoPlanta | null>(null)
  const client = useQueryClient()
  const invalidate = () => Promise.all([['tipos-planta'], ['dashboard']].map(queryKey => client.invalidateQueries({ queryKey })))
  const mutation = useMutation({ mutationFn: (tipo: TipoPlanta) => send('/tipos-planta/' + tipo.id + '/desativar', 'PATCH'), onSuccess: async () => { await invalidate(); setDesativar(null) } })
  return <><div className="page-heading"><div><h1>Tipos de planta</h1><p>Catálogo administrável. Nenhuma categoria comercial é pré-cadastrada.</p></div><button className="primary" onClick={() => setEdit('novo')}>Novo tipo</button></div>
    {q.isPending ? <p role="status">Carregando tipos…</p> : q.isError ? <ErrorBox retry={() => void q.refetch()} /> : !q.data?.length ? <p>Cadastre o primeiro tipo de planta antes de configurar preços.</p> : <section className="panel table-wrap"><table><caption>Catálogo de plantas</caption><thead><tr><th>Nome</th><th>Situação</th><th>Preço ativo</th><th>Ações</th></tr></thead><tbody>{q.data.map(t => <tr key={t.id}><td>{t.nome}</td><td>{t.ativo ? 'Ativo' : 'Inativo'}</td><td>{t.possuiPrecoAtivo ? 'Sim' : 'Não'}</td><td><button onClick={() => setEdit(t)}>Renomear {t.nome}</button><button disabled={!t.ativo} onClick={() => { mutation.reset(); setDesativar(t) }}>Desativar {t.nome}</button></td></tr>)}</tbody></table></section>}
    {edit && <Modal title={edit === 'novo' ? 'Cadastrar tipo' : 'Renomear tipo'} close={() => setEdit(null)}><TipoForm tipo={edit === 'novo' ? undefined : edit} done={async () => { await invalidate(); setEdit(null) }} /></Modal>}
    {desativar && <Modal title="Desativar tipo" close={() => setDesativar(null)}><p>O histórico será preservado. Desative primeiro qualquer preço ativo.</p>{mutation.isError && <ErrorBox message="Não foi possível desativar. Verifique se ainda existe preço ativo e atualize o catálogo." />}<button onClick={() => mutation.mutate(desativar)} disabled={mutation.isPending}>Confirmar desativação</button></Modal>}
  </>
}
function TipoForm({ tipo, done }: { tipo?: TipoPlanta; done: () => Promise<void> }) {
  const { register, handleSubmit, formState: { errors } } = useForm<{ nome: string }>({ resolver: zodResolver(tipoSchema), defaultValues: { nome: tipo?.nome || '' } })
  const mutation = useMutation({ mutationFn: (v: { nome: string }) => send('/tipos-planta' + (tipo ? '/' + tipo.id : ''), tipo ? 'PUT' : 'POST', v), onSuccess: done })
  return <form onSubmit={handleSubmit(v => mutation.mutate(v))}><div className="field"><label htmlFor="nomeTipo">Nome do tipo</label><input id="nomeTipo" {...register('nome')} />{errors.nome && <span role="alert">{errors.nome.message}</span>}</div>{mutation.isError && <ErrorBox message="Não foi possível salvar. O nome deve ser único no catálogo." />}<button className="primary" disabled={mutation.isPending}>Salvar tipo</button></form>
}
