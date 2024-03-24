// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IProcessedStyleSet, type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import type { SettingKey, SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import type { IStrings } from '../../services/localization';

export type AdminConfigStyles = {
  root: IStyle;
  card: IStyle;
  title: IStyle;
  description: IStyle;
  tiles: IStyle;
  bottomContainer: IStyle;
  sourceNameTextField: IStyle;
  customLabelTextField: IStyle;
  defaultColumnSpan: IStyle;
  sourceNameDescriptionContainer: IStyle;
  sourceNameTextFieldContainer: IStyle;
  listOfAttributesTitleDescriptionContainer: IStyle;
  detailsListContainer: IStyle;
  descriptionText: IStyle;
};

export type AdminConfigStyleProps = {
  className?: string;
  theme: ITheme;
};

export type AdminConfigProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<AdminConfigStyleProps, AdminConfigStyles>;
};

export type AdminConfigViewProps = AdminConfigProps & {
  isSaving: boolean;
  onSave: (settings: { readonly [key in SettingKey]: string }, sqlMembershipSource: SqlMembershipSource | undefined, sqlMembershipAttributes: SqlMembershipAttribute[] | undefined) => void;
  settings: { readonly [key in SettingKey]: string };
  sqlMembershipSource: SqlMembershipSource | undefined;
  sqlMembershipSourceAttributes: SqlMembershipAttribute[] | undefined;
  strings: IStrings['AdminConfig'];
  isHyperlinkAdmin: boolean;
  isCustomMembershipProviderAdmin: boolean;
};

export type HyperlinkSettingsProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: IStrings['AdminConfig'];
  settings: { readonly [key in SettingKey]: string };
  setSettings: React.Dispatch<React.SetStateAction<{ readonly [key in SettingKey]: string }>>;
  setHasValidationErrors: React.Dispatch<React.SetStateAction<boolean>>;
};

export type CustomSourceSettingsProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  sqlMembershipSource: SqlMembershipSource | undefined;
  sqlMembershipSourceAttributes: SqlMembershipAttribute[] | undefined;
  strings: IStrings['AdminConfig'];
  setNewSource: React.Dispatch<React.SetStateAction<SqlMembershipSource | undefined>>;
  setNewAttributes: React.Dispatch<React.SetStateAction<SqlMembershipAttribute[] | undefined>>;
};

export type CustomLabelCellProps = {
  value: string;
  placeholder: string;
  onChange: ((event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string | undefined) => void) | undefined;
  className: string;
};