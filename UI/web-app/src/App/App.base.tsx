// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import React, { useEffect } from 'react';

import { useSelector, useDispatch } from 'react-redux';
import { Outlet } from 'react-router-dom';
import { Text } from '@fluentui/react/lib/Text';
import { type IAppProps, type IAppStyleProps, type IAppStyles } from './App.types';
import { AppHeader } from '../components/AppHeader';
import { Loader } from '../components/Loader';
import { AppDispatch } from '../store';
import { loginAsync } from '../store/account.api';
import { selectLoggedIn } from '../store/account.slice';
import { useStrings } from '../store/hooks';
import { selectProfile } from '../store/profile.slice';
import { setLanguage } from '../store/localization.api';
import { fetchSettings } from '../store/settings.api';
import { AppFooter } from '../components/AppFooter';
import { fetchDefaultSqlMembershipSource, fetchDefaultSqlMembershipSourceAttributes } from '../store/sqlMembershipSources.api';
import { selectHasAccess, selectIsFetchingRoles } from '../store/roles.slice';

const getClassNames = classNamesFunction<IAppStyleProps, IAppStyles>();

export const AppBase: React.FunctionComponent<IAppProps> = (props: IAppProps) => {
  const { className, styles } = props;
  const theme = useTheme();
  const strings = useStrings();
  const classNames: IProcessedStyleSet<IAppStyles> = getClassNames(styles, {
    className,
    theme,
  });

  const dispatch = useDispatch<AppDispatch>();
  const profile = useSelector(selectProfile);
  const loggedIn = useSelector(selectLoggedIn);
  const hasAccess = useSelector(selectHasAccess);
  const isFetchingRoles = useSelector(selectIsFetchingRoles);

  // run once after load.
  useEffect(() => {
    if (!loggedIn) {
      dispatch(loginAsync());
    }
  }, [dispatch, loggedIn]);

  useEffect(() => {
    if (loggedIn) {
      dispatch(fetchSettings());
      dispatch(fetchDefaultSqlMembershipSource());
      dispatch(fetchDefaultSqlMembershipSourceAttributes());
    }
  }, [dispatch, loggedIn]);

  // run if the localization context change or the user's preferred language changes.
  useEffect(() => {
    dispatch(setLanguage(profile?.userPreferredLanguage));
  }, [dispatch, profile?.userPreferredLanguage]);

  const showLoader = !loggedIn || isFetchingRoles;

  if (showLoader) {
    return (
      <div className={classNames.root}>
        <Loader />
      </div>
    );
  } else {
    return (
      <div className={classNames.root}>
        <AppHeader />
        <div className={classNames.content}>
          {hasAccess ?
            <Outlet /> :
            <div className={classNames.permissionDenied}>
              <Text>{strings.permissionDenied}</Text>
            </div>
          }
        </div>
        <AppFooter />
      </div>
    );
  }
};
