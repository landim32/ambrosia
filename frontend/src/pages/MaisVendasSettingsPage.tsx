import { useTranslation } from 'react-i18next'

import { MaisVendasCredentialsCard } from '@/components/settings/MaisVendasCredentialsCard'

/**
 * Tela de configuração do +Vendas (feature 008, US1, FR-001 a FR-007; rota
 * `/configuracoes/mais-vendas` de `contracts/ui.md` e research R10).
 *
 * Hoje a seção tem um cartão só — as credenciais do +Vendas. A tela é o ÚNICO
 * lugar do sistema onde a vendedora informa o login e a senha dela no +Vendas
 * (FR-019: o plugin não oferece essa ação), e o destino do link que a tela de
 * importação mostra quando falta credencial (FR-009).
 *
 * O carregamento é do cartão (`useMaisVendasSettings`), não daqui: a página é só a
 * coluna de título + cartões, como `ImportPage` é só a coluna da importação.
 */
export const MaisVendasSettingsPage = () => {
  const { t } = useTranslation()

  return (
    // Dentro do `AppShell`, que já é o <main>: aqui é só a coluna da tela.
    <div className="mx-auto flex w-full max-w-screen-md flex-col gap-4">
      <h1 className="text-3xl font-bold tracking-tight text-foreground">{t('settings.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('settings.subtitle')}</p>

      <MaisVendasCredentialsCard />
    </div>
  )
}

export default MaisVendasSettingsPage
