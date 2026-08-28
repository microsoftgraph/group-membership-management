// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { DatePicker, MessageBar, MessageBarType, TextField, Toggle } from '@fluentui/react';
import { AlertBannerConfig } from '../../models/AlertBannerConfig';
import { ServiceNotificationSettingsProps } from './AdminConfig.types';
import type { IStrings } from '../../services/localization';

export const MAX_NOTIFICATION_MESSAGE_LENGTH = 250;

export const toDate = (value: string): Date | undefined => {
  const date = new Date(value);
  return isNaN(date.getTime()) ? undefined : date;
};

/**
 * Client-side validation mirroring the server rules on PATCH /settings/alertBanner.
 * Returns a map of field -> error message; an empty object means the configuration is valid.
 */
export const validateServiceNotification = (
  config: AlertBannerConfig,
  strings: IStrings['AdminConfig']
): Record<string, string> => {
  const errors: Record<string, string> = {};
  const { errors: errorStrings } = strings.ServiceNotifications;

  if (config.isEnabled && config.message.trim().length === 0) {
    errors.message = errorStrings.messageRequired;
  }
  if (config.message.length > MAX_NOTIFICATION_MESSAGE_LENGTH) {
    errors.message = errorStrings.messageTooLong;
  }

  const start = toDate(config.startDate);
  const end = toDate(config.endDate);
  if (!start || !end) {
    errors.dates = errorStrings.datesRequired;
  } else if (start >= end) {
    errors.dates = errorStrings.dateOrder;
  }

  if (config.linkUrl && config.linkUrl.trim().length > 0 && !/^https:\/\//i.test(config.linkUrl.trim())) {
    errors.linkUrl = errorStrings.linkUrlInvalid;
  }

  return errors;
};

/**
 * Service notification (site-wide alert banner) settings, rendered within the General tab.
 * The draft configuration lives in the parent view so that the page level Save button
 * persists it alongside the other Admin Configuration settings.
 * Visible to GENERAL_SETTINGS readers; canEdit is false for view-only holders, which disables
 * every control here. The API rejects the write regardless.
 */
export const ServiceNotificationSettings: React.FunctionComponent<ServiceNotificationSettingsProps> = (
  props: ServiceNotificationSettingsProps
) => {
  const { classNames, strings, config, setConfig, errors, saveError, canEdit } = props;
  const labels = strings.ServiceNotifications.labels;

  const update = (partial: Partial<AlertBannerConfig>) => {
    setConfig((previous) => ({ ...previous, ...partial }));
  };

  return (
    <div className={classNames.serviceNotificationCard}>
      <div className={classNames.serviceNotificationTitle}>{labels.serviceDisruptionTitle}</div>
      <div className={classNames.serviceNotificationDescription}>{labels.serviceDisruptionDescription}</div>

      {saveError && (
        <MessageBar
          className={classNames.serviceNotificationErrorMessage}
          messageBarType={MessageBarType.error}
          isMultiline={false}
        >
          {saveError}
        </MessageBar>
      )}

      <Toggle
        className={classNames.serviceNotificationToggle}
        label={labels.enabled}
        checked={config.isEnabled}
        onText={labels.enabledOn}
        offText={labels.enabledOff}
        inlineLabel={false}
        disabled={!canEdit}
        onChange={(_event, checked) => update({ isEnabled: !!checked })}
      />

      <TextField
        label={labels.message}
        placeholder={labels.messagePlaceholder}
        value={config.message}
        maxLength={MAX_NOTIFICATION_MESSAGE_LENGTH}
        errorMessage={errors.message}
        styles={{ fieldGroup: classNames.serviceNotificationTextFieldGroup }}
        disabled={!canEdit}
        onChange={(_event, newValue) => update({ message: newValue ?? '' })}
      />

      <div className={classNames.serviceNotificationFieldRow}>
        <TextField
          label={labels.linkLabel}
          placeholder={labels.linkLabelPlaceholder}
          value={config.linkText ?? ''}
          styles={{ fieldGroup: classNames.serviceNotificationTextFieldGroup }}
          disabled={!canEdit}
          onChange={(_event, newValue) => update({ linkText: newValue ?? '' })}
        />
        <TextField
          label={labels.linkUrl}
          placeholder={labels.linkUrlPlaceholder}
          value={config.linkUrl ?? ''}
          errorMessage={errors.linkUrl}
          styles={{ fieldGroup: classNames.serviceNotificationTextFieldGroup }}
          disabled={!canEdit}
          onChange={(_event, newValue) => update({ linkUrl: newValue ?? '' })}
        />
      </div>

      <div className={classNames.serviceNotificationFieldRow}>
        <DatePicker
          label={labels.startDate}
          placeholder={labels.startDatePlaceholder}
          value={toDate(config.startDate)}
          textField={{ styles: { fieldGroup: classNames.serviceNotificationTextFieldGroup } }}
          disabled={!canEdit}
          onSelectDate={(date) => date && update({ startDate: date.toISOString() })}
        />
        <DatePicker
          label={labels.endDate}
          placeholder={labels.endDatePlaceholder}
          value={toDate(config.endDate)}
          textField={{ styles: { fieldGroup: classNames.serviceNotificationTextFieldGroup } }}
          disabled={!canEdit}
          onSelectDate={(date) => date && update({ endDate: date.toISOString() })}
        />
      </div>

      {errors.dates && (
        <MessageBar
          className={classNames.serviceNotificationErrorMessage}
          messageBarType={MessageBarType.error}
          isMultiline={false}
        >
          {errors.dates}
        </MessageBar>
      )}
    </div>
  );
};
