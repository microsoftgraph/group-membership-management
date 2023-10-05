// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from "react";
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
import { useStrings } from "../../localization/hooks";

const getClassNames = classNamesFunction<
  IErrorStyleProps,
  IErrorStyles
>();

export const ErrorBase: React.FunctionComponent<IErrorProps> = (props) => {
  const { className, styles } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<IErrorStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  return (
    <div className={classNames.root}>
      <div className={classNames.text}> {strings.errorItemNotFound} </div>
    </div>
  );
};
