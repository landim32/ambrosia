import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'

import App, { siteBasename } from '@/App'
import { AuthProvider } from '@/Contexts/AuthContext'
import { CustomerPhotoProvider } from '@/Contexts/CustomerPhotoContext'
import { CustomerProvider } from '@/Contexts/CustomerContext'
import { ImportProvider } from '@/Contexts/ImportContext'
import { MaisVendasSettingsProvider } from '@/Contexts/MaisVendasSettingsContext'
import { OrderProvider } from '@/Contexts/OrderContext'
import { Toaster } from '@/components/ui/sonner'

import '@/index.css'
import '@/i18n'

const container = document.getElementById('root')

if (!container) {
  throw new Error('Elemento #root ausente em index.html')
}

/**
 * Ponto de entrada. Cadeia de providers da skill `react-architecture`
 * (research §15):
 *
 *   BrowserRouter → AuthProvider (US1) → CustomerProvider (US2) →
 *   CustomerPhotoProvider (feature 009; dentro do de clientes: o álbum é sempre o
 *   da cliente aberta no perfil, e nada aqui carrega sozinho — é a `CustomerProfilePage`
 *   que chama `loadPhotos`) → OrderProvider (US3)
 *     → ImportProvider (feature 006; não carrega nada sozinho — a `ImportPage` busca)
 *     → MaisVendasSettingsProvider (feature 008; idem: é a tela de configuração que
 *        lê as credenciais, e a senha nunca passa por aqui)
 *
 * O roteador mora AQUI (e não em `App.tsx`) porque o `AuthContext` precisa de
 * `useNavigate`/`useLocation` para encerrar a sessão em expiração e 401 (T039);
 * as rotas propriamente ditas ficam em `App.tsx`. O `basename` vem de
 * `VITE_SITE_BASENAME` (T029): vazio = raiz da origem, e o `App` usa caminhos
 * relativos a essa base.
 */
createRoot(container).render(
  <StrictMode>
    <BrowserRouter basename={siteBasename()}>
      <AuthProvider>
        <CustomerProvider>
          <CustomerPhotoProvider>
            <OrderProvider>
              <ImportProvider>
                <MaisVendasSettingsProvider>
                  <App />
                </MaisVendasSettingsProvider>
              </ImportProvider>
            </OrderProvider>
          </CustomerPhotoProvider>
        </CustomerProvider>
        <Toaster richColors position="top-center" />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)
