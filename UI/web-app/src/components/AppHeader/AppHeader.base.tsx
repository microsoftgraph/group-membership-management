// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useEffect, useState } from 'react';
import { classNamesFunction, IButtonStyles, IconButton, IPersonaSharedProps, IProcessedStyleSet, IStyle, Persona, PersonaSize, useTheme } from '@fluentui/react';
import { useNavigate } from 'react-router-dom';
import {
  type IAppHeaderProps,
  type IAppHeaderStyleProps,
  type IAppHeaderStyles,
} from './AppHeader.types';
import { useDispatch, useSelector } from 'react-redux';
import { AppDispatch } from '../../store';
import { selectProfilePhoto } from '../../store/profile.slice';
import { getProfilePhoto } from '../../store/profile.api';
import logo from '../../logo.svg';
import { useStrings } from '../../store/hooks';
import { selectHasAdminCenterPermissions } from '../../store/roles.slice';
import { selectIsDisclaimerEnabled } from '../../store/settings.slice';
import { toggleTheme, selectIsDarkMode } from '../../store/theme.slice';
import { Disclaimer } from '../Disclaimer';
import { jsxFormat } from '../../utils/stringUtils';

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
  const isDisclaimerEnabled = useSelector(selectIsDisclaimerEnabled);
  const isDarkMode = useSelector(selectIsDarkMode);

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

  const handleThemeToggle = () => {
    dispatch(toggleTheme());
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

  return (<>
      <header className={classNames.root}>
        <a href="/" className={classNames.mainButton} onClick={onLogoClicked}>
          <div className={classNames.titleContainer}>
            <div className={classNames.appIcon}>
              <img src={logo} aria-hidden="true" style={{ height: 32, width: 32 }} />
            </div>
            <div className={classNames.appTitle}>{strings.membershipManagement}</div>
          </div>
        </a>
        <div className={classNames.headerControls}>
          <IconButton
            iconProps={{ iconName: isDarkMode ? 'Sunny' : 'ClearNight' }}
            title={isDarkMode ? strings.Components.AppHeader.switchToLightMode : strings.Components.AppHeader.switchToDarkMode}
            ariaLabel={isDarkMode ? strings.Components.AppHeader.switchToLightMode : strings.Components.AppHeader.switchToDarkMode}
            className={classNames.settingsIcon}
            styles={buttonStyles}
            onClick={handleThemeToggle}
          />
          {canViewSettings && (
            <IconButton
              title={strings.Components.AppHeader.settings}
              iconProps={{ iconName: 'settings' }}
              className={classNames.settingsIcon}
              styles={buttonStyles}
              onClick={onSettingsButtonClicked} />
          )}
          {isDisclaimerEnabled && (
            <IconButton
              title={strings.Components.AppHeader.reviewDisclaimer}
              iconProps={{ iconName: 'Info' }}
              className={classNames.settingsIcon}
              styles={buttonStyles}
              onClick={onReviewDisclaimerClicked}
            />
          )}
          <Persona size={PersonaSize.size32} className={classNames.userPersona} {...personaProps} />
        </div>
      </header>
      <>
        {isDisclaimerEnabled && isDisclaimerOpen && (
          <Disclaimer
            checkboxes={[
              { id: 'membershipRules', label: jsxFormat(strings.Disclaimer.membershipRules,<strong>{strings.Disclaimer.membershipRulesBoldNote}</strong>) },
              { id: 'outlookWelcomeMessage', label: strings.Disclaimer.outlookWelcomeMessage },
              { id: 'autoSubscribeSettings',
                label: jsxFormat(
                  strings.Disclaimer.autoSubscribeSettings,
                  <strong>{strings.Disclaimer.membersAutoFollowGroupConversationsOption}</strong>,
                  <strong><a href="https://myaccount.microsoft.com/groups" target="_blank" rel="noopener noreferrer" style={{ color: theme.palette.themePrimary, textDecoration: 'underline' }}>{strings.Disclaimer.myGroupsUI}</a></strong>
                )
              },
              { id: 'authorizedSenders', label: jsxFormat(strings.Disclaimer.authorizedSenders,<strong>{strings.Disclaimer.authorizedSendersBoldNote}</strong>) },
              { id: 'teamsVivaNotifications', label: strings.Disclaimer.teamsVivaNotifications },
              { id: 'flatList', label: strings.Disclaimer.flatList },
            ]}
            onDismiss={closeDisclaimer}
          />
        )}
      </>
    </>
  );
};
