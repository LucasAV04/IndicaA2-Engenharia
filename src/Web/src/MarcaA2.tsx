import horizontalClaro from './assets/brand/a2-horizontal-fundo-claro-384.png'
import horizontalClaro2x from './assets/brand/a2-horizontal-fundo-claro.png'
import horizontalEscuro from './assets/brand/a2-horizontal-fundo-escuro-384.png'
import horizontalEscuro2x from './assets/brand/a2-horizontal-fundo-escuro.png'
import verticalClaro from './assets/brand/a2-vertical-fundo-claro.png'
import verticalEscuro from './assets/brand/a2-vertical-fundo-escuro.png'
import simboloEscuro from './assets/brand/a2-simbolo-fundo-escuro.png'
import './marca-a2.css'

const horizontais = {
  claro: { src: horizontalClaro, srcSet: horizontalClaro + ', ' + horizontalClaro2x + ' 2x', width: 384, height: 128 },
  escuro: { src: horizontalEscuro, srcSet: horizontalEscuro + ', ' + horizontalEscuro2x + ' 2x', width: 384, height: 130 },
}

const verticais = {
  claro: { src: verticalClaro, width: 320, height: 409 },
  escuro: { src: verticalEscuro, width: 320, height: 405 },
}

type Fundo = 'claro' | 'escuro'
type MarcaProps = {
  className?: string
  descricao?: string
  decorativa?: boolean
} & (
  | { formato: 'horizontal'; fundo: Fundo; compacto?: never }
  | { formato: 'vertical'; fundo: Fundo; compacto?: string }
  | { formato: 'simbolo'; fundo: 'escuro'; compacto?: never }
)

export default function MarcaA2(props: MarcaProps) {
  const { formato, fundo, className, descricao = 'A2 Engenharia & Diagnóstico', decorativa } = props
  const imagem = formato === 'horizontal' ? horizontais[fundo]
    : formato === 'vertical' ? verticais[fundo]
      : { src: simboloEscuro, width: 192, height: 193 }
  const horizontal = props.compacto ? horizontais[fundo] : undefined

  return <picture className={className ? 'marca-a2 ' + className : 'marca-a2'}>
    {horizontal && <source media={props.compacto} srcSet={horizontal.srcSet} width={horizontal.width} height={horizontal.height} />}
    <img {...imagem} alt={decorativa ? '' : descricao} />
  </picture>
}
