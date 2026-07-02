// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  DatePicker,
  DefaultButton,
  MessageBar,
  MessageBarType,
  PrimaryButton,
  Stack,
  Text,
  TextField,
  Toggle,
} from '@fluentui/react';
import { AppDispatch } from '../../store';
import { fetchAlertBanner, patchAlertBanner } from '../../store/alertBanner.api';
import {
  selectAlertBannerConfig,
  selectAlertBannerIsSaving,
  selectAlertBannerSaveError,
} from '../../store/alertBanner.slice';
import { AlertBannerConfig } from '../../models/AlertBannerConfig';

const MAX_MESSAGE_LENGTH = 250;

const toDate = (value: string): Date | undefined => {
  const d = new Date(value);
  return isNaN(d.getTime()) ? undefined : d;
};

/**
 * Client-side validation mirroring the server rules on PATCH /settings/alertBanner.
 * Returns a map of field -> error message; empty object means valid.
 */
const validate = (config: AlertBannerConfig): Record<string, string> => {
  const errors: Record<string, string> = {};

  if (config.isEnabled && config.message.trim().length === 0) {
    errors.message = 'Message is required when the alert banner is enabled.';
  }
  if (config.message.length > MAX_MESSAGE_LENGTH) {
    errors.message = `Message must be ${MAX_MESSAGE_LENGTH} characters or fewer.`;
  }

  const start = toDate(config.startDate);
  const end = toDate(config.endDate);
  if (!start || !end) {
    errors.dates = 'Valid start and end dates are required.';
  } else if (start >= end) {
    errors.dates = 'Start date must be earlier than end date.';
  }

  if (config.linkUrl && config.linkUrl.trim().length > 0 && !/^https:\/\//i.test(config.linkUrl.trim())) {
    errors.linkUrl = 'Link URL must start with https://.';
  }

  return errors;
};

/**
 * Admin form for configuring the GMM alert banner. Self-contained: it manages its
 * own draft state, validation, and Save action against the alertBanner thunks.
 * Gated to GENERAL_SETTINGS_ADMINISTRATOR by the parent Pivot.
 */
export const AlertBannerAdmin: React.FunctionComponent = () => {
  const dispatch = useDispatch<AppDispatch>();
  const savedConfig = useSelector(selectAlertBannerConfig);
  const isSaving = useSelector(selectAlertBannerIsSaving);
  const saveError = useSelector(selectAlertBannerSaveError);

  // On mount, (re)load the existing config so the form pre-populates for editing.
  useEffect(() => {
    dispatch(fetchAlertBanner());
  }, [dispatch]);

  const [draft, setDraft] = useState<AlertBannerConfig>(savedConfig);

  // Keep the draft in sync when the stored config loads/changes.
  useEffect(() => {
    setDraft(savedConfig);
  }, [savedConfig]);

  const errors = useMemo(() => validate(draft), [draft]);
  const hasErrors = Object.keys(errors).length > 0;
  const hasChanges = JSON.stringify(draft) !== JSON.stringify(savedConfig);

  const update = (partial: Partial<AlertBannerConfig>) => {
    setDraft((prev) => ({ ...prev, ...partial }));
  };

  const handleSave = () => {
    if (hasErrors) {
      return;
    }
    dispatch(
      patchAlertBanner({
        ...draft,
        message: draft.message.trim(),
        linkUrl: draft.linkUrl && draft.linkUrl.trim().length > 0 ? draft.linkUrl.trim() : null,
        linkText: draft.linkText && draft.linkText.trim().length > 0 ? draft.linkText.trim() : null,
      })
    );
  };

  return (
    <Stack tokens={{ childrenGap: 16 }} styles={{ root: { maxWidth: 640, paddingTop: 12 } }}>
      <Text variant="large">Alert Banner</Text>
      <Text>
        Configure a site-wide alert banner. When enabled and within the date window, all users see the
        message at the top of every page.
      </Text>

      {saveError && (
        <MessageBar messageBarType={MessageBarType.error} isMultiline={false}>
          {saveError}
        </MessageBar>
      )}

      <Toggle
        label="Enabled"
        checked={draft.isEnabled}
        onText="On"
        offText="Off"
        onChange={(_e, checked) => update({ isEnabled: !!checked })}
      />

      <TextField
        label="Message"
        multiline
        rows={3}
        value={draft.message}
        maxLength={MAX_MESSAGE_LENGTH}
        description={`${draft.message.length}/${MAX_MESSAGE_LENGTH}`}
        errorMessage={errors.message}
        onChange={(_e, newValue) => update({ message: newValue ?? '' })}
      />

      <Stack horizontal tokens={{ childrenGap: 16 }}>
        <DatePicker
          label="Start date (UTC)"
          value={toDate(draft.startDate)}
          onSelectDate={(date) => date && update({ startDate: date.toISOString() })}
        />
        <DatePicker
          label="End date (UTC)"
          value={toDate(draft.endDate)}
          onSelectDate={(date) => date && update({ endDate: date.toISOString() })}
        />
      </Stack>
      {errors.dates && (
        <MessageBar messageBarType={MessageBarType.error} isMultiline={false}>
          {errors.dates}
        </MessageBar>
      )}

      <TextField
        label="Link URL (optional, https:// only)"
        value={draft.linkUrl ?? ''}
        errorMessage={errors.linkUrl}
        onChange={(_e, newValue) => update({ linkUrl: newValue ?? '' })}
      />

      <TextField
        label="Link text (optional)"
        placeholder="If empty, the URL is shown as the link text."
        value={draft.linkText ?? ''}
        onChange={(_e, newValue) => update({ linkText: newValue ?? '' })}
      />

      <Stack horizontal tokens={{ childrenGap: 12 }}>
        <PrimaryButton
          text="Save"
          onClick={handleSave}
          disabled={!hasChanges || hasErrors || isSaving}
        />
        <DefaultButton
          text="Reset"
          onClick={() => setDraft(savedConfig)}
          disabled={!hasChanges || isSaving}
        />
      </Stack>
    </Stack>
  );
};
