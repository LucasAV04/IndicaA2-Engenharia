import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { send } from './api'
import { ErrorBox } from './components'

export default function GerarCobrancaForm({ pagamentoId }: { pagamentoId: string }) {
  const client = useQueryClient()
  const [copiado, setCopiado] = useState(false)
  const [erroCopia, setErroCopia] = useState(false)
  const mutation = useMutation({
    mutationFn: async () => {
      const cobranca = await send<{ id: string }>('/cobrancas-pix-vistoria/por-pagamento/' + pagamentoId, 'POST')
      return send<{ link: string }>('/cobrancas-pix-vistoria/' + cobranca.id + '/link', 'POST')
    },
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['cobrancas'] })
      void client.invalidateQueries({ queryKey: ['dashboard'] })
    },
  })
  return <section>
    <p>O valor vem do pagamento persistido. Gerar um novo link invalida o link anterior. Uma resposta indeterminada não confirma nem cancela o pagamento.</p>
    {!mutation.isSuccess && <button disabled={mutation.isPending} onClick={() => mutation.mutate()}>{mutation.isPending ? 'Gerando…' : 'Salvar'}</button>}
    {mutation.isError && <ErrorBox />}
    {mutation.data && <><p>Link disponível somente nesta janela.</p><button onClick={() => {
      setErroCopia(false)
      void navigator.clipboard.writeText(mutation.data.link).then(() => setCopiado(true)).catch(() => setErroCopia(true))
    }}>Copiar link de pagamento</button><p aria-live="polite">{copiado ? 'Link copiado.' : ''}</p>{erroCopia && <ErrorBox message="Não foi possível copiar o link." />}</>}
    <Link to={'/cobrancas-pix-vistoria?pagamento=' + pagamentoId}>Ver cobrança</Link>
  </section>
}
