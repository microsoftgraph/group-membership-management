// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { classNamesFunction, Link, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import React from 'react';

import {
  type IPrivacyPolicyLinkProps,
  type IPrivacyPolicyLinkStyleProps,
  type IPrivacyPolicyLinkStyles,
} from './PrivacyPolicyLink.types';
import { useStrings } from '../../store/hooks';
import { useSelector } from 'react-redux';
import { selectPrivacyPolicyUrl } from '../../store/settings.slice';

const getClassNames = classNamesFunction<IPrivacyPolicyLinkStyleProps, IPrivacyPolicyLinkStyles>();

export const PrivacyPolicyLinkBase: React.FunctionComponent<IPrivacyPolicyLinkProps> = (
  props: IPrivacyPolicyLinkProps
) => {
  const { className, styles } = props;
  const classNames: IProcessedStyleSet<IPrivacyPolicyLinkStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const privacyPolicyUrl = useSelector(selectPrivacyPolicyUrl);
  const strings = useStrings();

  return !privacyPolicyUrl || privacyPolicyUrl === '' ? null : (
    <div className={classNames.root}>
      <Link className={classNames.link} href={privacyPolicyUrl} target='_blank'>{strings.privacyPolicy}</Link>
    </div>
  );
};
