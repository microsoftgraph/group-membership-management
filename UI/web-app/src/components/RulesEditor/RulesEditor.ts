// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { RulesEditorBase } from './RulesEditor.base';
import { getStyles } from './RulesEditor.styles';
import {
  type RulesEditorProps,
  type RulesEditorStyleProps,
  type RulesEditorStyles,
} from './RulesEditor.types';

export const RulesEditor: React.FunctionComponent<RulesEditorProps> = styled<
  RulesEditorProps,
  RulesEditorStyleProps,
  RulesEditorStyles
>(RulesEditorBase, getStyles, undefined, {
  scope: 'RulesEditor',
});
