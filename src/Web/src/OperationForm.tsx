import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import type { Registro } from './types'
import type { Operation, ResourceKey } from './resources'
import { optionLabel } from './resources'
import { send, ApiError } from './api'
import { ErrorBox } from './components'

export default function OperationForm({ operation, row, lists, invalidateKeys, done }: { operation: Operation; row?: Registro; lists: Partial<Record<ResourceKey, Registro[]>>; invalidateKeys: readonly (readonly string[])[]; done: () => void }) {
  const fields = operation.fields || []
  const schema = z.object(Object.fromEntries(fields.map(f => {
    let value = z.string().trim()
    if (!f.optional) value = value.min(1, 'Campo obrigatório.')
    if (f.type === 'email') value = value.email('Informe um e-mail válido.')
    if (f.name === 'senha') value = value.min(8, 'Use pelo menos oito caracteres.')
    return [f.name, f.type === 'number' ? value.refine(v => Number.isFinite(Number(v)) && Number(v) >= (f.min || 0), 'Informe um valor positivo.') : value]
  })))
  const { register, handleSubmit, formState: { errors } } = useForm<Record<string, string>>({
    resolver: zodResolver(schema),
    defaultValues: Object.fromEntries(fields.map(f => [f.name, row && f.name in row ? String(row[f.name as keyof Registro] ?? '') : ''])),
  })
  const client = useQueryClient()
  const mutation = useMutation({
    mutationFn: (values: Record<string, string>) => {
      const data = Object.fromEntries(Object.entries(values).map(([key, value]) => {
        const field = fields.find(f => f.name === key)
        return [key, field?.options || field?.type === 'number' ? Number(value) : value]
      }))
      return send(operation.path(row, values), operation.method, operation.body ? operation.body(values, row) : fields.length ? data : undefined)
    },
    onSuccess: async () => { await Promise.all(invalidateKeys.map(queryKey => client.invalidateQueries({ queryKey }))); done() },
  })
  return <form onSubmit={handleSubmit(v => mutation.mutate(v))}>
    {operation.confirm && <p>Confirma esta ação? O estado será alterado conforme as regras do sistema.</p>}
    {fields.map(f => <div className="field" key={f.name}><label htmlFor={f.name}>{f.label}</label>
      {f.source || f.options ? <select id={f.name} {...register(f.name)}>
        <option value="">Selecione</option>
        {f.options?.map((v, i) => <option key={i} value={i}>{v}</option>)}
        {f.source && lists[f.source]?.filter(v => f.onlyStatus === undefined || v.status === f.onlyStatus).map(v => <option key={v.id} value={v.id}>{optionLabel(f.source!, v, lists)}</option>)}
      </select> : <input id={f.name} type={f.type || 'text'} step={f.type === 'number' ? '0.01' : undefined} autoComplete={f.type === 'password' ? 'new-password' : 'off'} {...register(f.name)} />}
      {errors[f.name] && <span className="field-error" role="alert">{errors[f.name]?.message}</span>}
    </div>)}
    {mutation.isError && <ErrorBox message={mutation.error instanceof ApiError ? mutation.error.message : undefined} />}
    <button className="primary" disabled={mutation.isPending} type="submit">{mutation.isPending ? 'Salvando…' : operation.confirm ? 'Confirmar cancelamento' : 'Salvar'}</button>
  </form>
}
