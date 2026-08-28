// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IProcessedStyleSet, type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import type { SettingKey, SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import type { AlertBannerConfig } from '../../models/AlertBannerConfig';
import type { IStrings } from '../../services/localization';
import { MouseEventHandler } from 'react';

export type AdminConfigStyles = {
  root: IStyle;
  card: IStyle;
  titleRow: IStyle;
  saveButton: IStyle;
  title: IStyle;
  description: IStyle;
  tiles: IStyle;
  sourceNameTextField: IStyle;
  customLabelTextField: IStyle;
  defaultColumnSpan: IStyle;
  sourceNameTextFieldContainer: IStyle;
  detailsListContainer: IStyle;
  descriptionText: IStyle;
  valuesDropdown: IStyle;
  valuesDropdownTitle: IStyle;
  valuesDropdownSpinner: IStyle;
  descriptionTextField: IStyle;
  aiSettingsLeaveEmptyNote: IStyle;
  aiSettingsLeaveEmptyNoteIcon: IStyle;
  aiSettingsDefaultInstructionsContainer: IStyle;
  aiSettingsDefaultInstructionsToggle: IStyle;
  aiSettingsDefaultInstructionsToggleIcon: IStyle;
  aiSettingsDefaultInstructionsContent: IStyle;
  sectionContainer: IStyle;
  sectionContainerDivided: IStyle;
  modelBehaviorCard: IStyle;
  modelBehaviorHeader: IStyle;
  modelBehaviorTitle: IStyle;
  modelBehaviorSlider: IStyle;
  modelBehaviorDescription: IStyle;
  sectionHeading: IStyle;
  sectionSubtitle: IStyle;
  settingsGrid: IStyle;
  suggestedPromptsGrid: IStyle;
  suggestedPromptRow: IStyle;
  suggestedPromptLabelField: IStyle;
  suggestedPromptPromptField: IStyle;
  suggestedPromptRemoveButton: IStyle;
  suggestedPromptsActions: IStyle;
  suggestedPromptAddButton: IStyle;
  operationsGrid: IStyle;
  serviceNotificationCard: IStyle;
  serviceNotificationTitle: IStyle;
  serviceNotificationDescription: IStyle;
  serviceNotificationToggle: IStyle;
  serviceNotificationFieldRow: IStyle;
  serviceNotificationTextFieldGroup: IStyle;
  serviceNotificationErrorMessage: IStyle;
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
  onSave: (settings: { readonly [key in SettingKey]: string }, sqlMembershipSource: SqlMembershipSource | undefined, sqlMembershipAttributes: SqlMembershipAttribute[] | undefined, serviceNotification: AlertBannerConfig) => void;
  handleGetValues: (attribute: SqlMembershipAttribute) => void;
  settings: { readonly [key in SettingKey]: string };
  sqlMembershipSource: SqlMembershipSource | undefined;
  sqlMembershipSourceAttributes: SqlMembershipAttribute[] | undefined;
  serviceNotification: AlertBannerConfig;
  serviceNotificationSaveError?: string;
  strings: IStrings['AdminConfig'];
  isCustomMembershipProviderAdmin: boolean;
  isOperationsResetAdministrator: boolean;
  isGeneralSettingsAdministrator: boolean;
  isAutoApproverAdministrator: boolean;
  isAISettingsAdministrator: boolean;
  defaultAIPrompt: string;
};

export type UserResourcesSettingsProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: IStrings['AdminConfig'];
  settings: { readonly [key in SettingKey]: string };
  setSettings: React.Dispatch<React.SetStateAction<{ readonly [key in SettingKey]: string }>>;
  setHasValidationErrors: React.Dispatch<React.SetStateAction<boolean>>;
};

export type OperationsProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: IStrings['AdminConfig'];
};

export type GeneralSettingsProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: IStrings['AdminConfig'];
  settings: { readonly [key in SettingKey]: string };
  setSettings: React.Dispatch<React.SetStateAction<{ readonly [key in SettingKey]: string }>>;
  setHasValidationErrors: React.Dispatch<React.SetStateAction<boolean>>;
  serviceNotification: AlertBannerConfig;
  setServiceNotification: React.Dispatch<React.SetStateAction<AlertBannerConfig>>;
  serviceNotificationErrors: Record<string, string>;
  serviceNotificationSaveError?: string;
};

export type ServiceNotificationSettingsProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: IStrings['AdminConfig'];
  config: AlertBannerConfig;
  setConfig: React.Dispatch<React.SetStateAction<AlertBannerConfig>>;
  errors: Record<string, string>;
  saveError?: string;
};

export type AutoApproverSettingsProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: IStrings['AdminConfig'];
  settings: { readonly [key in SettingKey]: string };
  setSettings: React.Dispatch<React.SetStateAction<{ readonly [key in SettingKey]: string }>>;
};

export type SettingsSectionProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  title: string;
  subtitle?: string;
  showDivider?: boolean;
  children: React.ReactNode;
};

export type AISettingsProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: IStrings['AdminConfig'];
  settings: { readonly [key in SettingKey]: string };
  setSettings: React.Dispatch<React.SetStateAction<{ readonly [key in SettingKey]: string }>>;
  defaultAIPrompt: string;
};

export type CustomSourceSettingsProps = {
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  sqlMembershipSource: SqlMembershipSource | undefined;
  sqlMembershipSourceAttributes: SqlMembershipAttribute[] | undefined;
  strings: IStrings['AdminConfig'];
  setNewSource: React.Dispatch<React.SetStateAction<SqlMembershipSource | undefined>>;
  setNewAttributes: React.Dispatch<React.SetStateAction<SqlMembershipAttribute[] | undefined>>;
  setHasValidationErrors: React.Dispatch<React.SetStateAction<boolean>>;
  handleGetValues: (attribute: SqlMembershipAttribute) => void;
};

export type NullThresholdCellProps = {
  attributeName: string;
  storedValue: number | undefined;
  title: string;
  ariaLabel: string;
  placeholder: string;
  validationErrorMessage: string;
  onValueChange: (attributeName: string, raw: string, isValid: boolean) => void;
};

export type CustomLabelCellProps = {
  value: string;
  placeholder: string;
  onChange: ((event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string | undefined) => void) | undefined;
  className: string;
};

export type AttributeValuesCellProps = {
  values: string[];
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: IStrings['AdminConfig'];
  onDropdownClick: MouseEventHandler<HTMLDivElement> | undefined;
};
