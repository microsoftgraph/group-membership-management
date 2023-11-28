// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { RunConfigurationBase } from './RunConfiguration.base';
import { getStyles } from './RunConfiguration.styles';
import {
  type IRunConfigurationProps,
  type IRunConfigurationStyleProps,
  type IRunConfigurationStyles,
} from './RunConfiguration.types';

export const RunConfiguration: React.FunctionComponent<IRunConfigurationProps> = styled<
  IRunConfigurationProps,
  IRunConfigurationStyleProps,
  IRunConfigurationStyles
>(RunConfigurationBase, getStyles, undefined, {
  scope: 'RunConfiguration',
});

RunConfiguration.displayName = "StyledRunConfigurationBase";