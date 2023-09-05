// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  classNamesFunction,
  type IProcessedStyleSet
} from '@fluentui/react';
import { Text } from '@fluentui/react/lib/Text';
import { useTheme } from '@fluentui/react/lib/Theme';
import React from 'react';
import { useTranslation } from 'react-i18next';

import {
  type IPageVersionProps,
  type IPageVersionStyleProps,
  type IPageVersionStyles,
} from './PageVersion.types';

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
  const { t } = useTranslation();

  return (
    <div className={classNames.root}>
        {t('version') as string} #{versionNumber}
    </div>
  );
};
