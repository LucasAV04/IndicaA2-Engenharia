import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from './api'
import type { DadosPix, Registro } from './types'
import OperationForm from './OperationForm'
import { ErrorBox, Modal } from './components'
import { utcDate } from './format'

export default function DadosPixPanel({ usuario, close }: { usuario: Registro; close: () => void }) {
  const client = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [removing, setRemoving] = useState(false)
  const [error, setError] = useState(false)
  const [pending, setPending] = useState(false)
  const path = '/usuarios/' + usuario.id + '/dados-pix'
  const query = useQuery({ queryKey: ['dados-pix', usuario.id], queryFn: () => api<DadosPix | null>(path) })
  async function remove() {
    setPending(true); setError(false)
    try { await send(path, 'DELETE'); await client.invalidateQueries({ queryKey: ['dados-pix', usuario.id] }); setRemoving(false) }
    catch { setError(true) } finally { setPending(false) }
  }
  return <Modal title={'Dados Pix • ' + usuario.nome} close={close}>
    <p>A chave original nunca é recuperada pelo painel. Informe uma nova chave para substituí-la.</p>
    {query.isPending ? <p role="status">Carregando Dados Pix…</p> : query.isError ? <ErrorBox retry={() => void query.refetch()} /> : <>
      {query.data ? <div className="notice"><strong>{query.data.chaveMascarada}</strong><p>{['CPF', 'CNPJ', 'E-mail', 'Telefone', 'Aleatória'][query.data.tipoChavePix]} • Atualizado em {utcDate(query.data.updatedAt)}</p></div> : <p>Nenhuma chave Pix cadastrada.</p>}
      {!editing && <button onClick={() => setEditing(true)}>{query.data ? 'Substituir chave' : 'Cadastrar chave'}</button>}
      {query.data && !editing && <button onClick={() => setRemoving(true)}>Remover Dados Pix</button>}
      {editing && <OperationForm lists={{}} invalidateKeys={[[ 'dados-pix', usuario.id ]]} done={() => setEditing(false)} operation={{
        label: 'Salvar Dados Pix', method: 'PUT', path: () => path,
        fields: [{ name: 'tipoChavePix', label: 'Tipo de chave', options: ['CPF', 'CNPJ', 'E-mail', 'Telefone', 'Aleatória'] }, { name: 'chavePix', label: 'Nova chave Pix', type: 'password' }],
        // A chave não é senha de autenticação; validação completa fica no Domain.
      }} />}
      {removing && <div role="group" aria-label="Confirmar remoção"><p>Remover os Dados Pix deste usuário?</p><button disabled={pending} onClick={() => void remove()}>Confirmar remoção</button><button onClick={() => setRemoving(false)}>Voltar</button></div>}
      {error && <ErrorBox />}
    </>}
  </Modal>
}
