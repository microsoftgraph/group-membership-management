// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  classNamesFunction,
  type IProcessedStyleSet,
  type IIconProps
} from '@fluentui/react';
import { CommandBarButton } from '@fluentui/react/lib/Button';
import { Stack, type IStackStyles } from '@fluentui/react/lib/Stack';
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

const stackStyles: Partial<IStackStyles> = { root: { height: 44 } };
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
      !backButtonHidden && <Stack horizontal styles={stackStyles}>
        <CommandBarButton
          iconProps={leftArrowIcon}
          text={t('back') as string}
          onClick={backButtonOnClick}
        />
      </Stack>
      }
      {children}
    </Stack>
  );
};
