// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { FontSizes, FontWeights } from '@fluentui/react';
import { type IPrivacyPolicyLinkStyleProps, type IPrivacyPolicyLinkStyles } from './PrivacyPolicyLink.types';

export const getStyles = (props: IPrivacyPolicyLinkStyleProps): IPrivacyPolicyLinkStyles => {
  const { className } = props;

  return {
    root: [
      {
        fontSize: FontSizes.small,
        fontWeight: FontWeights.regular,
      },
      className,
    ],
  };
};
