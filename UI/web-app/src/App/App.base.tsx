// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useMsal } from '@azure/msal-react';
import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import React, { useEffect } from 'react';
import { I18nextProvider, useTranslation } from 'react-i18next';
import { useSelector, useDispatch } from 'react-redux';
import { Outlet } from 'react-router-dom';
import { selectProfile } from '../store/profile.slice';
import { getProfile } from "../store/profile.api";
import {
  type IAppProps,
  type IAppStyleProps,
  type IAppStyles,
} from './App.types';
import { AppHeader } from '../components/AppHeader';
import { type AppDispatch } from '../store';
import { fetchAccount } from '../store/account.api';
import { selectAccount } from '../store/account.slice';
import { Loader } from '../components/Loader';

const getClassNames = classNamesFunction<IAppStyleProps, IAppStyles>();

export const AppBase: React.FunctionComponent<IAppProps> = (
  props: IAppProps
) => {
  const { className, styles } = props;
  const theme = useTheme();
  const classNames: IProcessedStyleSet<IAppStyles> = getClassNames(styles, {
    className,
    theme,
  });
  const { t, i18n } = useTranslation();
  const account = useSelector(selectAccount);
  const profile = useSelector(selectProfile)
  const dispatch = useDispatch<AppDispatch>();
  const context = useMsal();

  useEffect(() => {
    dispatch(fetchAccount(context));
    dispatch(getProfile());
    i18n.changeLanguage(profile.userPreferredLanguage);
  }, [dispatch, profile]);

  if (account != null) {
    return (
      <I18nextProvider i18n={i18n}>
        <div className={classNames.root}>
          <AppHeader />
          <div className={classNames.body}>
            <div className={classNames.content}>
              <Outlet />
            </div>
          </div>
        </div>
      </I18nextProvider>
    );
  } else {
    return (
      <div className={classNames.root}>
        <Loader />
      </div>
    );
  }
};
