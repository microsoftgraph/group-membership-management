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
  type IPageFooterProps,
  type IPageFooterStyleProps,
  type IPageFooterStyles,
} from './PageFooter.types';

const getClassNames = classNamesFunction<
  IPageFooterStyleProps,
  IPageFooterStyles
>();

export const PageFooterBase: React.FunctionComponent<IPageFooterProps> = (
  props: IPageFooterProps
) => {
  const { className, styles } = props;
  const classNames: IProcessedStyleSet<IPageFooterStyles> = getClassNames(
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
      <Text variant='small'>
        {t('version') as string} #{versionNumber}
      </Text>
    </div>
  );
};
