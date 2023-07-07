// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { useSelector } from 'react-redux';
import { selectAccountName } from '../../store/account.slice';
import { useTranslation } from 'react-i18next';
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import {
  IWelcomeNameProps,
  IWelcomeNameStyleProps,
  IWelcomeNameStyles,
} from './WelcomeName.types';

const getClassNames = classNamesFunction<
  IWelcomeNameStyleProps,
  IWelcomeNameStyles
>();

export const WelcomeNameBase: React.FunctionComponent<IWelcomeNameProps> = (
  props
) => {
  const { className, styles } = props;
  const { t } = useTranslation();
  const classNames: IProcessedStyleSet<IWelcomeNameStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  const name: string | undefined = useSelector(selectAccountName);

  if (name) {
    return (
      <div className={classNames.root}>
        {t('welcome')}, {name}
      </div>
    );
  } else {
    return null;
  }
};
