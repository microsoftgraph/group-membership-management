// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from "react";
import { Spinner, SpinnerSize } from "@fluentui/react";
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import {
  ILoaderProps,
  ILoaderStyleProps,
  ILoaderStyles,
} from './Loader.types';
import { useStrings } from '../../localization/hooks';

const getClassNames = classNamesFunction<
  ILoaderStyleProps,
  ILoaderStyles
>();

export const LoaderBase: React.FunctionComponent<ILoaderProps> = (props) => {
  const { className, styles } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<ILoaderStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  return (
    <div className={classNames.root}>
      <Spinner size={SpinnerSize.large} className={classNames.spinner}/>
      <div className={classNames.text} >{strings.loading}...</div>
    </div>
  );
};
