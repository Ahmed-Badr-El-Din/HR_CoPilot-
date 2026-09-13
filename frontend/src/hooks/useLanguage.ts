import { useEffect, useState } from 'react';

type Language = 'en' | 'ar';

export function useLanguage() {
  const [language, setLanguageState] = useState<Language>(() => {
    return (localStorage.getItem('app_language') as Language) || 'en';
  });

  useEffect(() => {
    const root = document.documentElement;
    if (language === 'ar') {
      root.dir = 'rtl';
      root.lang = 'ar';
    } else {
      root.dir = 'ltr';
      root.lang = 'en';
    }
  }, [language]);

  const setLanguage = (lang: Language) => {
    localStorage.setItem('app_language', lang);
    setLanguageState(lang);
  };

  return { language, setLanguage };
}
