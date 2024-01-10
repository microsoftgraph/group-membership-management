// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  classNamesFunction,
  type IProcessedStyleSet,
  type IIconProps
} from '@fluentui/react';
import { ActionButton } from '@fluentui/react/lib/Button';
import { Stack } from '@fluentui/react/lib/Stack';
import { useTheme } from '@fluentui/react/lib/Theme';
import React from 'react';
import { useNavigate } from 'react-router-dom';

import {
  type IPageHeaderProps,
  type IPageHeaderStyleProps,
  type IPageHeaderStyles,
} from './PageHeader.types';
import { useStrings } from '../../store/hooks';
import { Banner } from '../Banner';

const getClassNames = classNamesFunction<
  IPageHeaderStyleProps,
  IPageHeaderStyles
>();

const leftArrowIcon: IIconProps = { iconName: 'ChevronLeftMed' };

export const PageHeaderBase: React.FunctionComponent<IPageHeaderProps> = (
  props: IPageHeaderProps
) => {
  const { backButtonHidden, onBackToDashboardButtonClick, children, className, styles } = props;
  const classNames: IProcessedStyleSet<IPageHeaderStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const strings = useStrings();

  const navigate = useNavigate();

  const backButtonOnClick = (): void => {
    navigate('/');
  };

  return (
    <Stack className={classNames.root}>
      {
        !backButtonHidden &&
        <Stack verticalAlign='center'>
        <Stack className={classNames.actionButtonsContainer} horizontal horizontalAlign="space-between" verticalAlign='center'>
          <ActionButton
            className={classNames.backButton}
            iconProps={leftArrowIcon}
            text={strings.backToDashboard}
            onClick={onBackToDashboardButtonClick ?? backButtonOnClick}
          />
          <Banner />
        </Stack>
        <div className={classNames.separator}></div>
      </Stack>
      }
      {children}
    </Stack>
  );
};
