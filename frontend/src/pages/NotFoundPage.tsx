import { NotFound } from '@/components/common/NotFound'
import { useTranslation } from 'react-i18next'

/** Rota desconhecida (contratos/ui-routes.md: `*`). */
export const NotFoundPage = () => {
  const { t } = useTranslation()

  return (
    <div className="py-10">
      <NotFound title={t('errors.notFoundPage')} linkTo="/clientes" />
    </div>
  )
}

export default NotFoundPage
