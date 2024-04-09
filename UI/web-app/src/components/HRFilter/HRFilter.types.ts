// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { SqlMembershipAttribute } from '../../models';

export type HRFilterStyles = {
  root: IStyle;
  textFieldGroup: IStyle;
  textField: IStyle;
  labelContainer: IStyle;
  dropdownTitle: IStyle;
  comboBoxHover: IStyle;
  comboBoxTitle: IStyle;
  horizontalChoiceGroup: IStyle;
  horizontalChoiceGroupContainer: IStyle;
  removeButton: IStyle;
  error: IStyle;
};

export type HRFilterStyleProps = {
  className?: string;
  theme: ITheme;
};

type ChildType = {
  filter: string;
};

export type HRFilterProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<HRFilterStyleProps, HRFilterStyles>;
  source: HRSourcePartSource;
  filter: any;
  key: number;
  index: number;
  partId: number;
  attributes: SqlMembershipAttribute[];
  childFilters: ChildType[];
  parentFilter: string | undefined;
  onSourceChange: (source: HRSourcePartSource, partId: number) => void;
  onRemove: () => void;
};