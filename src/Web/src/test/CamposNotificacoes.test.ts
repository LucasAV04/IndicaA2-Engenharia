import { describe, expect, it } from 'vitest'
import { dataCivilValida, dataValida, emailSchema, erroTelefone, intervaloRelatorioValido, mascararTelefone, telefoneCanonico } from '../campos'
import { businessDate, calendarDate, utcDate } from '../format'
import { apresentarNotificacao, type Notificacao } from '../notificacoes'

describe('campos padronizados', () => {
  it.each([
    ['79999999999', '(79) 99999-9999', '5579999999999'],
    ['(79) 3333-4444', '(79) 3333-4444', '557933334444'],
    ['+55 7933334444', '(79) 3333-4444', '557933334444'],
    ['5579999999999', '(79) 99999-9999', '5579999999999'],
    ['(55) 79 99999-9999', '(79) 99999-9999', '5579999999999'],
    ['(55) (79) 3333-4444', '(79) 3333-4444', '557933334444'],
    ['55999999999', '(55) 99999-9999', '5555999999999'],
  ])('formata %s sem perder os dígitos', (entrada, visual, canonico) => {
    expect(mascararTelefone(entrada)).toBe(visual)
    expect(telefoneCanonico(visual)).toBe(canonico)
  })
  it.each(['+1 212 555 0100', '0733334444', '79999', '797777777777', ''])('não converte %s', valor => {
    expect(telefoneCanonico(valor)).toBeNull()
    expect(mascararTelefone(valor)).toBe(valor)
  })
  it.each([
    ['', 'Informe o telefone com DDD.'],
    ['(79) 9999', 'Complete o telefone com DDD.'],
    ['(00) 99999-9999', 'Informe um DDD válido.'],
    ['(55) 99999-99990', 'Confira o telefone. Use DDD e número brasileiro.'],
    ['+1 212 555 0100', 'Este campo aceita somente telefones do Brasil (+55).'],
  ])('distingue a mensagem de %s', (entrada, mensagem) => {
    expect(erroTelefone(entrada)).toBe(mensagem)
  })
  it('valida e-mail sem remover + ou mudar a capitalização', () => {
    expect(emailSchema.parse(' Ana+Teste@example.invalid ')).toBe('Ana+Teste@example.invalid')
    expect(emailSchema.safeParse('a@').success).toBe(false)
    expect(emailSchema.safeParse('a'.repeat(255) + '@example.invalid').success).toBe(false)
  })
  it('distingue data civil, horário comercial e instante UTC', () => {
    expect(dataValida('2024-02-29')).toBe(true)
    expect(dataValida('2026-02-29')).toBe(false)
    expect(dataValida('2026-04-31')).toBe(false)
    expect(dataCivilValida('2026-11-28T19:00')).toBe(true)
    expect(dataCivilValida('2026-11-28T24:00')).toBe(false)
    expect(calendarDate('2026-11-28')).toBe('28/11/2026')
    expect(businessDate('2026-11-28T19:00:00')).toBe('28/11/2026 às 19:00')
    const instante = new Date('2026-11-28T00:00:01Z')
    expect(utcDate(instante.toISOString()).split(' às ')[0]).toBe(instante.toLocaleDateString('pt-BR'))
    expect(intervaloRelatorioValido('2026-11-28', '2026-11-27')).toBe(false)
    expect(intervaloRelatorioValido('2024-01-01', '2024-12-31')).toBe(true)
    expect(intervaloRelatorioValido('2024-01-01', '2025-01-01')).toBe(false)
  })
})

const base: Omit<Notificacao, 'tipo'> = { id: 'aviso', criadaEm: '2026-11-28T00:00:01Z', lidaEm: null }
it.each([
  [0, 'Atualização de vistoria'], [1, 'Pagamento confirmado'], [2, 'Dados Pix necessários'],
  [3, 'Cashback disponível'], [4, 'Cashback pago'], [5, 'Falha financeira'], [6, 'Cobrança Pix em revisão'],
  [99, 'Notificação'],
])('exibe título e descrição controlados para tipo %s', (tipo, titulo) => {
  const item = apresentarNotificacao({ ...base, tipo }, false)
  expect(item.titulo).toBe(titulo)
  expect(item.descricao).not.toBe('')
  if (tipo === 5 || tipo === 6 || tipo === 99) expect(item).not.toHaveProperty('destino')
})
it('distingue vínculo de criação e oferece rota interna autorizada', () => {
  const contexto = { primeiroNome: 'Ana', agendamentoAtual: '2026-11-28T19:00:00', vistoriaPropria: false }
  expect(apresentarNotificacao({ ...base, tipo: 0, contexto }, false)).toMatchObject({
    titulo: 'Vistoria vinculada', descricao: 'A indicação de Ana foi vinculada a uma vistoria.',
    destino: '/minha-conta#indicacoes',
  })
  expect(apresentarNotificacao({ ...base, tipo: 0, contexto: { ...contexto, primeiroNome: null, vistoriaPropria: true } }, false).titulo)
    .toBe('Vistoria cadastrada')
  expect(apresentarNotificacao({ ...base, tipo: 6 }, true)).toHaveProperty('destino', '/cobrancas-pix-vistoria')
})
