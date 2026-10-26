/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_URL: string
  readonly VITE_SITE_BASENAME?: string
  readonly VITE_GOOGLE_CLIENT_ID?: string
  readonly VITE_DEV_LOGIN?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
