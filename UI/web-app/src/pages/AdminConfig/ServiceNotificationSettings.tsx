// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import {
  DatePicker,
  Icon,
  IRenderFunction,
  ITextFieldProps,
  Label,
  MessageBar,
  MessageBarType,
  Stack,
  TextField,
  Toggle,
  TooltipHost,
} from '@fluentui/react';
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

  if ((config.linkText ?? '').length > MAX_NOTIFICATION_MESSAGE_LENGTH) {
    errors.linkText = errorStrings.linkTextTooLong;
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
  const tooltips = strings.ServiceNotifications.tooltips;

  const update = (partial: Partial<AlertBannerConfig>) => {
    setConfig((previous) => ({ ...previous, ...partial }));
  };

  // Info icon with a tooltip describing the validation rules for a field.
  // The icon carries a generic accessible name so it does not collide with the
  // field's own label; the validation text is exposed as the tooltip content
  // (announced via aria-describedby when the icon is focused).
  const infoIcon = (tooltip: string): JSX.Element => (
    <TooltipHost content={tooltip} calloutProps={{ gapSpace: 0 }}>
      <Icon
        iconName="Info"
        tabIndex={0}
        aria-label={tooltips.infoAriaLabel}
        className={classNames.serviceNotificationInfoIcon}
      />
    </TooltipHost>
  );

  // onRenderLabel that keeps the control's native label (and its accessibility
  // association) and appends the validation info icon beside it.
  const renderLabelWithInfo =
    <T,>(tooltip: string): IRenderFunction<T> =>
    (labelProps, defaultRender) =>
      (
        <Stack horizontal verticalAlign="center" tokens={{ childrenGap: 4 }}>
          {defaultRender?.(labelProps)}
          {infoIcon(tooltip)}
        </Stack>
      );

  // DatePicker and Toggle do not expose onRenderLabel, so build a label row with
  // the validation info icon that can be rendered above/alongside them.
  const labelWithInfo = (label: string, tooltip: string, required?: boolean): JSX.Element => (
    <Stack horizontal verticalAlign="center" tokens={{ childrenGap: 4 }}>
      <Label required={required}>{label}</Label>
      {infoIcon(tooltip)}
    </Stack>
  );

  return (
    <div className={classNames.serviceNotificationCard}>
      <div className={classNames.serviceNotificationTitle}>{labels.serviceDisruptionTitle}</div>

      {saveError && (
        <MessageBar
          className={classNames.serviceNotificationErrorMessage}
          messageBarType={MessageBarType.error}
          isMultiline={false}
        >
          {saveError}
        </MessageBar>
      )}

      <Stack className={classNames.serviceNotificationToggle} tokens={{ childrenGap: 4 }}>
        {labelWithInfo(labels.enabled, tooltips.enabled)}
        <Toggle
          ariaLabel={labels.enabled}
          checked={config.isEnabled}
          onText={labels.enabledOn}
          offText={labels.enabledOff}
          inlineLabel={false}
          disabled={!canEdit}
          onChange={(_event, checked) => update({ isEnabled: !!checked })}
        />
      </Stack>

      <TextField
        label={labels.message}
        onRenderLabel={renderLabelWithInfo<ITextFieldProps>(tooltips.message)}
        placeholder={labels.messagePlaceholder}
        value={config.message}
        maxLength={MAX_NOTIFICATION_MESSAGE_LENGTH}
        required={config.isEnabled}
        errorMessage={errors.message}
        styles={{ fieldGroup: classNames.serviceNotificationTextFieldGroup }}
        disabled={!canEdit}
        onChange={(_event, newValue) => update({ message: newValue ?? '' })}
      />

      <div className={classNames.serviceNotificationFieldRow}>
        <TextField
          label={labels.linkLabel}
          onRenderLabel={renderLabelWithInfo<ITextFieldProps>(tooltips.linkLabel)}
          placeholder={labels.linkLabelPlaceholder}
          value={config.linkText ?? ''}
          maxLength={MAX_NOTIFICATION_MESSAGE_LENGTH}
          errorMessage={errors.linkText}
          styles={{ fieldGroup: classNames.serviceNotificationTextFieldGroup }}
          disabled={!canEdit}
          onChange={(_event, newValue) => update({ linkText: newValue ?? '' })}
        />
        <TextField
          label={labels.linkUrl}
          onRenderLabel={renderLabelWithInfo<ITextFieldProps>(tooltips.linkUrl)}
          placeholder={labels.linkUrlPlaceholder}
          value={config.linkUrl ?? ''}
          errorMessage={errors.linkUrl}
          styles={{ fieldGroup: classNames.serviceNotificationTextFieldGroup }}
          disabled={!canEdit}
          onChange={(_event, newValue) => update({ linkUrl: newValue ?? '' })}
        />
      </div>

      <div className={classNames.serviceNotificationFieldRow}>
        <Stack tokens={{ childrenGap: 4 }}>
          {labelWithInfo(labels.startDate, tooltips.startDate, config.isEnabled)}
          <DatePicker
            ariaLabel={labels.startDate}
            placeholder={labels.startDatePlaceholder}
            value={toDate(config.startDate)}
            isRequired={config.isEnabled}
            textField={{ styles: { fieldGroup: classNames.serviceNotificationTextFieldGroup } }}
            disabled={!canEdit}
            onSelectDate={(date) => date && update({ startDate: date.toISOString() })}
          />
        </Stack>
        <Stack tokens={{ childrenGap: 4 }}>
          {labelWithInfo(labels.endDate, tooltips.endDate, config.isEnabled)}
          <DatePicker
            ariaLabel={labels.endDate}
            placeholder={labels.endDatePlaceholder}
            value={toDate(config.endDate)}
            isRequired={config.isEnabled}
            textField={{ styles: { fieldGroup: classNames.serviceNotificationTextFieldGroup } }}
            disabled={!canEdit}
            onSelectDate={(date) => date && update({ endDate: date.toISOString() })}
          />
        </Stack>
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
