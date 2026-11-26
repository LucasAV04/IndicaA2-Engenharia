import { z } from 'zod'

export const limiteEmail = 254
export const emailSchema = z.string().trim().max(limiteEmail, 'Use até 254 caracteres.').email('Informe um e-mail válido.')
const paisBrasil = '55'
const caracteresTelefone = /^\+?[0-9 ().-]+$/

export function separarTelefone(valor: string) {
  const texto = valor.trim()
  const digitos = texto.replace(/\D/g, '')
  const internacional = texto.startsWith('+')
  const estrangeiro = internacional && !digitos.startsWith(paisBrasil)
  // Uma entrada nacional já mascarada começa com parêntese. Seu DDD 55
  // continua sendo DDD mesmo quando ainda há dígitos excedentes.
  const pais = !estrangeiro && (internacional
    || (texto.startsWith(paisBrasil) && (digitos.length === 12 || digitos.length === 13))) ? 2 : 0
  return { nacional: digitos.slice(pais), pais, estrangeiro, formatoValido: texto.length <= 30 && caracteresTelefone.test(texto) }
}

export function erroTelefone(valor: string, opcional = false) {
  if (!valor.trim()) return opcional ? null : 'Informe o telefone com DDD.'
  const { nacional, estrangeiro, formatoValido } = separarTelefone(valor)
  if (estrangeiro) return 'Este campo aceita somente telefones do Brasil (+55).'
  if (!formatoValido || nacional.length > 11) return 'Confira o telefone. Use DDD e número brasileiro.'
  if (nacional.length < 10) return 'Complete o telefone com DDD.'
  if (!/^[1-9]{2}/.test(nacional)) return 'Informe um DDD válido.'
  return null
}

export function telefoneCanonico(valor: string) {
  return erroTelefone(valor) ? null : paisBrasil + separarTelefone(valor).nacional
}

export function mascararTelefone(valor: string, duranteEdicao = false) {
  const { nacional, estrangeiro, formatoValido } = separarTelefone(valor)
  if (estrangeiro || !formatoValido || nacional.length > 11 || (!duranteEdicao && erroTelefone(valor))) return valor
  if (!nacional) return ''
  if (nacional.length <= 2) return '(' + nacional
  const corte = nacional.length === 11 ? 7 : 6
  return '(' + nacional.slice(0, 2) + ') ' + nacional.slice(2, corte)
    + (nacional.length > corte ? '-' + nacional.slice(corte) : '')
}

export function schemaTelefone(opcional = false, original?: string) {
  return z.string().trim().superRefine((valor, contexto) => {
    if (valor === original) return
    const erro = erroTelefone(valor, opcional)
    if (erro) contexto.addIssue({ code: 'custom', message: erro })
  })
}

export function dataValida(valor: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(valor)) return false
  const [ano, mes, dia] = valor.split('-').map(Number)
  const bissexto = ano % 4 === 0 && (ano % 100 !== 0 || ano % 400 === 0)
  const dias = [31, bissexto ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  return ano >= 1 && mes >= 1 && mes <= 12 && dia >= 1 && dia <= dias[mes - 1]
}

export function dataCivilValida(valor: string) {
  const [data, hora, sobra] = valor.split('T')
  return sobra === undefined && dataValida(data) && /^([01]\d|2[0-3]):[0-5]\d(?::[0-5]\d(?:\.\d+)?)?$/.test(hora ?? '')
}

export function intervaloRelatorioValido(inicio: string, fim: string) {
  if ((inicio && !dataValida(inicio)) || (fim && (!dataValida(fim) || fim === '9999-12-31'))) return false
  if (!inicio) return true
  const fimEfetivo = fim || new Date().toISOString().slice(0, 10)
  return fimEfetivo >= inicio && Date.parse(fimEfetivo) - Date.parse(inicio) < 366 * 86_400_000
}
