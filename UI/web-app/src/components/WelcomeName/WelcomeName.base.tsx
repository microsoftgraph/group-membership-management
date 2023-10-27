// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { useSelector } from 'react-redux';
import { selectAccountName } from '../../store/account.slice';
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
import { useStrings } from '../../localization';

const getClassNames = classNamesFunction<
  IWelcomeNameStyleProps,
  IWelcomeNameStyles
>();

export const WelcomeNameBase: React.FunctionComponent<IWelcomeNameProps> = (
  props
) => {
  const { className, styles } = props;
  const strings = useStrings();
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
        {strings.welcome}, {name}
      </div>
    );
  } else {
    return null;
  }
};
