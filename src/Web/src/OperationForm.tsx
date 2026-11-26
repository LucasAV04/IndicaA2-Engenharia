import { useEnvioUnico } from './useEnvioUnico'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import type { Registro } from './types'
import type { Operation, ResourceKey } from './resources'
import { optionLabel } from './resources'
import { send, ApiError } from './api'
import { ErrorBox } from './components'
import { emailSchema, limiteEmail, mascararTelefone, mensagemTelefone, telefoneCanonico } from './campos'
import TelefoneInput from './TelefoneInput'

export default function OperationForm({ operation, row, lists, invalidateKeys, done }: { operation: Operation; row?: Registro; lists: Partial<Record<ResourceKey, Registro[]>>; invalidateKeys: readonly (readonly string[])[]; done: () => void }) {
  const enviar = useEnvioUnico()
  const fields = operation.fields || []
  const original = (name: string) => String(row?.[name as keyof Registro] ?? '')
  const schema = z.object(Object.fromEntries(fields.map(f => {
    let value = z.string().trim()
    if (!f.optional) value = value.min(1, 'Campo obrigatório.')
    if (f.type === 'email') return [f.name, value.refine(v => (f.optional && !v)
      || (!!row && v === original(f.name).trim()) || emailSchema.safeParse(v).success, 'Informe um e-mail válido com até 254 caracteres.')]
    if (f.type === 'tel') return [f.name, value.refine(v => (f.optional && !v)
      || (!!row && v === mascararTelefone(original(f.name)).trim()) || telefoneCanonico(v) !== null, mensagemTelefone)]
    if (f.name === 'senha') value = value.min(8, 'Use pelo menos oito caracteres.')
    return [f.name, f.type === 'number' ? value.refine(v => Number.isFinite(Number(v)) && Number(v) >= (f.min || 0), 'Informe um valor positivo.') : value]
  })))
  const { register, handleSubmit, formState: { errors } } = useForm<Record<string, string>>({
    resolver: zodResolver(schema),
    defaultValues: Object.fromEntries(fields.map(f => [f.name, f.type === 'tel' ? mascararTelefone(original(f.name)) : original(f.name)])),
  })
  const client = useQueryClient()
  const mutation = useMutation({
    mutationFn: (values: Record<string, string>) => {
      const data = Object.fromEntries(Object.entries(values).map(([key, value]) => {
        const field = fields.find(f => f.name === key)
        if (field?.type === 'tel') {
          return [key, row && value === mascararTelefone(original(key)).trim()
            ? row[key as keyof Registro] ?? null : telefoneCanonico(value)]
        }
        if (field?.type === 'email' && row && value === original(key).trim()) return [key, original(key)]
        return [key, field?.options || field?.type === 'number' ? Number(value) : value]
      }))
      return send(operation.path(row, values), operation.method, operation.body ? operation.body(values, row) : fields.length ? data : undefined)
    },
    onSuccess: async () => { await Promise.all(invalidateKeys.map(queryKey => client.invalidateQueries({ queryKey }))); done() },
  })
  return <form noValidate onSubmit={handleSubmit(v => enviar(() => mutation.mutateAsync(v)))}>
    {operation.confirm && <p>Confirme: {operation.confirm}. Esta ação altera o registro.</p>}
    {fields.map(f => <div className="field" key={f.name}><label htmlFor={f.name}>{f.label}</label>{!f.optional && <span className="required-mark" aria-hidden="true">*</span>}
      {f.source || f.options ? <select id={f.name} required={!f.optional} aria-invalid={Boolean(errors[f.name])} aria-describedby={errors[f.name] ? f.name + '-erro' : undefined} {...register(f.name)}>
        <option value="">Selecione</option>
        {f.options?.map((v, i) => <option key={i} value={i}>{v}</option>)}
        {f.source && lists[f.source]?.filter(v => f.onlyStatus === undefined || v.status === f.onlyStatus).map(v => <option key={v.id} value={v.id}>{optionLabel(f.source!, v, lists)}</option>)}
      </select> : f.type === 'tel'
        ? <TelefoneInput id={f.name} campo={register(f.name)} obrigatorio={!f.optional} erro={!!errors[f.name]} terceiro={f.name === 'telefoneIndicada'} />
        : <input id={f.name} type={f.type || 'text'} step={f.type === 'number' ? '0.01' : undefined} autoComplete={f.type === 'password' ? 'new-password' : f.type === 'email' ? 'email' : 'off'} maxLength={f.type === 'email' ? limiteEmail : undefined} required={!f.optional} aria-invalid={Boolean(errors[f.name])} aria-describedby={errors[f.name] ? f.name + '-erro' : undefined} {...register(f.name)} />}
      {row && original(f.name) && (f.type === 'tel' && !telefoneCanonico(original(f.name))
        || f.type === 'email' && !emailSchema.safeParse(original(f.name)).success)
        && <p>Valor antigo preservado. Se precisar corrigir, informe um {f.type === 'tel' ? 'telefone brasileiro com DDD' : 'e-mail válido'}.</p>}
      {errors[f.name] && <span id={f.name + '-erro'} className="field-error" role="alert">{errors[f.name]?.message}</span>}
    </div>)}
    {mutation.isError && <ErrorBox message={mutation.error instanceof ApiError ? mutation.error.message : undefined} />}
    <button className="primary" disabled={mutation.isPending} type="submit">{mutation.isPending ? 'Salvando…' : operation.confirm ? 'Confirmar ' + operation.confirm : 'Salvar'}</button>
  </form>
}
