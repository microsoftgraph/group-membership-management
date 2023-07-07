// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.import React from "react";

import { classNamesFunction, IButtonStyles, IconButton, IPersonaSharedProps, IProcessedStyleSet, IStyle, Persona, PersonaSize, useTheme } from '@fluentui/react';
import { useTranslation } from 'react-i18next';
import {
  AccountManagementIcon,
} from '@fluentui/react-icons-mdl2';
import {
  type IAppHeaderProps,
  type IAppHeaderStyleProps,
  type IAppHeaderStyles,
} from './AppHeader.types';
import { useDispatch, useSelector } from 'react-redux';
import { AppDispatch } from '../../store';
import { useEffect } from 'react';
import { selectProfilePhoto } from '../../store/profile.slice';
import { getProfilePhoto } from '../../store/profile.api';

const getClassNames = classNamesFunction<
  IAppHeaderStyleProps,
  IAppHeaderStyles
>();

export const AppHeaderBase: React.FunctionComponent<IAppHeaderProps> = (
  props: IAppHeaderProps
) => {
  const { className, styles } = props;
  const theme = useTheme();
  const { t } = useTranslation();
  const classNames: IProcessedStyleSet<IAppHeaderStyles> = getClassNames(
    styles,
    {
      className,
      theme
    }
  );

  const dispatch = useDispatch<AppDispatch>();
  const profilePhoto = useSelector(selectProfilePhoto);

  useEffect(() => {
    if (!profilePhoto) {
      dispatch(getProfilePhoto());
    }
  }, [dispatch, profilePhoto]);
  
  const personaProps: IPersonaSharedProps = {
    imageUrl: profilePhoto
  }

  const disabledStyles: IStyle = {
    background: theme.palette.themePrimary,
    color: theme.palette.white
  }
  const buttonStyles: IButtonStyles = {
    rootHovered: disabledStyles,
    rootPressed: disabledStyles
  }

  return (
    <header className={classNames.root}>
      <div className={classNames.titleContainer}>
        <div className={classNames.appIcon}>
          <AccountManagementIcon />
        </div>
        <div className={classNames.appTitle}>{t('membershipManagement')}</div>
      </div>
      <div className={classNames.settingsContainer}>
          <IconButton iconProps={{iconName: 'settings'}} className={classNames.settingsIcon} styles={buttonStyles} />
          <Persona size={PersonaSize.size32} className={classNames.userPersona} {...personaProps} />
      </div>
    </header>
  );
};
