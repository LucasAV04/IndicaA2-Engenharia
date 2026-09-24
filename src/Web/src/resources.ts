import type { Registro } from './types'
import { businessDate, utcDate, money } from './format'
export type ResourceKey = 'usuarios' | 'indicacoes' | 'vistorias' | 'pagamentos-vistoria' | 'cashbacks' | 'pagamentos-pix'
export const resourceDependencies: Record<ResourceKey, ResourceKey[]> = {
  usuarios: [],
  indicacoes: ['usuarios', 'vistorias'],
  vistorias: ['usuarios'],
  'pagamentos-vistoria': ['vistorias', 'usuarios'],
  cashbacks: ['pagamentos-vistoria', 'usuarios'],
  'pagamentos-pix': ['cashbacks', 'usuarios'],
}
export type Field = { name: string; label: string; type?: string; source?: ResourceKey; options?: string[]; optional?: boolean; onlyStatus?: number; min?: number }
export type Operation = { label: string; method: string; path: (row?: Registro, values?: Record<string, string>) => string; fields?: Field[]; allowed?: (r: Registro) => boolean; confirm?: boolean; body?: (v: Record<string, string>, row?: Registro) => unknown }
export type Resource = { title: string; singular: string; description: string; statuses: string[]; columns: { label: string; value: (r: Registro, lists: Partial<Record<ResourceKey, Registro[]>>) => string }[]; create: Operation; actions: Operation[] }
export const nome = (lists: Partial<Record<ResourceKey, Registro[]>>, id?: string) => lists.usuarios?.find(u => u.id === id)?.nome || (id ? 'Usuário não disponível' : '—')
const cancel = (key: ResourceKey, statuses: number[]): Operation => ({ label: 'Cancelar', method: 'PATCH', path: r => '/' + key + '/' + r!.id + '/cancelar', confirm: true, allowed: r => statuses.includes(r.status) })
const userFields: Field[] = [{ name: 'nome', label: 'Nome' }, { name: 'email', label: 'E-mail', type: 'email' }, { name: 'telefone', label: 'Telefone', type: 'tel', optional: true }]
export const resources: Record<ResourceKey, Resource> = {
  usuarios: {
    title: 'Usuários', singular: 'usuário', description: 'Clientes, códigos de indicação e dados para recebimento.',
    statuses: ['—', 'Ativo', 'Inativo', 'Bloqueado'],
    columns: [{ label: 'Nome', value: r => r.nome! }, { label: 'E-mail', value: r => r.email! }, { label: 'Telefone', value: r => r.telefone || '—' }, { label: 'Código de indicação', value: r => r.codigoIndicacao || '—' }],
    create: { label: 'Novo usuário', method: 'POST', path: () => '/usuarios', fields: [...userFields, { name: 'senha', label: 'Senha inicial', type: 'password' }] },
    actions: [{ label: 'Editar', method: 'PUT', path: r => '/usuarios/' + r!.id, fields: userFields, body: (v, r) => ({ ...v, id: r!.id }) }],
  },
  indicacoes: {
    title: 'Indicações', singular: 'indicação', description: 'Acompanhe a indicação desde o código até a vistoria concluída.',
    statuses: ['Pendente', 'Vistoria vinculada', 'Vistoria concluída', 'Cancelada'],
    columns: [{ label: 'Pessoa indicada', value: r => r.nomeIndicada! }, { label: 'Telefone', value: r => r.telefoneIndicada! }, { label: 'Indicador', value: (r, l) => nome(l, r.usuarioIndicadorId) }, { label: 'Código', value: r => r.codigoIndicacaoUsado || '—' }],
    create: { label: 'Nova indicação', method: 'POST', path: () => '/indicacoes/por-codigo', fields: [{ name: 'codigoIndicacao', label: 'Código de indicação' }, { name: 'nomeIndicada', label: 'Nome da pessoa indicada' }, { name: 'telefoneIndicada', label: 'Telefone da pessoa indicada', type: 'tel' }] },
    actions: [
      { label: 'Vincular usuário', method: 'PATCH', path: r => '/indicacoes/' + r!.id + '/usuario-indicado', fields: [{ name: 'usuarioIndicadoId', label: 'Usuário indicado', source: 'usuarios' }], body: (v, r) => ({ ...v, indicacaoId: r!.id }), allowed: r => !r.usuarioIndicadoId && r.status !== 3 },
      { label: 'Vincular vistoria', method: 'PATCH', path: r => '/indicacoes/' + r!.id + '/vistoria', fields: [{ name: 'vistoriaId', label: 'Vistoria', source: 'vistorias' }], body: (v, r) => ({ ...v, indicacaoId: r!.id }), allowed: r => r.status === 0 && !r.vistoriaId },
      { label: 'Concluir vínculo', method: 'PATCH', path: r => '/indicacoes/' + r!.id + '/vistoria/concluir', allowed: r => r.status === 1 },
      cancel('indicacoes', [0, 1]),
    ],
  },
  vistorias: {
    title: 'Vistorias', singular: 'vistoria', description: 'Agenda, execução e conclusão das vistorias dos clientes.',
    statuses: ['Agendada', 'Realizada', 'Concluída', 'Cancelada'],
    columns: [{ label: 'Cliente', value: (r, l) => nome(l, r.usuarioId) }, { label: 'Pacote', value: r => r.pacote === 0 ? 'Simples' : 'Total' }, { label: 'Área (m²)', value: r => String(r.areaM2) }, { label: 'Planta', value: r => r.tipoPlanta! }, { label: 'Precificação', value: r => r.precificacao ? `${money(r.precificacao.valorFinal)} • v${r.precificacao.versao}` : 'Legado — sem catálogo/snapshot' }, { label: 'Agendamento', value: r => businessDate(r.dataAgendada) }],
    create: { label: 'Nova vistoria', method: 'POST', path: () => '/vistorias', fields: [{ name: 'usuarioId', label: 'Cliente', source: 'usuarios' }] },
    actions: [{ label: 'Realizar', method: 'PATCH', path: r => '/vistorias/' + r!.id + '/realizar', allowed: r => r.status === 0 }, { label: 'Concluir', method: 'PATCH', path: r => '/vistorias/' + r!.id + '/concluir', allowed: r => r.status === 1 }, cancel('vistorias', [0])],
  },
  'pagamentos-vistoria': {
    title: 'Pagamentos de vistoria', singular: 'pagamento', description: 'Valores derivados do snapshot da vistoria. Pendente é receita esperada, não recebida.',
    statuses: ['Pendente', 'Confirmado', 'Cancelado'],
    columns: [{ label: 'Vistoria / cliente', value: (r, l) => nome(l, l.vistorias?.find(v => v.id === r.vistoriaId)?.usuarioId) }, { label: 'Valor', value: r => money(r.valor) }, { label: 'Confirmado em', value: r => utcDate(r.pagoEm) }],
    create: { label: 'Novo pagamento', method: 'POST', path: () => '/pagamentos-vistoria', fields: [{ name: 'vistoriaId', label: 'Vistoria', source: 'vistorias' }] },
    actions: [{ label: 'Confirmar pagamento', method: 'PATCH', path: r => '/pagamentos-vistoria/' + r!.id + '/confirmar', allowed: r => r.status === 0 }, cancel('pagamentos-vistoria', [0])],
  },
  cashbacks: {
    title: 'Cashback', singular: 'cashback', description: 'Gere a partir de um pagamento confirmado. Disponível não significa pago.',
    statuses: ['Pendente', 'Disponível', 'Pago', 'Cancelado'],
    columns: [{ label: 'Indicador', value: (r, l) => nome(l, r.usuarioIndicadorId) }, { label: 'Total pago', value: r => money(r.valorTotalPago) }, { label: 'Percentual', value: r => new Intl.NumberFormat('pt-BR', { style: 'percent' }).format(r.percentual || 0) }, { label: 'Cashback', value: r => money(r.valor) }],
    create: { label: 'Gerar cashback', method: 'POST', path: (_, v) => '/cashbacks/por-pagamento/' + v!.pagamentoVistoriaId, fields: [{ name: 'pagamentoVistoriaId', label: 'Pagamento confirmado', source: 'pagamentos-vistoria', onlyStatus: 1 }], body: () => undefined },
    actions: [{ label: 'Aprovar', method: 'PATCH', path: r => '/cashbacks/' + r!.id + '/aprovar', allowed: r => r.status === 0 }, cancel('cashbacks', [0, 1])],
  },
  'pagamentos-pix': {
    title: 'Pagamentos Pix', singular: 'pagamento Pix', description: 'O processamento depende do worker configurado no servidor. Este painel não dispara envios nem retentativas.',
    statuses: ['Pendente', 'Processando', 'Concluído', 'Falhou', 'Falha definitiva', 'Cancelado'],
    columns: [{ label: 'Beneficiário', value: (r, l) => nome(l, r.usuarioBeneficiarioId) }, { label: 'Valor', value: r => money(r.valor) }, { label: 'Tipo de chave', value: r => ['CPF', 'CNPJ', 'E-mail', 'Telefone', 'Aleatória'][r.tipoChavePix!] }, { label: 'Tentativas', value: r => String(r.quantidadeTentativas) }, { label: 'Criação', value: r => utcDate(r.createdAt) }, { label: 'Atualização', value: r => utcDate(r.updatedAt) }],
    create: { label: 'Criar pagamento Pix', method: 'POST', path: (_, v) => '/pagamentos-pix/por-cashback/' + v!.cashbackId, fields: [{ name: 'cashbackId', label: 'Cashback disponível', source: 'cashbacks', onlyStatus: 1 }], body: () => undefined },
    actions: [cancel('pagamentos-pix', [0, 3])],
  },
}
export function optionLabel(key: ResourceKey, row: Registro, lists: Partial<Record<ResourceKey, Registro[]>>) {
  if (key === 'usuarios') return row.nome + ' • ' + row.email
  if (key === 'vistorias') return nome(lists, row.usuarioId) + ' • ' + businessDate(row.dataAgendada) + ' • ' + row.id.slice(0, 8)
  if (key === 'cashbacks') return nome(lists, row.usuarioIndicadorId) + ' • ' + money(row.valor) + ' • ' + row.id.slice(0, 8)
  return money(row.valor) + ' • ' + row.id.slice(0, 8)
}
