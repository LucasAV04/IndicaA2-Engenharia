import type { Reporter, TestCase, TestResult } from '@playwright/test/reporter'

export default class SafeReporter implements Reporter {
  onTestEnd(test: TestCase, result: TestResult) {
    if (result.status === 'skipped') return
    const frame = result.errors[0]?.stack?.match(/aceite\.spec\.ts:(\d+):\d+/)
    const line = result.errors[0]?.location?.line ?? Number(frame?.[1] ?? test.location.line)
    // Asserções do navegador podem conter credenciais no diagnóstico bruto.
    process.stdout.write(`Chromium: ${result.status} (linha ${line}).\n`)
  }
}
