// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useMsal } from '@azure/msal-react';
import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import React, { useEffect } from 'react';

import { useSelector, useDispatch } from 'react-redux';
import { Outlet } from 'react-router-dom';
import { selectProfile } from '../store/profile.slice';
import { getProfile } from '../store/profile.api';
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
import { useLocalization } from '../localization';

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
  const account = useSelector(selectAccount);
  const profile = useSelector(selectProfile);
  const dispatch = useDispatch<AppDispatch>();
  const context = useMsal();
  const localization = useLocalization();

  // run once after load.
  useEffect(() => {
    if (!account) {
      dispatch(fetchAccount(context));
      dispatch(getProfile());
    }
  });

  // run if the localization context change or the user's preferred language changes.
  useEffect(() => {
    localization.setUserPreferredLanguage(profile?.userPreferredLanguage);
  }, [localization, profile?.userPreferredLanguage]);

  if (account != null) {
    return (
      <div className={classNames.root}>
        <AppHeader />
        <div className={classNames.body}>
          <div className={classNames.content}>
            <Outlet />
          </div>
        </div>
      </div>
    );
  } else {
    return (
      <div className={classNames.root}>
        <Loader />
      </div>
    );
  }
};
