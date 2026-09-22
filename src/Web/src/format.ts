export const money = (value = 0) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
export const utcDate = (value?: string) => value ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) : '—'
// DataAgendada é um horário civil, sem fuso: não criar um instante Date.
export function businessDate(value?: string) {
  const parts = value?.match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::\d{2}(?:\.\d+)?)?$/)
  return parts ? `${parts[3]}/${parts[2]}/${parts[1]}, ${parts[4]}:${parts[5]}` : '—'
}
export const labels: Record<string, string> = { VistoriaVinculada: 'Vistoria vinculada', VistoriaConcluida: 'Vistoria concluída', Concluida: 'Concluída', Concluido: 'Concluído', Disponivel: 'Disponível', FalhaDefinitiva: 'Falha definitiva' }
