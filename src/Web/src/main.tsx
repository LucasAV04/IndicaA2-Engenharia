import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import AppErrorBoundary from './AppErrorBoundary'
import './style.css'
import { capturarFragmentoInicial } from './fragmentoPrivado'

capturarFragmentoInicial()

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false }, mutations: { retry: false } } })
createRoot(document.getElementById('root')!, { onCaughtError: () => undefined }).render(
  <StrictMode><QueryClientProvider client={queryClient}><BrowserRouter><AppErrorBoundary><App /></AppErrorBoundary></BrowserRouter></QueryClientProvider></StrictMode>,
)
