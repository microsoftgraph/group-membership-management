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
import { useEffect, useState } from 'react';
import { selectProfilePhoto } from '../../store/profile.slice';
import { getProfilePhoto } from '../../store/profile.api';
import logo from '../../logo.svg';
import { useStrings } from '../../store/hooks';
import { selectHasAdminCenterPermissions } from '../../store/roles.slice';
import { Disclaimer } from '../Disclaimer';

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
  const canViewSettings = useSelector(selectHasAdminCenterPermissions);

  useEffect(() => {
    if (!profilePhoto) {
      dispatch(getProfilePhoto());
    }
  }, [dispatch, profilePhoto]);

  const navigate = useNavigate();

  const onSettingsButtonClicked = (): void => {
    navigate('/Admin', { replace: false, state: { item: 1 } });
  };

  const onLogoClicked = () => {
    navigate('/', { replace: false, state: { item: 1 } });
  };

  const [isDisclaimerOpen, setIsDisclaimerOpen] = useState(false);

  const onReviewDisclaimerClicked = (): void => {
    setIsDisclaimerOpen(true);
  };

  const closeDisclaimer = (): void => {
    setIsDisclaimerOpen(false);
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
      {
        canViewSettings &&
        <div className={classNames.settingsContainer}>
          <IconButton
            title={strings.Components.AppHeader.settings}
            iconProps={{ iconName: 'settings' }}
            className={classNames.settingsIcon}
            styles={buttonStyles}
            onClick={onSettingsButtonClicked} />
          <Persona size={PersonaSize.size32} className={classNames.userPersona} {...personaProps} />
          <IconButton
            title="Review Disclaimer"
            iconProps={{ iconName: 'Info' }}
            className={classNames.settingsIcon}
            styles={buttonStyles}
            onClick={onReviewDisclaimerClicked} />
        </div>
      }
      {isDisclaimerOpen && (
        <Disclaimer
          checkboxes={[
            { id: 'outlookWelcomeMessage', label: strings.Disclaimer.outlookWelcomeMessage },
            { id: 'autoSubscribeSettings', label: strings.Disclaimer.autoSubscribeSettings },
            { id: 'authorizedSenders', label: strings.Disclaimer.authorizedSenders },
            { id: 'globalHelpDesk', label: strings.Disclaimer.globalHelpDesk },
            { id: 'teamsVivaNotifications', label: strings.Disclaimer.teamsVivaNotifications },
            { id: 'membershipRules', label: strings.Disclaimer.membershipRules },
            { id: 'supportedGroups', label: strings.Disclaimer.supportedGroups },
            { id: 'flatList', label: strings.Disclaimer.flatList },
          ]}
          onDismiss={closeDisclaimer}
        />
      )}
    </header>
  );
};
