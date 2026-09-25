import { useQuery } from '@tanstack/react-query'
import { send } from './api'
import { money, moneyPreciso } from './format'
import { simulacaoSchema, type Calculo } from './precificacao'

export function useSimulacao(tipoPlantaId: string, areaM2: string, pacote: string) {
  const valid = simulacaoSchema.safeParse({ tipoPlantaId, areaM2, pacote }).success
  return useQuery({ queryKey: ['simulacao-vistoria', tipoPlantaId, areaM2, pacote], enabled: valid, retry: false,
    queryFn: ({ signal }) => send<Calculo>('/precos-vistoria/simular', 'POST', { tipoPlantaId, areaM2, pacote: Number(pacote) }, signal) })
}
export function ResumoCalculo({ calculo }: { calculo: Calculo }) {
  return <div className="notice" aria-live="polite"><p>Simulação do backend — versão {calculo.versao}</p><p>Preço/m²: {moneyPreciso(calculo.precoM2)} • Valor base: {moneyPreciso(calculo.valorBase, 6)}</p><p>Valor final: <strong>{money(calculo.valorFinal)}</strong></p><p>O servidor recalcula com a versão ativa no momento de salvar.</p></div>
}
