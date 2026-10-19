// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import { NewGroupType } from '../../models/NewGroupType';

export interface ICreateGroupStyles {
  root: IStyle;
  textField: IStyle;
  alias: IStyle;
  help: IStyle;
  label: IStyle;
}

export interface ICreateGroupStyleProps {
  className?: string;
  theme: ITheme;
}

export interface ICreateGroupProps extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<ICreateGroupStyleProps, ICreateGroupStyles>;
  groupType: NewGroupType;
  onDismiss: () => void;
  onGroupCreated: (groupName: string, groupAlias: string, groupType: NewGroupType, description: string) => void | Promise<void>;
};
