// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { AdminConfigProps } from './AdminConfig.types';
import {
  selectDashboardUrl,
  selectIsSaving,
  selectOutlookWarningUrl,
  selectPrivacyPolicyUrl,
  selectUIUrl,
  selectCanReviewOwnSubmissions,
  selectCreateGroupFeatureEnabled,
  selectIsBusinessJustificationRequired,
  selectIsDisclaimerEnabled,
  selectIsAutoApprovalForGroupBasedSyncsEnabled,
  selectIsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled,
  selectIsPerPartAutoApprovalEnabled,
  selectIsAITitleEnabled,
  selectIsAICopilotEnabled,
  selectCopilotTemperature,
  selectCopilotTopP,
  selectCopilotInstructions,
  selectCopilotSuggestedPrompts,
  selectDefaultAIPrompt,
  selectIsAISearchForUserEnabled,
  selectIsAIRunExplanationEnabled,
  selectAreSettingsLoaded,
  selectError,
} from '../../store/settings.slice';
import { patchSetting, fetchDefaultAIPrompt, fetchSettings } from '../../store/settings.api';
import { AppDispatch } from '../../store';
import { AdminConfigView } from './AdminConfig.view';
import { useStrings } from '../../store/hooks';
import { SettingKey } from '../../models/SettingKey';
import { setPagingBarVisible } from '../../store/pagingBar.slice';
import { selectSource, selectAttributes, selectIsSourceSaving, selectAreAttributesSaving, setSource, setAttributes } from '../../store/sqlMembershipSources.slice';
import { SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import { fetchAttributeValues, patchDefaultSqlMembershipSourceAttributes, patchDefaultSqlMembershipSourceCustomLabel } from '../../store/sqlMembershipSources.api';
import {
  selectIsCustomMembershipProviderAdministrator,
  selectIsOperationsResetAdministrator,
  selectIsGeneralSettingsAdministrator,
  selectIsAutoApproverAdministrator,
  selectHasAdminCenterPermissions,
  selectIsAISettingsAdministrator,
} from '../../store/roles.slice';
import { MessageBar, MessageBarType } from '@fluentui/react';
import { Loader } from '../../components/Loader';
import { fetchAlertBanner, patchAlertBanner } from '../../store/alertBanner.api';
import { selectAlertBannerConfig, selectAlertBannerIsSaving, selectAlertBannerSaveError } from '../../store/alertBanner.slice';
import { AlertBannerConfig, normalizeAlertBannerWindow } from '../../models/AlertBannerConfig';

export const AdminConfigBase: React.FunctionComponent<AdminConfigProps> = (props: AdminConfigProps) => {
  // get the store's dispatch function
  const dispatch = useDispatch<AppDispatch>();
  useEffect(() => {
    dispatch(setPagingBarVisible(false));
    dispatch(fetchDefaultAIPrompt());
    // Fetched by App on login, but re-requested here so a direct navigation or refresh to this
    // page never renders against an empty settings store.
    dispatch(fetchSettings());
    // Load the current service notification (alert banner) configuration so the General tab
    // renders the persisted values instead of the disabled default.
    dispatch(fetchAlertBanner());
  }, [dispatch]);

  // get the settings data from the store
  const dashboardUrl = useSelector(selectDashboardUrl);
  const outlookWarningUrl = useSelector(selectOutlookWarningUrl);
  const privacyPolicyUrl = useSelector(selectPrivacyPolicyUrl);
  const UIUrl = useSelector(selectUIUrl);
  const canReviewOwnSubmissions = useSelector(selectCanReviewOwnSubmissions);
  const createGroupFeatureEnabled = useSelector(selectCreateGroupFeatureEnabled);
  const isBusinessJustificationRequired = useSelector(selectIsBusinessJustificationRequired);
  const IsDisclaimerEnabled = useSelector(selectIsDisclaimerEnabled);
  const IsAutoApprovalForGroupBasedSyncsEnabled = useSelector(selectIsAutoApprovalForGroupBasedSyncsEnabled);
  const IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled = useSelector(selectIsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled);
  const isPerPartAutoApprovalEnabled = useSelector(selectIsPerPartAutoApprovalEnabled);
  const isAITitleEnabled = useSelector(selectIsAITitleEnabled);
  const isAICopilotEnabled = useSelector(selectIsAICopilotEnabled);
  const isAISearchForUserEnabled = useSelector(selectIsAISearchForUserEnabled);
  const isAIRunExplanationEnabled = useSelector(selectIsAIRunExplanationEnabled);
  const copilotTemperature = useSelector(selectCopilotTemperature);
  const copilotTopP = useSelector(selectCopilotTopP);
  const copilotInstructions = useSelector(selectCopilotInstructions);
  const copilotSuggestedPrompts = useSelector(selectCopilotSuggestedPrompts);
  const defaultAIPrompt = useSelector(selectDefaultAIPrompt);
  const sqlMembershipSource = useSelector(selectSource);
  const sqlMembershipSourceAttributes = useSelector(selectAttributes);
  const isSourceSaving = useSelector(selectIsSourceSaving);
  const areAttributesSaving = useSelector(selectAreAttributesSaving);
  const areSettingsSaving = useSelector(selectIsSaving);
  const storedServiceNotification = useSelector(selectAlertBannerConfig);
  const isServiceNotificationSaving = useSelector(selectAlertBannerIsSaving);
  const serviceNotificationSaveError = useSelector(selectAlertBannerSaveError);

  // Normalize before the value reaches the view so it acts as both the form's initial value and
  // its unchanged baseline. A backend that still returns start == end therefore neither shows a
  // validation error on load nor marks the page dirty before the admin edits anything.
  const serviceNotification = useMemo(
    () => normalizeAlertBannerWindow(storedServiceNotification),
    [storedServiceNotification]
  );
  const isCustomMembershipProviderAdmin = useSelector(selectIsCustomMembershipProviderAdministrator);
  const isOperationsResetAdministrator = useSelector(selectIsOperationsResetAdministrator);
  const isGeneralSettingsAdministrator = useSelector(selectIsGeneralSettingsAdministrator);
  const isAutoApproverAdministrator = useSelector(selectIsAutoApproverAdministrator);
  const isAISettingsAdministrator = useSelector(selectIsAISettingsAdministrator);
  const canViewSettings = useSelector(selectHasAdminCenterPermissions);
  const areSettingsLoaded = useSelector(selectAreSettingsLoaded);
  const settingsError = useSelector(selectError);

  const strings = useStrings().AdminConfig;

  const generateSettings = () => ({
    [SettingKey.DashboardUrl]: dashboardUrl ?? '',
    [SettingKey.OutlookWarningUrl]: outlookWarningUrl ?? '',
    [SettingKey.PrivacyPolicyUrl]: privacyPolicyUrl ?? '',
    [SettingKey.UIUrl]: UIUrl ?? '',
    [SettingKey.CanReviewOwnSubmissions]: canReviewOwnSubmissions ? 'true' : 'false',
    [SettingKey.CreateGroupFeatureEnabled]: createGroupFeatureEnabled ? 'true' : 'false',
    [SettingKey.IsBusinessJustificationRequired]: isBusinessJustificationRequired ? 'true' : 'false',
    [SettingKey.IsDisclaimerEnabled]: IsDisclaimerEnabled ? 'true' : 'false',
    [SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]: IsAutoApprovalForGroupBasedSyncsEnabled ? 'true' : 'false',
    [SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]: IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled ? 'true' : 'false',
    [SettingKey.IsPerPartAutoApprovalEnabled]: isPerPartAutoApprovalEnabled ? 'true' : 'false',
    [SettingKey.IsAITitleEnabled]: isAITitleEnabled ? 'true' : 'false',
    [SettingKey.IsAICopilotEnabled]: isAICopilotEnabled ? 'true' : 'false',
    [SettingKey.IsAISearchForUserEnabled]: isAISearchForUserEnabled ? 'true' : 'false',
    [SettingKey.IsAIRunExplanationEnabled]: isAIRunExplanationEnabled ? 'true' : 'false',
    [SettingKey.CopilotTemperature]: copilotTemperature ?? '0.7',
    [SettingKey.CopilotTopP]: copilotTopP ?? '0.9',
    [SettingKey.CopilotInstructions]: copilotInstructions ?? '',
    [SettingKey.CopilotSuggestedPrompts]: copilotSuggestedPrompts ?? '',
  });

  const [settings, setSettings] = useState<{ readonly [key in SettingKey]: string }>(generateSettings());

  useEffect(() => {
    setSettings(generateSettings())
  }, [dashboardUrl, outlookWarningUrl, privacyPolicyUrl, canReviewOwnSubmissions, createGroupFeatureEnabled, isBusinessJustificationRequired, IsDisclaimerEnabled, IsAutoApprovalForGroupBasedSyncsEnabled, IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, isPerPartAutoApprovalEnabled, isAITitleEnabled, isAICopilotEnabled, isAISearchForUserEnabled, isAIRunExplanationEnabled, copilotTemperature, copilotTopP, copilotInstructions, copilotSuggestedPrompts]);

  const handleGetValues = (attribute: SqlMembershipAttribute) => {
    dispatch(fetchAttributeValues(attribute));
  }

  // Setting keys owned by each Admin Configuration administrator role. Only changed keys within
  // the signed-in administrator's authorized scopes are dispatched, so a user holding a single
  // role never triggers an unauthorized PATCH. UIUrl is loaded for display
  // purposes only and is intentionally excluded from every scope.
  const generalSettingKeys: SettingKey[] = [
    SettingKey.DashboardUrl,
    SettingKey.OutlookWarningUrl,
    SettingKey.PrivacyPolicyUrl,
    SettingKey.CanReviewOwnSubmissions,
    SettingKey.CreateGroupFeatureEnabled,
    SettingKey.IsBusinessJustificationRequired,
    SettingKey.IsDisclaimerEnabled,
  ];

  const autoApproverSettingKeys: SettingKey[] = [
    SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled,
    SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled,
    SettingKey.IsPerPartAutoApprovalEnabled,
  ];

  const aiSettingKeys: SettingKey[] = [
    SettingKey.IsAITitleEnabled,
    SettingKey.IsAICopilotEnabled,
    SettingKey.IsAISearchForUserEnabled,
    SettingKey.IsAIRunExplanationEnabled,
    SettingKey.IsAIRejectionFeedbackRefinementEnabled,
    SettingKey.CopilotTemperature,
    SettingKey.CopilotTopP,
    SettingKey.CopilotInstructions,
    SettingKey.CopilotSuggestedPrompts,
  ];

  const booleanSettingKeys: SettingKey[] = [
    SettingKey.CanReviewOwnSubmissions,
    SettingKey.CreateGroupFeatureEnabled,
    SettingKey.IsBusinessJustificationRequired,
    SettingKey.IsDisclaimerEnabled,
    SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled,
    SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled,
    SettingKey.IsPerPartAutoApprovalEnabled,
    SettingKey.IsAITitleEnabled,
    SettingKey.IsAICopilotEnabled,
    SettingKey.IsAISearchForUserEnabled,
    SettingKey.IsAIRunExplanationEnabled,
    SettingKey.IsAIRejectionFeedbackRefinementEnabled,
  ];

  // Normalize boolean values to strings, then dispatch only the changed settings that the
  // signed-in administrator is authorized to manage.
  const handleSave = (newSettings: { readonly [key in SettingKey]: string }, newSqlMembershipSource: SqlMembershipSource | undefined, newSqlMembershipAttributes: SqlMembershipAttribute[] | undefined, newServiceNotification: AlertBannerConfig) => {
    const formattedSettings = { ...newSettings };
    booleanSettingKeys.forEach((settingKey) => {
      formattedSettings[settingKey] = newSettings[settingKey] === 'true' ? 'true' : 'false';
    });

    const authorizedSettingKeys: SettingKey[] = [
      ...(isGeneralSettingsAdministrator ? generalSettingKeys : []),
      ...(isAutoApproverAdministrator ? autoApproverSettingKeys : []),
      ...(isAISettingsAdministrator ? aiSettingKeys : []),
    ];

    const changedAuthorizedSettingKeys = authorizedSettingKeys.filter(
      (settingKey) => formattedSettings[settingKey] !== settings[settingKey]
    );

    if (changedAuthorizedSettingKeys.length > 0) {
      setSettings(formattedSettings);

      changedAuthorizedSettingKeys.forEach((settingKey) => {
        dispatch(
          patchSetting({
            settingKey,
            settingValue: formattedSettings[settingKey],
          })
        );
      });
    }
    if (JSON.stringify(newSqlMembershipSource) !== JSON.stringify(sqlMembershipSource)) {
      dispatch(
        patchDefaultSqlMembershipSourceCustomLabel(newSqlMembershipSource?.customLabel ?? '')
      );

      dispatch(
        setSource(newSqlMembershipSource)
      );
    }

    if (JSON.stringify(newSqlMembershipAttributes) !== JSON.stringify(sqlMembershipSourceAttributes)) {
      dispatch(
        patchDefaultSqlMembershipSourceAttributes(newSqlMembershipAttributes ?? [])
      );

      dispatch(
        setAttributes(newSqlMembershipAttributes)
      );
    }
    // The service notification (alert banner) is persisted through its own endpoint, which is
    // restricted to General Settings administrators. Only dispatch when it actually changed.
    if (isGeneralSettingsAdministrator && JSON.stringify(newServiceNotification) !== JSON.stringify(serviceNotification)) {
      dispatch(
        patchAlertBanner({
          ...newServiceNotification,
          message: newServiceNotification.message.trim(),
          linkUrl: newServiceNotification.linkUrl && newServiceNotification.linkUrl.trim().length > 0 ? newServiceNotification.linkUrl.trim() : null,
          linkText: newServiceNotification.linkText && newServiceNotification.linkText.trim().length > 0 ? newServiceNotification.linkText.trim() : null,
        })
      );
    }

    // there is a Toast notification in fluent/react-components (v9) that we should be using for save notifications.

    // Refetch settings so Redux store reflects saved values
    setTimeout(() => dispatch(fetchSettings()), 1000);
  };

  if (!canViewSettings) {
    return (<MessageBar
      messageBarType={MessageBarType.error}
      isMultiline={false}
    >
    {strings.Errors.forbidden}
  </MessageBar>);
}

  // Until the settings have been fetched every selector resolves to undefined, which
  // generateSettings would render as an unchecked toggle or an empty field. Showing a loader
  // instead prevents an unloaded store from being mistaken for settings that are turned off.
  if (!areSettingsLoaded) {
    if (settingsError) {
      return (<MessageBar
        messageBarType={MessageBarType.error}
        isMultiline={false}
      >
        {strings.Errors.loadFailed}
      </MessageBar>);
    }
    return <Loader />;
  }
  // render the view with the data from the store and the event handler
  return (
    <AdminConfigView
      {...props}
      isSaving={areSettingsSaving || isSourceSaving || areAttributesSaving || isServiceNotificationSaving}
      settings={settings}
      strings={strings}
      onSave={handleSave}
      handleGetValues={handleGetValues}
      sqlMembershipSource={sqlMembershipSource}
      sqlMembershipSourceAttributes={sqlMembershipSourceAttributes}
      serviceNotification={serviceNotification}
      serviceNotificationSaveError={serviceNotificationSaveError}
      isCustomMembershipProviderAdmin={isCustomMembershipProviderAdmin}
      isOperationsResetAdministrator={isOperationsResetAdministrator}
      isGeneralSettingsAdministrator={isGeneralSettingsAdministrator}
      isAutoApproverAdministrator={isAutoApproverAdministrator}
      isAISettingsAdministrator={isAISettingsAdministrator}
      defaultAIPrompt={defaultAIPrompt}
    />
  );
};
