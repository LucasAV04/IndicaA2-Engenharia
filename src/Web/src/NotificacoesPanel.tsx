import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, readSession, send } from './api'
import { ErrorBox } from './components'
import { utcDate } from './format'
export type Notificacao = { id: string; tipo: number; criadaEm: string; lidaEm: string | null }
const textos = ['Vistoria vinculada ou agendada.', 'Pagamento da vistoria confirmado.', 'Cadastre Dados Pix para preparar o recebimento do cashback.', 'Cashback disponível.', 'Cashback pago com sucesso.', 'Falha financeira: revisão administrativa necessária.', 'Cobrança Pix exige revisão administrativa.']
export default function NotificacoesPanel({ admin = false }: { admin?: boolean }) {
  const usuario = readSession()?.usuarioId
  const path = admin ? '/admin/jornada/notificacoes' : '/notificacoes'
  const key = ['notificacoes', usuario, admin]
  const client = useQueryClient()
  const query = useQuery({ queryKey: key, queryFn: () => api<Notificacao[]>(path) })
  const mutation = useMutation({ mutationFn: (id: string | null) => send(id ? path + '/' + id + '/lida' : path + '/lidas', 'PATCH'), onSuccess: () => { void client.invalidateQueries({ queryKey: key }); if (admin) void client.invalidateQueries({ queryKey: ['jornada', 'indicadores'] }) } })
  return <section className="panel"><h2>{admin ? 'Alertas administrativos' : 'Minhas notificações'}</h2>
    {query.isPending ? <p role="status">Carregando notificações…</p> : query.isError ? <ErrorBox retry={() => void query.refetch()} /> : <>
      <p>{query.data.filter(n => !n.lidaEm).length} não lidas nesta página</p><button disabled={mutation.isPending} onClick={() => mutation.mutate(null)}>Marcar todas como lidas</button>
      {!query.data.length && <p>Nenhuma notificação.</p>}
      <ul>{query.data.map(n => <li key={n.id}><p>{textos[n.tipo] ?? 'Atualização disponível.'} <time>{utcDate(n.criadaEm)}</time></p>{n.lidaEm ? <span>Lida</span> : <button disabled={mutation.isPending} onClick={() => mutation.mutate(n.id)}>Marcar como lida</button>}</li>)}</ul>
    </>}{mutation.isError && <ErrorBox />}
  </section>
}
