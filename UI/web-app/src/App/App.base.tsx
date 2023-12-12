// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import React, { useEffect } from 'react';

import { useSelector, useDispatch } from 'react-redux';
import { Outlet } from 'react-router-dom';


import {
  type IAppProps,
  type IAppStyleProps,
  type IAppStyles,
} from './App.types';
import { AppHeader } from '../components/AppHeader';
import { Loader } from '../components/Loader';
import { AppDispatch } from '../store';
import { loginAsync } from '../store/account.api';
import { selectLoggedIn } from '../store/account.slice';
import { selectProfile } from '../store/profile.slice';
import { setLanguage } from '../store/localization.api';
import { getIsAdmin } from '../store/roles.api';

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

  const dispatch = useDispatch<AppDispatch>();
  const profile = useSelector(selectProfile);
  const loggedIn = useSelector(selectLoggedIn);
  
  // run once after load.
  useEffect(() => {
    if (!loggedIn) {
      dispatch(loginAsync());
    }
  });

  // run if the localization context change or the user's preferred language changes.
  useEffect(() => {
    dispatch(setLanguage(profile?.userPreferredLanguage));
  }, [dispatch, profile?.userPreferredLanguage]);

  if (loggedIn) {
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
