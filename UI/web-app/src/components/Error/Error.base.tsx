// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from "react";
import { useTranslation } from 'react-i18next';
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import {
  IErrorProps,
  IErrorStyleProps,
  IErrorStyles,
} from './Error.types';

const getClassNames = classNamesFunction<
  IErrorStyleProps,
  IErrorStyles
>();

export const ErrorBase: React.FunctionComponent<IErrorProps> = (props) => {
  const { className, styles } = props;
  const { t } = useTranslation();
  const classNames: IProcessedStyleSet<IErrorStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  return (
    <div className={classNames.root}>
      <div className={classNames.text}> {t('errorItemNotFound')} </div>
    </div>
  );
};
