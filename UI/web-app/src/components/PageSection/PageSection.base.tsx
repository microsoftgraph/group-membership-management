// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react';

import {
  type IPageSectionProps,
  type IPageSectionStyleProps,
  type IPageSectionStyles,
} from './PageSection.types';

const getClassNames = classNamesFunction<
  IPageSectionStyleProps,
  IPageSectionStyles
>();

export const PageSectionBase: React.FunctionComponent<IPageSectionProps> = (
  props: IPageSectionProps
) => {
  const { children, className, styles } = props;
  const classNames: IProcessedStyleSet<IPageSectionStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  return <div className={classNames.root}>{children}</div>;
};
