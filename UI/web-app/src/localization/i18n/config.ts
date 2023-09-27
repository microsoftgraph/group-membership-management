import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'

i18n.use(initReactI18next).init({
  returnNull: false,
  fallbackLng: 'en',
  lng: 'en',
  resources: {
    en: {
      translations: require('./locales/en/translations').strings
    },
    es: {
      translations: require('./locales/es/translations').strings
    }
  },
  ns: ['translations'],
  defaultNS: 'translations'
})

i18n.languages = ['en', 'es']

export default i18n
