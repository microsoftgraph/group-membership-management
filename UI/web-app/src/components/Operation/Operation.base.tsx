// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type {
  OperationProps,
  OperationStyleProps,
  OperationStyles,
} from './Operation.types';
import { useStrings } from '../../store/hooks';

export const getClassNames = classNamesFunction<OperationStyleProps, OperationStyles>();

export const OperationBase: React.FunctionComponent<OperationProps> = (props: OperationProps) => {
  const { title, className, styles } = props;
  const classNames: IProcessedStyleSet<OperationStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  return (
    <div className={classNames.card}>
      <div className={classNames.title}>{title}</div>
    </div>
  );
};