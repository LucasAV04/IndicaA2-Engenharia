import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api, send } from './api'
import { simulacaoSchema, type TipoPlanta } from './precificacao'
import { useSimulacao, ResumoCalculo } from './SimulacaoFields'
import { ErrorBox } from './components'
import type { Registro } from './types'

const schema = simulacaoSchema.extend({ usuarioId: z.string().uuid('Selecione um cliente.'), dataAgendada: z.string().min(16, 'Informe o agendamento.') })
export default function VistoriaForm({ usuarios, done }: { usuarios: Registro[]; done: () => void }) {
  const tipos = useQuery({ queryKey: ['tipos-planta'], queryFn: () => api<TipoPlanta[]>('/tipos-planta') })
  const { register, handleSubmit, control, formState: { errors } } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema), defaultValues: { tipoPlantaId: '', areaM2: '', pacote: '0', usuarioId: '', dataAgendada: '' } })
  const [tipoId, area, pacote] = useWatch({ control, name: ['tipoPlantaId', 'areaM2', 'pacote'] })
  const sim = useSimulacao(tipoId, area, pacote)
  const client = useQueryClient()
  const mutation = useMutation({ mutationFn: (v: z.infer<typeof schema>) => send('/vistorias', 'POST', { ...v, pacote: Number(v.pacote) }), onSuccess: async () => { await Promise.all([['vistorias'], ['dashboard']].map(queryKey => client.invalidateQueries({ queryKey }))); done() } })
  if (tipos.isPending) return <p role="status">Carregando catálogo…</p>
  if (tipos.isError) return <ErrorBox />
  const ativos = tipos.data?.filter(t => t.ativo) || []
  if (!ativos.length) return <p>Cadastre o primeiro tipo ativo em Tipos de planta antes de criar uma vistoria.</p>
  return <form onSubmit={handleSubmit(v => mutation.mutate(v))}><div className="field"><label htmlFor="usuarioId">Cliente</label><select id="usuarioId" {...register('usuarioId')}><option value="">Selecione</option>{usuarios.map(u => <option key={u.id} value={u.id}>{u.nome}</option>)}</select></div><div className="field"><label htmlFor="tipoPlantaId">Tipo de planta</label><select id="tipoPlantaId" {...register('tipoPlantaId')}><option value="">Selecione</option>{ativos.map(t => <option key={t.id} value={t.id}>{t.nome}</option>)}</select></div><div className="field"><label htmlFor="areaM2">Área (m²)</label><input id="areaM2" type="number" step="0.01" {...register('areaM2')} /></div><div className="field"><label htmlFor="pacote">Pacote</label><select id="pacote" {...register('pacote')}><option value="0">Simples</option><option value="1">Total</option></select></div><div className="field"><label htmlFor="dataAgendada">Data agendada</label><input id="dataAgendada" type="datetime-local" {...register('dataAgendada')} /></div>{Object.values(errors).map((e, i) => <p key={i} role="alert">{e.message}</p>)}{sim.isFetching ? <p role="status">Calculando prévia…</p> : sim.isError ? <ErrorBox message="Configuração de preço pendente ou parâmetros inválidos. Cadastre um preço ativo para este tipo." /> : sim.data && <ResumoCalculo calculo={sim.data} />}{mutation.isError && <ErrorBox message="Não foi possível criar a vistoria. Verifique se o tipo e o preço continuam ativos." />}<button className="primary" disabled={mutation.isPending || sim.isFetching || !sim.data || sim.isError}>Salvar</button></form>
}
