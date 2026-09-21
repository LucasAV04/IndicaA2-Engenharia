export const money = (value = 0) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
export const date = (value?: string) => value ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) : '—'
export const labels: Record<string, string> = { VistoriaVinculada: 'Vistoria vinculada', VistoriaConcluida: 'Vistoria concluída', Concluida: 'Concluída', Concluido: 'Concluído', Disponivel: 'Disponível', FalhaDefinitiva: 'Falha definitiva' }
