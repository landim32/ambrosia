import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'

import { ptBR } from '@/i18n/locales/pt-BR'

/**
 * i18next com um único recurso pt-BR (research §17, FR-024).
 * `initImmediate: false` deixa as chaves prontas no primeiro render: como as
 * `resources` são locais, o `init` resolve de forma síncrona na mesma tarefa.
 * (A partir do i18next 25.10 a opção ganhou o nome `initAsync`; o apelido
 * `initImmediate` continua mapeado para ela — T025 fixa a redação da spec.)
 */
if (!i18n.isInitialized) {
  void i18n.use(initReactI18next).init({
    lng: 'pt-BR',
    fallbackLng: 'pt-BR',
    // eslint-disable-next-line @typescript-eslint/no-deprecated
    initImmediate: false,
    resources: {
      'pt-BR': {
        translation: ptBR,
      },
    },
    interpolation: {
      escapeValue: false,
    },
  })
}

export default i18n
