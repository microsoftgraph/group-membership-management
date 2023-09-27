// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  classNamesFunction,
  type IProcessedStyleSet
} from '@fluentui/react';
import { Text } from '@fluentui/react/lib/Text';
import { useTheme } from '@fluentui/react/lib/Theme';
import React from 'react';

import {
  type IPageVersionProps,
  type IPageVersionStyleProps,
  type IPageVersionStyles,
} from './PageVersion.types';
import { useStrings } from '../../localization/hooks';

const getClassNames = classNamesFunction<
  IPageVersionStyleProps,
  IPageVersionStyles
>();

export const PageVersionBase: React.FunctionComponent<IPageVersionProps> = (
  props: IPageVersionProps
) => {
  const { className, styles } = props;
  const classNames: IProcessedStyleSet<IPageVersionStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const versionNumber = `${process.env.REACT_APP_VERSION_NUMBER}` || '1.0.0';
  const strings = useStrings();

  return (
    <div className={classNames.root}>
        {strings.version as string} #{versionNumber}
    </div>
  );
};
