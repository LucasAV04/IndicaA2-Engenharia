export const estadoDesconhecido = 'Estado não reconhecido'
export const estadosUsuario = { 1: 'Ativo', 2: 'Inativo', 3: 'Bloqueado' } as const
export const estadosIndicacao = ['Pendente', 'Vistoria vinculada', 'Vistoria concluída', 'Cancelada', 'Cashback pago'] as const
export const estadosVistoria = ['Agendada', 'Realizada', 'Concluída', 'Cancelada'] as const
export const estadosPagamento = ['Pendente', 'Confirmado', 'Cancelado'] as const
export const estadosCashback = ['Pendente', 'Disponível', 'Pago', 'Cancelado'] as const
export const estadosPix = ['Pendente', 'Processando', 'Concluído', 'Falhou', 'Falha definitiva', 'Cancelado'] as const
export const estadosCobranca = ['Preparada', 'Criando', 'Ativa', 'Indeterminada', 'Confirmação pendente', 'Confirmada',
  'Expirada', 'Remoção pendente', 'Removida', 'Falha definitiva', 'Divergência financeira'] as const
export const estadosEmail = ['Pendente', 'Processando', 'Enviado', 'Falha permanente', 'Indeterminado', 'Expirado'] as const
export const operacoesCobranca = ['Criar cobrança', 'Consultar cobrança', 'Remover cobrança',
  'Consultar recebimento', 'Processar recebimento'] as const

export function rotulo(estados: Readonly<Partial<Record<number, string>>>, valor: number) {
  return Object.hasOwn(estados, valor) ? estados[valor] ?? estadoDesconhecido : estadoDesconhecido
}

export const estadosPorRelatorio: readonly Readonly<Partial<Record<number, string>>>[] = [
  { 1: estadosPagamento[1] }, { 1: estadosCashback[1] }, { 2: estadosCashback[2] },
  { 0: estadosPix[0], 1: estadosPix[1] }, { 3: estadosPix[3], 4: estadosPix[4] },
  { 10: estadosCobranca[10] }, estadosEmail,
]
