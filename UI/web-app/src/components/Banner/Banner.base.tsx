// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import {
  IButtonProps,
  IButtonStyles,
  IProcessedStyleSet,
  IStyle,
  IconButton,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import { IBannerProps, IBannerStyleProps, IBannerStyles } from './Banner.types';
import { InfoIcon } from '@fluentui/react-icons-mdl2';
import { useStrings } from '../../localization/hooks';

const getClassNames = classNamesFunction<IBannerStyleProps, IBannerStyles>();

export const BannerBase: React.FunctionComponent<IBannerProps> = (props) => {
  const { className, styles } = props;
  const strings = useStrings();
  const [collapsed, setCollapsed] = useState(false);
  const theme = useTheme();

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
          <div className={classNames.message}>{strings.bannerMessage}</div>
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
