import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, send } from './api'
import { ErrorBox, Modal } from './components'
import { useEnvioUnico } from './useEnvioUnico'

const tipos = ['CPF', 'CNPJ', 'E-mail', 'Telefone', 'Aleatória']
type DadosPix = { tipoChavePix: number; chaveMascarada: string }
const caminho = '/minha-conta/dados-pix'

export function DadosPixPortal({ usuario }: { usuario: string }) {
  const chave = ['portal', usuario, 'dados-pix']
  const cache = useQueryClient()
  const dados = useQuery({ queryKey: chave, queryFn: () => api<DadosPix | null>(caminho) })
  const [editar, setEditar] = useState(false)
  const [mensagem, setMensagem] = useState('')
  const titulo = dados.data ? 'Atualizar Dados Pix' : 'Cadastrar Dados Pix'

  return <section id="dados-pix" tabIndex={-1} className="cliente-card" aria-labelledby="titulo-pix">
    <h2 id="titulo-pix">Meus Dados Pix</h2>
    <p>A chave é usada para o pagamento de cashback. Aqui você vê apenas a versão mascarada.</p>
    {dados.isPending ? <p role="status">Carregando Dados Pix…</p> : dados.isError
      ? <ErrorBox retry={() => void dados.refetch()} /> : <>
        <div className="cliente-vazio">
          <strong>{dados.data ? 'Chave cadastrada' : 'Chave ainda não cadastrada'}</strong>
          {dados.data && <p>{tipos[dados.data.tipoChavePix] ?? 'Tipo não reconhecido'} · <span>{dados.data.chaveMascarada}</span></p>}
        </div>
        <button type="button" onClick={() => { setMensagem(''); setEditar(true) }}>{titulo}</button>
      </>}
    <p aria-live="polite" className="cliente-feedback">{mensagem}</p>
    {editar && <FormularioPix titulo={titulo} fechar={() => setEditar(false)} confirmar={() => {
      setEditar(false)
      setMensagem('Dados Pix salvos.')
      void cache.invalidateQueries({ queryKey: chave, exact: true })
    }} />}
  </section>
}

function FormularioPix({ titulo, fechar, confirmar }: { titulo: string; fechar: () => void; confirmar: () => void }) {
  const enviar = useEnvioUnico()
  const [erroChave, setErroChave] = useState(false)
  const mutation = useMutation({
    mutationFn: (dados: { tipoChavePix: number; chavePix: string }) => send(caminho, 'PUT', dados),
    onSuccess: confirmar,
  })
  function salvar(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const chavePix = String(form.get('chavePix') ?? '').trim()
    if (!chavePix) {
      setErroChave(true)
      event.currentTarget.querySelector<HTMLInputElement>('input')?.focus()
      return
    }
    setErroChave(false)
    void enviar(() => mutation.mutateAsync({ tipoChavePix: Number(form.get('tipoChavePix')), chavePix }))
  }
  return <Modal title={titulo} close={() => { if (!mutation.isPending) fechar() }}>
    <p>Confira o tipo e informe a nova chave. Ao salvar, ela será usada nos pagamentos de cashback.</p>
    <form onSubmit={salvar}>
      <div className="field">
        <label htmlFor="pix-tipo">Tipo de chave</label>
        <select id="pix-tipo" name="tipoChavePix" required defaultValue="">
          <option value="">Selecione</option>{tipos.map((tipo, index) => <option key={tipo} value={index}>{tipo}</option>)}
        </select>
      </div>
      <div className="field">
        <label htmlFor="pix-chave">Nova chave Pix</label>
        <input id="pix-chave" name="chavePix" type="password" autoComplete="new-password" required aria-invalid={erroChave} aria-describedby={erroChave ? 'pix-erro' : undefined} />
        {erroChave && <p id="pix-erro" className="field-error" role="alert">Informe a nova chave.</p>}
      </div>
      {mutation.isError && <ErrorBox message={mutation.error instanceof ApiError ? mutation.error.message : 'Não foi possível salvar os Dados Pix.'} />}
      <div className="cliente-acoes">
        <button type="submit" className="primary" disabled={mutation.isPending}>{mutation.isPending ? 'Salvando…' : 'Salvar'}</button>
        <button type="button" onClick={fechar} disabled={mutation.isPending}>Cancelar</button>
      </div>
    </form>
  </Modal>
}
