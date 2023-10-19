// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  IButtonProps,
  IButtonStyles,
  IProcessedStyleSet,
  IStyle,
  IconButton,
  Link,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import { IBannerProps, IBannerStyleProps, IBannerStyles } from './Banner.types';
import { InfoIcon } from '@fluentui/react-icons-mdl2';
import { useStrings } from '../../localization';

import { fetchSettingByKey } from '../../store/settings.api';
import { selectSelectedSetting } from '../../store/settings.slice';
import { AppDispatch } from '../../store';

const getClassNames = classNamesFunction<IBannerStyleProps, IBannerStyles>();

export const BannerBase: React.FunctionComponent<IBannerProps> = (props) => {
  const { className, styles } = props;
  const strings = useStrings();
  const [collapsed, setCollapsed] = useState(false);
  const theme = useTheme();
  const dispatch = useDispatch<AppDispatch>();

  useEffect(() => {
    dispatch(fetchSettingByKey('dashboardUrl'));
  }, [dispatch]);

  const dashboardUrl = useSelector(selectSelectedSetting);

  const openLink = (): void => {
    window.open(dashboardUrl?.value, '_blank', 'noopener,noreferrer');
  };

  const classNames: IProcessedStyleSet<IBannerStyles> = getClassNames(styles, {
    collapsed,
    className,
    theme
  });

  const buttonProps: IButtonProps = {
    iconProps: {
      iconName: collapsed ? 'ChevronRight' : 'ChevronLeft',
    },
  };

  const handleToggle = () => setCollapsed(!collapsed);

  const disabledStyles: IStyle = {
    background: theme.palette.themeLight,
    color: theme.semanticColors.bodyText
  }
  const buttonStyles: IButtonStyles = {
    rootHovered: disabledStyles,
    rootPressed: disabledStyles
  }

  return (
    <div className={classNames.root}>
      {!collapsed && (
        <div className={classNames.messageContainer}>
          <InfoIcon className={classNames.icon} />
          <div className={classNames.message}>
            {strings.bannerMessageStart}
            <Link
              href={dashboardUrl?.value ?? ''}
              onClick={() => openLink()}
              underline={true}
              className={classNames.link}>
              {strings.clickHere}
            </Link>
            {strings.bannerMessageEnd}
          </div>
        </div>
      )}
      <IconButton
        {...buttonProps}
        className={classNames.toggle}
        styles={buttonStyles}
        onClick={handleToggle}
      />
    </div>
  );
};
