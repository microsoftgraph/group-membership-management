import React from 'react';
import { useTranslation } from 'react-i18next';
import logo from './logo.svg';
import './App.css';

function App() {
  const { t } = useTranslation();
  const { i18n } = useTranslation();
  
  function changeLanguage(e: any) {
    i18n.changeLanguage(e.target.value);
  }

  return (
    <div className="App">
      <header className="App-header">
        <img src={logo} className="App-logo" alt="logo" />
        <p>
          {t('edit')} <code>src/App.tsx</code> {t('saveToReload')}.
        </p>
        <a
          className="App-link"
          href="https://reactjs.org"
          target="_blank"
          rel="noopener noreferrer"
        >
          {t('learnReact')}
        </a>
      </header>
      <div className='footer'>
        <button onClick={changeLanguage} value='en'>{t('english')}</button>
        <button onClick={changeLanguage} value='es'>{t('spanish')}</button>
      </div>
    </div>
  );
}

export default App;
