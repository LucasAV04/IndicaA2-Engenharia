import { dataValida } from './campos'

export const money = (value = 0) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
// Exibição preserva a precisão dos parâmetros e da base; não calcula valores financeiros.
export const moneyPreciso = (value: number, casas = 4) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: 2, maximumFractionDigits: casas }).format(value)
export function utcDate(value?: string | null) {
  if (!value) return '—'
  const instante = new Date(value)
  if (!Number.isFinite(instante.getTime())) return '—'
  return instante.toLocaleDateString('pt-BR') + ' às ' + instante.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
}
export function calendarDate(value: string) {
  return dataValida(value) ? value.split('-').reverse().join('/') : '—'
}

// DataAgendada é um horário civil, sem fuso: não criar um instante Date.
export function businessDate(value?: string) {
  const parts = value?.match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::\d{2}(?:\.\d+)?)?$/)
  return parts ? `${parts[3]}/${parts[2]}/${parts[1]} às ${parts[4]}:${parts[5]}` : '—'
}
