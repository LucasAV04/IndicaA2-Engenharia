import { z } from 'zod'
export type TipoPlanta = { id: string; nome: string; ativo: boolean; possuiPrecoAtivo: boolean; createdAt: string; updatedAt: string }
export type Preco = { id: string; tipoPlantaId: string; nomeTipoPlanta: string; precoM2: number; modalidade: number; acrescimo: number; versao: number; ativo: boolean; createdAt: string; desativadoEm: string | null }
export type Calculo = { precoId: string; tipoPlantaId: string; nomeTipoPlanta: string; versao: number; precoM2: number; valorBase: number; valorFinal: number; simulacao: boolean }
// Validação de formato apenas; toda aritmética financeira pertence à API decimal.
export const decimalPositivo = z.string().regex(/^\d{1,8}(\.\d{1,4})?$/, 'Use até quatro casas decimais.').refine(v => Number(v) > 0, 'Informe um valor positivo.')
export const areaSchema = z.string().regex(/^\d{1,8}(\.\d{1,2})?$/, 'Use uma área com até duas casas.').refine(v => Number(v) > 0, 'Informe uma área positiva.')
export const tipoSchema = z.object({ nome: z.string().trim().min(1, 'Informe o nome.').max(150, 'Use até 150 caracteres.') })
export const precoSchema = z.object({ precoM2: decimalPositivo, modalidade: z.enum(['0', '1']), acrescimo: z.string().regex(/^\d{1,8}(\.\d{1,4})?$/, 'Informe um acréscimo não negativo.') })
  .refine(v => v.modalidade !== '1' || Number(v.acrescimo) <= 10000, { path: ['acrescimo'], message: 'Limite técnico: 10000%.' })
export const simulacaoSchema = z.object({ tipoPlantaId: z.string().uuid('Selecione um tipo.'), areaM2: areaSchema, pacote: z.enum(['0', '1']) })
