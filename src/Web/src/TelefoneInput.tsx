import type { ChangeEvent, KeyboardEvent } from 'react'
import type { UseFormRegisterReturn } from 'react-hook-form'
import { mascararTelefone } from './campos'

type Props = {
  campo: UseFormRegisterReturn
  id: string
  obrigatorio?: boolean
  erro?: boolean
  terceiro?: boolean
}

export default function TelefoneInput({ campo, id, obrigatorio, erro, terceiro }: Props) {
  function alterar(evento: ChangeEvent<HTMLInputElement>) {
    const input = evento.currentTarget
    const posicao = input.selectionStart ?? input.value.length
    const quantidade = (input.value.slice(0, posicao).match(/[+\d]/g) ?? []).length
    const visual = mascararTelefone(input.value, true)
    if (visual !== input.value) {
      input.value = visual
      let cursor = 0
      let encontrados = 0
      while (cursor < visual.length && encontrados < quantidade) {
        if (/[+\d]/.test(visual[cursor])) encontrados++
        cursor++
      }
      input.setSelectionRange(cursor, cursor)
    }
    void campo.onChange(evento)
  }

  function apagar(evento: KeyboardEvent<HTMLInputElement>) {
    const input = evento.currentTarget
    const posicao = input.selectionStart
    if (posicao === null || posicao !== input.selectionEnd || evento.ctrlKey || evento.metaKey || evento.altKey) return
    let cursor = posicao
    if (evento.key === 'Backspace') while (cursor > 0 && /[ ().-]/.test(input.value[cursor - 1])) cursor--
    if (evento.key === 'Delete') while (cursor < input.value.length && /[ ().-]/.test(input.value[cursor])) cursor++
    if (cursor !== posicao) input.setSelectionRange(cursor, cursor)
  }

  return <input {...campo} id={id} type="tel" inputMode="tel" autoComplete={terceiro ? 'off' : 'tel'}
    required={obrigatorio} aria-invalid={!!erro} aria-describedby={erro ? id + '-erro' : undefined}
    onChange={alterar} onKeyDown={apagar} />
}
