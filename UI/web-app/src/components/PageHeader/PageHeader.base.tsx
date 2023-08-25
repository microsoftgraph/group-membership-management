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
import { useTranslation } from 'react-i18next';

import {
  type IPageHeaderProps,
  type IPageHeaderStyleProps,
  type IPageHeaderStyles,
} from './PageHeader.types';

const getClassNames = classNamesFunction<
  IPageHeaderStyleProps,
  IPageHeaderStyles
>();

const leftArrowIcon: IIconProps = { iconName: 'ChevronLeftMed' };

export const PageHeaderBase: React.FunctionComponent<IPageHeaderProps> = (
  props: IPageHeaderProps
) => {
  const { backButtonHidden, children, className, styles } = props;
  const classNames: IProcessedStyleSet<IPageHeaderStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const { t } = useTranslation();

  const navigate = useNavigate();

  const backButtonOnClick = (): void => {
    navigate(-1);
  };

  return (
    <Stack className={classNames.root}>
      {
        !backButtonHidden &&
        <Stack horizontalAlign="start" verticalAlign='center'> 
          <ActionButton
            className={classNames.backButton}
            iconProps={leftArrowIcon}
            text={t('back') as string}
            onClick={backButtonOnClick}
          />
          <div className={classNames.separator}></div>
        </Stack>
      }
      {children}
    </Stack>
  );
};
