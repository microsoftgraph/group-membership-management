// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.import React from "react";

import { classNamesFunction, IButtonStyles, IconButton, IPersonaSharedProps, IProcessedStyleSet, IStyle, Persona, PersonaSize, useTheme } from '@fluentui/react';
import { useNavigate } from 'react-router-dom';
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
import logo from '../../logo.svg';
import { useStrings } from '../../store/hooks';

const getClassNames = classNamesFunction<
  IAppHeaderStyleProps,
  IAppHeaderStyles
>();

export const AppHeaderBase: React.FunctionComponent<IAppHeaderProps> = (
  props: IAppHeaderProps
) => {
  const { className, styles } = props;
  const theme = useTheme();
  const strings = useStrings();
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

  const navigate = useNavigate();

  const onSettingsButtonClicked = (): void => {
    navigate('/AdminConfig', { replace: false, state: { item: 1 } });
  };
  
  const onLogoClicked = () => {
    navigate('/', { replace: false, state: { item: 1 } });
  };

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
      <a href="/" className={classNames.mainButton} onClick={onLogoClicked}>
        <div className={classNames.titleContainer}>
          <div className={classNames.appIcon}>
            <img src={logo} alt="Membership Management Icon" style={{ height: 32, width: 32 }} />
          </div>
          <div className={classNames.appTitle}>{strings.membershipManagement}</div>
        </div>
      </a>
      <div className={classNames.settingsContainer}>
        <IconButton
          title={strings.Components.AppHeader.settings}
          iconProps={{ iconName: 'settings' }}
          className={classNames.settingsIcon}
          styles={buttonStyles}
          onClick={onSettingsButtonClicked} />
        <Persona size={PersonaSize.size32} className={classNames.userPersona} {...personaProps} />
      </div>
    </header>
  );
};
