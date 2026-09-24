import { useForm, useWatch } from 'react-hook-form'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError, send } from './api'
import { ErrorBox } from './components'
import { money } from './format'
import type { Registro } from './types'

export default function PagamentoVistoriaForm({ vistorias, done }: { vistorias: Registro[]; done: () => void }) {
  const { register, handleSubmit, control } = useForm<{ vistoriaId: string }>({ defaultValues: { vistoriaId: '' } })
  const id = useWatch({ control, name: 'vistoriaId' })
  const selecionada = vistorias.find(v => v.id === id && v.precificacao)
  const client = useQueryClient()
  const mutation = useMutation({
    mutationFn: ({ vistoriaId }: { vistoriaId: string }) => send('/pagamentos-vistoria', 'POST', { vistoriaId }),
    onSuccess: async () => {
      await Promise.all([['pagamentos-vistoria'], ['dashboard']].map(queryKey => client.invalidateQueries({ queryKey })))
      done()
    },
  })
  return <form onSubmit={handleSubmit(v => { if (selecionada) mutation.mutate(v) })}>
    <div className="field"><label htmlFor="vistoriaPagamento">Vistoria</label>
      <select id="vistoriaPagamento" {...register('vistoriaId', { required: true })}>
        <option value="">Selecione</option>
        {vistorias.map(v => <option key={v.id} value={v.id} disabled={!v.precificacao}>
          {v.tipoPlanta || v.id}{v.precificacao ? ` — ${money(v.precificacao.valorFinal)}` : ' — legada: indisponível'}
        </option>)}
      </select>
    </div>
    <p>O valor vem do snapshot histórico da vistoria. Vistorias legadas aguardam regularização administrativa futura.</p>
    {selecionada?.precificacao && <p aria-live="polite">Valor para conferência: <strong>{money(selecionada.precificacao.valorFinal)}</strong></p>}
    {mutation.isError && <ErrorBox message={mutation.error instanceof ApiError && [409, 422].includes(mutation.error.status)
      ? 'Não foi possível criar: a vistoria pode ser legada ou já possuir pagamento. Atualize a lista; regularização de legado não está disponível.'
      : mutation.error instanceof ApiError ? mutation.error.message : undefined} />}
    <button className="primary" disabled={!selecionada || mutation.isPending} type="submit">Salvar</button>
  </form>
}
