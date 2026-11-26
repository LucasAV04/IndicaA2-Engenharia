import { z } from 'zod'

export const limiteEmail = 254
export const emailSchema = z.string().trim().max(limiteEmail, 'Use até 254 caracteres.').email('Informe um e-mail válido.')
export const mensagemTelefone = 'Informe um telefone brasileiro com DDD e 10 ou 11 dígitos. Para código do país, use +55.'

export function telefoneCanonico(valor: string) {
  const texto = valor.trim()
  if (texto.length > 30 || !/^\+?[0-9 ().-]+$/.test(texto)) return null
  const digitos = texto.replace(/\D/g, '')
  const comPais = (digitos.length === 12 || digitos.length === 13) && digitos.startsWith('55')
  if (texto.startsWith('+') && !comPais) return null
  return /^[1-9]{2}\d{8,9}$/.test(comPais ? digitos.slice(2) : digitos) ? digitos : null
}

export function mascararTelefone(valor: string, duranteEdicao = false) {
  if (!/^\+?[0-9 ().-]*$/.test(valor)) return valor
  const digitos = valor.replace(/\D/g, '')
  const comMais = valor.startsWith('+')
  const comPais = comMais || (digitos.length >= 12 && digitos.startsWith('55'))
  if (comMais && !digitos.startsWith('55')) return valor
  const nacional = comPais ? digitos.slice(2) : digitos
  if (nacional.length > 11 || (!duranteEdicao && !telefoneCanonico(valor))) return valor
  const prefixo = comPais ? '+55 ' : ''
  if (!nacional) return prefixo.trimEnd()
  if (nacional.length <= 2) return prefixo + '(' + nacional
  const corte = nacional.length === 11 ? 7 : 6
  return prefixo + '(' + nacional.slice(0, 2) + ') ' + nacional.slice(2, corte)
    + (nacional.length > corte ? '-' + nacional.slice(corte) : '')
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
