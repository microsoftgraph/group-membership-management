// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import React from 'react';

import {
  type IPageProps,
  type IPageStyleProps,
  type IPageStyles,
} from './Page.types';
import { PrivacyPolicyLink } from '../PrivacyPolicyLink';

const getClassNames = classNamesFunction<IPageStyleProps, IPageStyles>();

export const PageBase: React.FunctionComponent<IPageProps> = (
  props: IPageProps
) => {
  const { children, className, styles } = props;
  const classNames: IProcessedStyleSet<IPageStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  return (
    <div className={classNames.root}>
      {children}
      <PrivacyPolicyLink className={classNames.privacyPolicy} />
    </div>);
};
