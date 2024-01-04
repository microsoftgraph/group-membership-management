// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { PrivacyPolicyLinkBase } from './PrivacyPolicyLink.base';
import { getStyles } from './PrivacyPolicyLink.styles';
import {
  type IPrivacyPolicyLinkProps,
  type IPrivacyPolicyLinkStyleProps,
  type IPrivacyPolicyLinkStyles,
} from './PrivacyPolicyLink.types';

export const PrivacyPolicyLink: React.FunctionComponent<IPrivacyPolicyLinkProps> = styled<
  IPrivacyPolicyLinkProps,
  IPrivacyPolicyLinkStyleProps,
  IPrivacyPolicyLinkStyles
>(PrivacyPolicyLinkBase, getStyles, undefined, {
  scope: 'PrivacyPolicyLink',
});
