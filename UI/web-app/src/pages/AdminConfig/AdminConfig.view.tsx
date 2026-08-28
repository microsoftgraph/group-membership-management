// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { classNamesFunction, Toggle, IProcessedStyleSet, Pivot, PivotItem, PrimaryButton, DefaultButton, TextField, Text, IColumn, SelectionMode, ShimmeredDetailsList, Dropdown, Spinner, IRenderFunction, ISelectableDroppableTextProps, IDropdown, Slider, Icon, IconButton, ActionButton, MessageBar, MessageBarType } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import {
  AdminConfigStyleProps,
  AdminConfigStyles,
  AdminConfigViewProps,
  CustomLabelCellProps,
  NullThresholdCellProps,
  AttributeValuesCellProps,
  CustomSourceSettingsProps,
  UserResourcesSettingsProps,
  OperationsProps,
  GeneralSettingsProps,
  AutoApproverSettingsProps,
  SettingsSectionProps,
  AISettingsProps } from './AdminConfig.types';
import { PageSection } from '../../components/PageSection';
import { HyperlinkSetting } from '../../components/HyperlinkSetting';
import { Operation } from '../../components/Operation';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { SettingKey, SettingKeyMap, SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import { GeneralSetting } from '../../components/GeneralSetting';
import { ServiceNotificationSettings, validateServiceNotification } from './ServiceNotificationSettings';
import type { AlertBannerConfig } from '../../models/AlertBannerConfig';

const getClassNames = classNamesFunction<AdminConfigStyleProps, AdminConfigStyles>();

export const AdminConfigView: React.FunctionComponent<AdminConfigViewProps> = (props: AdminConfigViewProps) => {
  // extract props
  const { className, isSaving, onSave, handleGetValues, settings, sqlMembershipSource, sqlMembershipSourceAttributes, strings, styles,
    serviceNotification,
    serviceNotificationSaveError,
    isCustomMembershipProviderAdmin,
    isOperationsResetAdministrator,
    isGeneralSettingsAdministrator,
    isAutoApproverAdministrator,
    isAISettingsAdministrator,
    canViewGeneralSettings,
    canViewAutoApproverSettings,
    canViewAISettings,
    canViewCustomSourceSettings,
    isReadOnly,
    defaultAIPrompt } = props;

  // generate class names
  const classNames: IProcessedStyleSet<AdminConfigStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  // setup the ui state
  const [newSettings, setNewSettings] = useState(settings);
  const [newSource, setNewSource] = useState<SqlMembershipSource | undefined>(sqlMembershipSource);
  const [newAttributes, setNewAttributes] = useState<SqlMembershipAttribute[] | undefined>(sqlMembershipSourceAttributes);
  const [hasUrlValidationErrors, setHasUrlValidationErrors] = useState<boolean>(false);
  const [hasAttributeValidationErrors, setHasAttributeValidationErrors] = useState<boolean>(false);
  const [newServiceNotification, setNewServiceNotification] = useState<AlertBannerConfig>(serviceNotification);

  useEffect(() => {
    setNewServiceNotification(serviceNotification);
  }, [serviceNotification]);

  const serviceNotificationErrors = useMemo(
    () => validateServiceNotification(newServiceNotification, strings),
    [newServiceNotification, strings]
  );
  const hasServiceNotificationErrors = Object.keys(serviceNotificationErrors).length > 0;

  useEffect(() => {
    setNewSource(sqlMembershipSource);
  }, [sqlMembershipSource]);

  useEffect(() => {
    setNewAttributes(sqlMembershipSourceAttributes);
  }, [sqlMembershipSourceAttributes]);

  useEffect(() => {
    setNewSettings(settings);
  }, [settings]);

  // create state helpers
  const hasChanges = () => {
    const hasSettingsChanges = Object.entries(newSettings).some(([key, value]) => value !== settings[Number(key) as SettingKey]);
    const hasSourceChanges = JSON.stringify(newSource) !== JSON.stringify(sqlMembershipSource);
    const hasAttributesChanges = JSON.stringify(newAttributes) !== JSON.stringify(sqlMembershipSourceAttributes);
    const hasServiceNotificationChanges = JSON.stringify(newServiceNotification) !== JSON.stringify(serviceNotification);
    return hasSettingsChanges || hasSourceChanges || hasAttributesChanges || hasServiceNotificationChanges;
  };

  // setup ui event handlers
  const handleOnSaveButtonClick = () => {
    onSave(newSettings, newSource, newAttributes, newServiceNotification);
  };

  return (
    <Page>
      <PageHeader />
      <div className={classNames.root}>
        <div className={classNames.titleRow}>
          <div className={classNames.title}>{strings.labels.pageTitle}</div>
          <PrimaryButton
            text={strings.labels.saveButton}
            onClick={handleOnSaveButtonClick}
            className={classNames.saveButton}
            title={isReadOnly ? strings.labels.readOnlyTooltip : undefined}
            disabled={isReadOnly || !hasChanges() || hasUrlValidationErrors || hasAttributeValidationErrors || hasServiceNotificationErrors || isSaving}
          ></PrimaryButton>
        </div>
        {isReadOnly &&
          <MessageBar messageBarType={MessageBarType.info}>
            {strings.labels.readOnlyBanner}
          </MessageBar>
        }
        <div className={classNames.card}>
          <PageSection>
            <Pivot>
              {canViewGeneralSettings &&
                <PivotItem
                  headerText={strings.GeneralSettings.labels.general}
                  headerButtonProps={{
                    'data-order': 1,
                    'data-title': strings.GeneralSettings.labels.general,
                  }}
                >
                  <GeneralSettings
                    classNames={classNames}
                    strings={strings}
                    settings={newSettings}
                    setSettings={setNewSettings}
                    setHasValidationErrors={setHasUrlValidationErrors}
                    canEdit={isGeneralSettingsAdministrator}
                    serviceNotification={newServiceNotification}
                    setServiceNotification={setNewServiceNotification}
                    serviceNotificationErrors={serviceNotificationErrors}
                    serviceNotificationSaveError={serviceNotificationSaveError} />
                </PivotItem>
              }
              {isOperationsResetAdministrator &&
                <PivotItem
                  headerText={strings.Operations.labels.operations}
                  headerButtonProps={{
                    'data-order': 2,
                    'data-title': strings.Operations.labels.operations,
                  }}
                >
                  <Operations
                    classNames={classNames}
                    strings={strings} />
                </PivotItem>
              }
              {canViewCustomSourceSettings &&
                <PivotItem
                  headerText={strings.CustomSourceSettings.labels.customSource}
                  headerButtonProps={{
                    'data-order': 3,
                    'data-title': strings.CustomSourceSettings.labels.customSource,
                  }}
                >
                  <CustomSourceSettings
                    classNames={classNames}
                    sqlMembershipSource={sqlMembershipSource}
                    sqlMembershipSourceAttributes={sqlMembershipSourceAttributes}
                    setNewAttributes={setNewAttributes}
                    setNewSource={setNewSource}
                    handleGetValues={handleGetValues}
                    canEdit={isCustomMembershipProviderAdmin}
                    setHasValidationErrors={setHasAttributeValidationErrors}
                    strings={strings} />
                </PivotItem>
              }
              {canViewAISettings &&
                <PivotItem
                  headerText={strings.AISettings.labels.aiSettings}
                  headerButtonProps={{
                    'data-order': 4,
                    'data-title': strings.AISettings.labels.aiSettings,
                  }}
                >
                  <AISettings
                    classNames={classNames}
                    strings={strings}
                    settings={newSettings}
                    setSettings={setNewSettings}
                    canEdit={isAISettingsAdministrator}
                    defaultAIPrompt={defaultAIPrompt} />
                </PivotItem>
              }
              {canViewAutoApproverSettings &&
                <PivotItem
                  headerText={strings.AutoApproverSettings.labels.autoApprover}
                  headerButtonProps={{
                    'data-order': 5,
                    'data-title': strings.AutoApproverSettings.labels.autoApprover,
                  }}
                >
                  <AutoApproverSettings
                    classNames={classNames}
                    strings={strings}
                    settings={newSettings}
                    canEdit={isAutoApproverAdministrator}
                    setSettings={setNewSettings} />
                </PivotItem>
              }
            </Pivot>
          </PageSection>
        </div>
      </div>
    </Page>
  );
};

const SettingsSection: React.FunctionComponent<SettingsSectionProps> = (props: SettingsSectionProps) => {
  const { classNames, title, subtitle, showDivider, children } = props;
  return (
    <section className={showDivider ? classNames.sectionContainerDivided : classNames.sectionContainer}>
      <Text variant="mediumPlus" className={classNames.sectionHeading}>{title}</Text>
      {subtitle && <Text variant="small" className={classNames.sectionSubtitle}>{subtitle}</Text>}
      {children}
    </section>
  );
}

const Operations: React.FunctionComponent<OperationsProps> = (props: OperationsProps) => {
  const { classNames, strings} = props;
  return (
    <SettingsSection classNames={classNames} title={strings.Operations.labels.controlPanel}>
      <div className={classNames.operationsGrid}>
        <Operation
          variant="service"
          title={strings.Operations.labels.serviceOperationTitle}
          description={strings.Operations.labels.serviceOperationDescription}
          buttonText={strings.Operations.buttons}
        ></Operation>
        <Operation
          variant="reset"
          title={strings.Operations.labels.resetOperationTitle}
          description={strings.Operations.labels.resetOperationDescription}
          buttonText={strings.Operations.buttons}
        ></Operation>
      </div>
    </SettingsSection>
  );
}

const GeneralSettings: React.FunctionComponent<GeneralSettingsProps> = (props: GeneralSettingsProps) => {
  const { classNames, strings, settings, setSettings, setHasValidationErrors, canEdit, serviceNotification, setServiceNotification, serviceNotificationErrors, serviceNotificationSaveError } = props;

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  return (
    <div>
      <SettingsSection
        classNames={classNames}
        title={strings.GeneralSettings.labels.featureControl}
        subtitle={strings.GeneralSettings.labels.featureControlDescription}
      >
        <div className={classNames.settingsGrid}>
        <GeneralSetting
          id={SettingKeyMap[SettingKey.CanReviewOwnSubmissions]}
          title={strings.GeneralSettings.labels.reviewOwnSubmissionTitle}
          description={strings.GeneralSettings.labels.reviewOwnSubmissionDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.CanReviewOwnSubmissions)}
          generalSettingValue={settings[SettingKey.CanReviewOwnSubmissions]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.CreateGroupFeatureEnabled]}
          title={strings.GeneralSettings.labels.createGroupTitle}
          description={strings.GeneralSettings.labels.createGroupDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.CreateGroupFeatureEnabled)}
          generalSettingValue={settings[SettingKey.CreateGroupFeatureEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsBusinessJustificationRequired]}
          title={strings.GeneralSettings.labels.businessJustificationTitle}
          description={strings.GeneralSettings.labels.businessJustificationDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsBusinessJustificationRequired)}
          generalSettingValue={settings[SettingKey.IsBusinessJustificationRequired]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsDisclaimerEnabled]}
          title={strings.GeneralSettings.labels.isDisclaimerEnabledTitle}
          description={strings.GeneralSettings.labels.isDisclaimerEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsDisclaimerEnabled)}
          generalSettingValue={settings[SettingKey.IsDisclaimerEnabled]}
          disabled={!canEdit}
        />
        </div>
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        title={strings.GeneralSettings.labels.userResources}
        subtitle={strings.HyperlinkSettings.labels.description}
      >
        <UserResourcesSettings
          classNames={classNames}
          strings={strings}
          settings={settings}
          setSettings={setSettings}
          canEdit={canEdit}
          setHasValidationErrors={setHasValidationErrors} />
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        title={strings.ServiceNotifications.labels.serviceNotifications}
        subtitle={strings.ServiceNotifications.labels.description}
      >
        <ServiceNotificationSettings
          classNames={classNames}
          strings={strings}
          config={serviceNotification}
          setConfig={setServiceNotification}
          errors={serviceNotificationErrors}
          saveError={serviceNotificationSaveError}
          canEdit={canEdit} />
      </SettingsSection>
    </div>
  );
}

const AutoApproverSettings: React.FunctionComponent<AutoApproverSettingsProps> = (props: AutoApproverSettingsProps) => {
  const { classNames, strings, settings, setSettings, canEdit } = props;

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  return (
    <SettingsSection
      classNames={classNames}
      title={strings.AutoApproverSettings.labels.controlsTitle}
      subtitle={strings.AutoApproverSettings.labels.description}
    >
      <div className={classNames.settingsGrid}>
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]}
        title={strings.AutoApproverSettings.labels.isAutoApprovalForGroupBasedSyncsEnabledTitle}
        description={strings.AutoApproverSettings.labels.isAutoApprovalForGroupBasedSyncsEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled)}
        generalSettingValue={settings[SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]}
        disabled={!canEdit}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]}
        title={strings.AutoApproverSettings.labels.isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledTitle}
        description={strings.AutoApproverSettings.labels.isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled)}
        generalSettingValue={settings[SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]}
        disabled={!canEdit}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsPerPartAutoApprovalEnabled]}
        title={strings.AutoApproverSettings.labels.isPerPartAutoApprovalEnabledTitle}
        description={strings.AutoApproverSettings.labels.isPerPartAutoApprovalEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsPerPartAutoApprovalEnabled)}
        generalSettingValue={settings[SettingKey.IsPerPartAutoApprovalEnabled]}
        disabled={!canEdit}
      />
      </div>
    </SettingsSection>
  );
}

const UserResourcesSettings: React.FunctionComponent<UserResourcesSettingsProps> = (props: UserResourcesSettingsProps) => {

  const { classNames, strings, settings, setSettings, setHasValidationErrors, canEdit } = props;

  const [urlValidations, setUrlValidations] = useState<{ readonly [key in SettingKey]: boolean }>({
    [SettingKey.DashboardUrl]: true,
    [SettingKey.OutlookWarningUrl]: true,
    [SettingKey.PrivacyPolicyUrl]: true,
    [SettingKey.UIUrl]: true,
    [SettingKey.CanReviewOwnSubmissions]: true,
    [SettingKey.CreateGroupFeatureEnabled]: true,
    [SettingKey.IsBusinessJustificationRequired]: true,
    [SettingKey.IsDisclaimerEnabled]: true,
    [SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]: true,
    [SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]: true,
    [SettingKey.IsPerPartAutoApprovalEnabled]: true,
    [SettingKey.IsAITitleEnabled]: true,
    [SettingKey.IsAICopilotEnabled]: true,
    [SettingKey.IsAISearchForUserEnabled]: true,
    [SettingKey.IsAIRunExplanationEnabled]: true,
    [SettingKey.CopilotTemperature]: true,
    [SettingKey.CopilotTopP]: true,
    [SettingKey.CopilotInstructions]: true,
    [SettingKey.CopilotSuggestedPrompts]: true,
  });

  useEffect(() => {
    const hasValidationErrors = hasUrlValidationErrors();
    setHasValidationErrors(hasValidationErrors);
  }, [urlValidations]);

  const hasUrlValidationErrors = () => Object.values(urlValidations).some((value) => value !== true);

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  const handleUrlSettingValidation = (settingKey: SettingKey) => (isValid: boolean) => {
    setUrlValidations((validations) => ({ ...validations, [settingKey]: isValid }));
  };

  return (
    <div>
      <div className={classNames.tiles}>
        <HyperlinkSetting
          title={strings.HyperlinkSettings.dashboardLink.title}
          description={strings.HyperlinkSettings.dashboardLink.description}
          link={settings[SettingKey.DashboardUrl]}
          onLinkChange={handleSettingChange(SettingKey.DashboardUrl)}
          onValidation={handleUrlSettingValidation(SettingKey.DashboardUrl)}
          disabled={!canEdit}
        ></HyperlinkSetting>
        <HyperlinkSetting
          title={strings.HyperlinkSettings.outlookWarningLink.title}
          description={strings.HyperlinkSettings.outlookWarningLink.description}
          link={settings[SettingKey.OutlookWarningUrl]}
          onLinkChange={handleSettingChange(SettingKey.OutlookWarningUrl)}
          onValidation={handleUrlSettingValidation(SettingKey.OutlookWarningUrl)}
          disabled={!canEdit}
        ></HyperlinkSetting>
        <HyperlinkSetting
          title={strings.HyperlinkSettings.privacyPolicyLink.title}
          description={strings.HyperlinkSettings.privacyPolicyLink.description}
          link={settings[SettingKey.PrivacyPolicyUrl]}
          onLinkChange={handleSettingChange(SettingKey.PrivacyPolicyUrl)}
          onValidation={handleUrlSettingValidation(SettingKey.PrivacyPolicyUrl)}
          disabled={!canEdit}
        ></HyperlinkSetting>
      </div>
    </div>
  );
}

const CustomSourceSettings: React.FunctionComponent<CustomSourceSettingsProps> = (props: CustomSourceSettingsProps) => {

  const { classNames, sqlMembershipSource, sqlMembershipSourceAttributes, setNewAttributes, setNewSource, handleGetValues, setHasValidationErrors, strings, canEdit } = props;

  const [attributeMap, setAttributeMap] = useState<{ [key: string]: SqlMembershipAttribute } | undefined>(undefined);
  const [isSortedDescending, setIsSortedDescending] = useState(false);
  const [sortKey, setSortKey] = useState('attribute');
  const [sourceNameValue, setSourceNameValue] = useState(sqlMembershipSource?.customLabel || '');

  // Using useMemo to only recalculate attributes when sqlMembershipSourceAttributes change
  const attributes = useMemo(() => {
    return sqlMembershipSourceAttributes;
  }, [sqlMembershipSourceAttributes]);

  useEffect(() => {
    setSourceNameValue(sqlMembershipSource?.customLabel || '');
  }, [sqlMembershipSource?.customLabel]);

  useEffect(() => {
    const newAttributeMap = attributes?.reduce((acc: { [key: string]: SqlMembershipAttribute }, currentItem: SqlMembershipAttribute) => {
      const { name } = currentItem;
      acc[name] = {
          ...currentItem,
          ...attributeMap?.[currentItem.name]
       };
      return acc;
    }, {});

    setAttributeMap(newAttributeMap);
  }, [attributes]);

  useEffect(() => {
    const newAttributeSettings = attributeMap ? Object.values(attributeMap) : undefined;
    setNewAttributes(newAttributeSettings);
  }, [attributeMap]);

  const onSourceNameChange: (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string | undefined) => void = (event, newValue) => {
    const newSourceName = newValue ? newValue : '';
    setNewSource({ ...sqlMembershipSource, customLabel: newSourceName } as SqlMembershipSource);
    setSourceNameValue(newSourceName);
  };

  const onColumnHeaderClick: (event?: any, column?: IColumn) => void = (event, column) => {
    if (column) {
      const isSortedDescending: boolean = !!column.isSorted && !column.isSortedDescending;
      setIsSortedDescending(isSortedDescending);
      setSortKey(column.key);
    }
  };

  const handleFieldChange = useCallback(
    (attributeName: any, fieldName: any, newValue: any): any => {
      setAttributeMap((prevRowsData: any) => ({
        ...prevRowsData,
        [attributeName]: {
          ...prevRowsData[attributeName],
          [fieldName]: newValue,
        },
      }));
    },
    [setAttributeMap]
  );

  const handleDropdownClick = useCallback(
    (attribute: SqlMembershipAttribute): any => {
      if (attribute && !attribute.values) {
        handleGetValues(attribute);
      }
    },
    [attributeMap]
  );

  // The API stores the threshold as a fraction (0-1); the grid edits it as a percentage (0-100).
  const [invalidThresholds, setInvalidThresholds] = useState<{ [key: string]: boolean }>({});

  const handleNullThresholdChange = useCallback(
    (attributeName: string, raw: string, isValid: boolean): void => {
      setInvalidThresholds((previous) => ({ ...previous, [attributeName]: !isValid }));

      if (!isValid) {
        // Leave the stored value untouched while the entry is invalid; saving is blocked meanwhile.
        return;
      }

      handleFieldChange(attributeName, 'nullThreshold', raw.trim() === '' ? undefined : percentTextToFraction(raw));
    },
    [handleFieldChange]
  );

  // Block saving while any threshold entry is invalid so a bad value can't be persisted.
  useEffect(() => {
    setHasValidationErrors(Object.values(invalidThresholds).some(Boolean));
  }, [invalidThresholds, setHasValidationErrors]);

  // The Pivot unmounts this tab when another is selected. Invalid entries are never written to
  // the draft, so release the flag on the way out; otherwise the page-level Save button stays
  // permanently disabled with no visible error to explain why.
  useEffect(() => {
    return () => setHasValidationErrors(false);
  }, [setHasValidationErrors]);

  const onRenderItemColumn = (item?: any, index?: number, column?: IColumn): JSX.Element => {

    if (!item || !column || !attributeMap) {
      return <div></div>;
    }

    const fieldContent = attributeMap[item.name][column?.fieldName as keyof SqlMembershipAttribute] as string;

    switch (column?.key) {
      case 'customLabel':
        return (
          <CustomLabelCell
            value={fieldContent}
            placeholder={strings.CustomSourceSettings.labels.customLabelInputPlaceHolder}
            onChange={(e, newValue) => {
              handleFieldChange(item.name, column.fieldName, newValue);
            }}
            className={classNames.customLabelTextField}
            disabled={!canEdit}
          />
        );
      case 'attributeValues':
        return (
          <AttributeValuesCell
            classNames={classNames}
            values={attributeMap[item.name].values}
            strings={strings}
            onDropdownClick={() => {
              handleDropdownClick(attributeMap[item.name]);
            }}
          />
        );
        case 'description':
          return (
            <TextField
              value={fieldContent}
              placeholder={strings.CustomSourceSettings.labels.descriptionPlaceHolder}
              onChange={(e, newValue) => {
                handleFieldChange(item.name, column.fieldName, newValue);
              }}
              multiline rows={3}
              disabled={!canEdit}
              styles={{ fieldGroup: classNames.descriptionTextField }}
            />
          );
        case 'enabled':
          return (
            <Toggle
              title={strings.CustomSourceSettings.labels.enabledToggleTitle}
              disabled={!canEdit}
              checked={fieldContent !== undefined ? Boolean(fieldContent) : true}
              onChange={(e, checked) => handleFieldChange(item.name, column.fieldName, checked)}
            />
          );
        case 'sensitive':
          return (
            <Toggle
              title={strings.CustomSourceSettings.labels.sensitiveToggleTitle}
              disabled={!canEdit}
              checked={fieldContent !== undefined ? Boolean(fieldContent) : false}
              onChange={(e, checked) => handleFieldChange(item.name, column.fieldName, checked)}
            />
          );
        case 'nullThreshold':
          return (
            <NullThresholdCell
              key={item.name}
              attributeName={item.name}
              storedValue={attributeMap[item.name].nullThreshold}
              title={strings.CustomSourceSettings.labels.nullThresholdTitle}
              ariaLabel={`${strings.CustomSourceSettings.labels.nullThresholdColumn} ${item.name}`}
              placeholder={strings.CustomSourceSettings.labels.nullThresholdPlaceHolder}
              validationErrorMessage={strings.CustomSourceSettings.labels.nullThresholdValidationError}
              onValueChange={handleNullThresholdChange}
              disabled={!canEdit}
            />
          );
      default:
        return (
          <div className={classNames.defaultColumnSpan}>
            <Text variant="medium">{fieldContent}</Text>
          </div>
        );
    }
  };

  const columns = [
    {
      key: 'enabled',
      name: strings.CustomSourceSettings.labels.enabledColumn,
      fieldName: 'enabled',
      minWidth: 100,
      maxWidth: 120,
      isResizable: true,
      isSorted: sortKey === 'enabled',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'sensitive',
      name: strings.CustomSourceSettings.labels.sensitiveColumn,
      fieldName: 'isSensitive',
      minWidth: 90,
      maxWidth: 110,
      isResizable: true,
      isSorted: sortKey === 'sensitive',
      isSortedDescending,
    },
    {
      key: 'name',
      name: strings.CustomSourceSettings.labels.attributeColumn,
      fieldName: 'name',
      minWidth: 180,
      maxWidth: 280,
      isResizable: true,
      isSorted: sortKey === 'name',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'customLabel',
      name: strings.CustomSourceSettings.labels.customLabelColumn,
      fieldName: 'customLabel',
      minWidth: 160,
      maxWidth: 220,
      isResizable: true,
      isSorted: sortKey === 'customLabel',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'attributeValues',
      name: strings.CustomSourceSettings.labels.valuesColumn,
      fieldName: 'attributeValues',
      minWidth: 160,
      maxWidth: 200,
    },
    {
      key: 'nullThreshold',
      name: strings.CustomSourceSettings.labels.nullThresholdColumn,
      fieldName: 'nullThreshold',
      minWidth: 140,
      maxWidth: 200,
      isResizable: true,
      isSorted: sortKey === 'nullThreshold',
      isSortedDescending,
    },
    {
      key: 'description',
      name: strings.CustomSourceSettings.labels.descriptionColumn,
      fieldName: 'description',
      minWidth: 260,
      maxWidth: 520,
      isResizable: true,
      isSorted: sortKey === 'description',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    }
  ];

  const sortedItems = [...(attributes ? attributes : [])].sort((a, b) => {
    if (sortKey === 'name' || sortKey === 'customLabel') {
      return isSortedDescending
        ? (b[sortKey] || '').localeCompare(a[sortKey] || '')
        : (a[sortKey] || '').localeCompare(b[sortKey] || '');
    }
    if (sortKey === 'enabled' || sortKey === 'sensitive') {
      const aVal = (sortKey === 'sensitive' ? a.isSensitive : a.enabled) ? 1 : 0;
      const bVal = (sortKey === 'sensitive' ? b.isSensitive : b.enabled) ? 1 : 0;
      return isSortedDescending ? bVal - aVal : aVal - bVal;
    }
    if (sortKey === 'nullThreshold') {
      // Attributes without an override sort together, ahead of any explicit value.
      const aVal = a.nullThreshold ?? -1;
      const bVal = b.nullThreshold ?? -1;
      return isSortedDescending ? bVal - aVal : aVal - bVal;
    }
    return 0;
  });

  return (
    <div>
      <SettingsSection
        classNames={classNames}
        title={strings.CustomSourceSettings.labels.sourceLabeling}
        subtitle={strings.CustomSourceSettings.labels.sourceDescription}
      >
        <div className={classNames.sourceNameTextFieldContainer}>
          <TextField
            label={strings.CustomSourceSettings.labels.sourceCustomLabelInput}
            required={!sourceNameValue.trim()}
            value={sourceNameValue}
            disabled={!canEdit || sqlMembershipSource === undefined}
            onChange={onSourceNameChange}
            placeholder={strings.CustomSourceSettings.labels.customLabelInputPlaceHolder}
            styles={{ fieldGroup: classNames.sourceNameTextField }}
          />
        </div>
      </SettingsSection>

      <SettingsSection
        classNames={classNames}
        title={strings.CustomSourceSettings.labels.listOfAttributes}
        subtitle={strings.CustomSourceSettings.labels.listOfAttributesDescription}
      >
        <div className={classNames.detailsListContainer}>
          <ShimmeredDetailsList
            setKey="items"
            items={sortedItems || []}
            columns={columns}
            selectionMode={SelectionMode.none}
            onRenderItemColumn={onRenderItemColumn}
            onColumnHeaderClick={onColumnHeaderClick}
            enableShimmer={sortedItems.length === 0}
          />
        </div>
      </SettingsSection>
    </div>
  );
}

// The threshold is stored as a fraction (0-1) but edited as a percentage (0-100).
// Both conversions round away binary floating point artifacts (e.g. 0.29 * 100 = 28.999999999999996).
export const percentTextToFraction = (raw: string): number => Math.round((Number(raw) / 100) * 1e8) / 1e8;

export const fractionToPercentText = (value: number | undefined | null): string =>
  value === undefined || value === null ? '' : String(Math.round(value * 1e6) / 1e4);

export const isValidNullThresholdInput = (raw: string): boolean => {
  if (raw.trim() === '') {
    return true;
  }
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed >= 0 && parsed <= 100;
};

// Owns its input text locally: DetailsList does not re-render a row when only the parent's
// draft state changes, so keeping the raw text here guarantees typing and validation errors
// are always reflected in the cell.
const NullThresholdCell = React.memo((props: NullThresholdCellProps) => {
  const { attributeName, storedValue, title, ariaLabel, placeholder, validationErrorMessage, disabled, onValueChange } = props;

  const [rawValue, setRawValue] = useState(fractionToPercentText(storedValue));
  const [errorMessage, setErrorMessage] = useState('');

  const handleChange = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string) => {
    const raw = newValue ?? '';
    const isValid = isValidNullThresholdInput(raw);

    setRawValue(raw);
    setErrorMessage(isValid ? '' : validationErrorMessage);
    onValueChange(attributeName, raw, isValid);
  };

  return (
    <TextField
      title={title}
      ariaLabel={ariaLabel}
      value={rawValue}
      placeholder={placeholder}
      errorMessage={errorMessage}
      disabled={disabled}
      onChange={handleChange}
    />
  );
});

const CustomLabelCell = React.memo((props: CustomLabelCellProps) => {
  const { className, value, onChange, placeholder, disabled } = props;
  return (
    <TextField
      value={value}
      styles={{ fieldGroup: className }}
      placeholder={placeholder}
      disabled={disabled}
      onChange={onChange}
    />
  );
});

const AttributeValuesCell = React.memo((props: AttributeValuesCellProps) => {
  const { classNames, values, onDropdownClick, strings } = props;

  const getDropdownOptions = (values : string[]) => {
    if (!values) {
      return [];
    }

    return values.map((value) => {
      return {
        key: value,
        text: value,
        disabled: true,
        title: value
      }
    });
  }

  const isLoading = values === undefined || values === null;

  const onRenderList: IRenderFunction<ISelectableDroppableTextProps<IDropdown, HTMLDivElement>> = (props, defaultRender) => {

    if (isLoading) {
      return (
        <div>
          <Spinner styles={{ root: classNames.valuesDropdownSpinner }} label={strings.CustomSourceSettings.labels.valuesDropdownSpinnerLabel} />
        </div>
      );
    }

    if (props?.options?.length === 0) {
      return (
        <div style={{ padding: '8px 12px' }}>
          {strings.CustomSourceSettings.labels.valuesDropdownNoValuesLabel}
        </div>
      );
    }

    return (
      <div>
        {defaultRender!(props)}
      </div>
    );
};

  return (
    <Dropdown
      title={strings.CustomSourceSettings.labels.valuesDropdownTitle}
      placeholder={strings.CustomSourceSettings.labels.valuesDropdownPlaceholder}
      onRenderList={onRenderList}
      onClick={onDropdownClick}
      options={getDropdownOptions(values)}
      dropdownWidth={'auto'}
      styles={{ dropdown: classNames.valuesDropdown, title: classNames.valuesDropdownTitle }}
    />
  );
});

const AISettings: React.FunctionComponent<AISettingsProps> = (props: AISettingsProps) => {
  const { classNames, strings, settings, setSettings, defaultAIPrompt, canEdit } = props;
  const [showDefaults, setShowDefaults] = useState(false);

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  return (
    <div>
      <SettingsSection
        classNames={classNames}
        title={strings.AISettings.labels.copilotAvailabilityTitle}
        subtitle={strings.AISettings.labels.copilotAvailabilityDescription}
      >
        <div className={classNames.settingsGrid}>
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAITitleEnabled]}
          title={strings.AISettings.labels.isAITitleEnabledTitle}
          description={strings.AISettings.labels.isAITitleEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAITitleEnabled)}
          generalSettingValue={settings[SettingKey.IsAITitleEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAICopilotEnabled]}
          title={strings.AISettings.labels.isAICopilotEnabledTitle}
          description={strings.AISettings.labels.isAICopilotEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAICopilotEnabled)}
          generalSettingValue={settings[SettingKey.IsAICopilotEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAISearchForUserEnabled]}
          title={strings.AISettings.labels.isAISearchForUserEnabledTitle}
          description={strings.AISettings.labels.isAISearchForUserEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAISearchForUserEnabled)}
          generalSettingValue={settings[SettingKey.IsAISearchForUserEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAIRunExplanationEnabled]}
          title={strings.AISettings.labels.isAIRunExplanationEnabledTitle}
          description={strings.AISettings.labels.isAIRunExplanationEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAIRunExplanationEnabled)}
          generalSettingValue={settings[SettingKey.IsAIRunExplanationEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAIRejectionFeedbackRefinementEnabled]}
          title={strings.AISettings.labels.isAIRejectionFeedbackRefinementEnabledTitle}
          description={strings.AISettings.labels.isAIRejectionFeedbackRefinementEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAIRejectionFeedbackRefinementEnabled)}
          generalSettingValue={settings[SettingKey.IsAIRejectionFeedbackRefinementEnabled]}
          disabled={!canEdit}
        />
        </div>
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        showDivider
        title={strings.AISettings.labels.suggestedPromptsTitle}
        subtitle={strings.AISettings.labels.suggestedPromptsDescription}
      >
        <SuggestedPromptsEditor classNames={classNames} strings={strings} settings={settings} setSettings={setSettings} canEdit={canEdit} />
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        showDivider
        title={strings.AISettings.labels.copilotInstructionsPromptTitle}
        subtitle={strings.AISettings.labels.copilotInstructionsPromptDescription}
      >
        <Text variant="small" block className={classNames.aiSettingsLeaveEmptyNote}>
          <Icon iconName="Info" className={classNames.aiSettingsLeaveEmptyNoteIcon} />
          {strings.AISettings.labels.leaveEmptyNote}
        </Text>
        {defaultAIPrompt && (
          <div className={classNames.aiSettingsDefaultInstructionsContainer}>
            <button
              onClick={() => setShowDefaults(!showDefaults)}
              className={classNames.aiSettingsDefaultInstructionsToggle}
            >
              <Icon iconName={showDefaults ? 'ChevronDown' : 'ChevronRight'} className={classNames.aiSettingsDefaultInstructionsToggleIcon} />
              {strings.AISettings.labels.currentDefaultInstructions}
            </button>
            {showDefaults && (
              <div className={classNames.aiSettingsDefaultInstructionsContent}>
                {defaultAIPrompt}
              </div>
            )}
          </div>
        )}
        <TextField
          multiline
          rows={12}
          value={settings[SettingKey.CopilotInstructions]}
          placeholder={strings.AISettings.labels.copilotInstructionsPromptPlaceholder}
          disabled={!canEdit}
          onChange={(_, newValue) => handleSettingChange(SettingKey.CopilotInstructions)(newValue ?? '')}
        />
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        showDivider
        title={strings.AISettings.labels.modelBehaviorTitle}
        subtitle={strings.AISettings.labels.modelBehaviorDescription}
      >
        <div className={classNames.settingsGrid}>
          <ModelBehaviorSetting
            classNames={classNames}
            title={strings.AISettings.labels.copilotTemperatureTitle}
            description={strings.AISettings.labels.copilotTemperatureDescription}
            value={settings[SettingKey.CopilotTemperature]}
            fallbackValue={0.7}
            canEdit={canEdit}
            onValueChange={handleSettingChange(SettingKey.CopilotTemperature)}
          />
          <ModelBehaviorSetting
            classNames={classNames}
            title={strings.AISettings.labels.copilotTopPTitle}
            description={strings.AISettings.labels.copilotTopPDescription}
            value={settings[SettingKey.CopilotTopP]}
            fallbackValue={0.9}
            canEdit={canEdit}
            onValueChange={handleSettingChange(SettingKey.CopilotTopP)}
          />
        </div>
      </SettingsSection>
    </div>
  );
}

const ModelBehaviorSetting: React.FunctionComponent<{
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  title: string;
  description: string;
  value: string;
  fallbackValue: number;
  canEdit: boolean;
  onValueChange: (newValue: string) => void;
}> = ({ classNames, title, description, value, fallbackValue, canEdit, onValueChange }) => {
  const theme = useTheme();
  const parsed = parseFloat(value);
  return (
    <div className={classNames.modelBehaviorCard}>
      <div className={classNames.modelBehaviorHeader}>
        <Text variant="mediumPlus" className={classNames.modelBehaviorTitle}>{title}</Text>
        <Slider
          className={classNames.modelBehaviorSlider}
          min={0}
          max={1}
          step={0.05}
          value={Number.isFinite(parsed) ? parsed : fallbackValue}
          showValue
          disabled={!canEdit}
          ariaLabel={title}
          onChange={(newValue) => onValueChange(newValue.toString())}
          styles={{
            root: { flexGrow: 1, minWidth: 0 },
            container: { alignItems: 'center' },
            slideBox: { flexGrow: 1, minWidth: 0 },
            activeSection: { backgroundColor: theme.palette.themePrimary, height: 4, borderRadius: 2 },
            inactiveSection: { backgroundColor: theme.palette.themeLighter, height: 4, borderRadius: 2 },
            thumb: {
              borderColor: theme.palette.themePrimary,
              borderWidth: 2,
              width: 16,
              height: 16,
              top: -6,
            },
            valueLabel: {
              marginLeft: 12,
              minWidth: 28,
              color: theme.palette.neutralPrimary,
            },
          }}
        />
      </div>
      <Text variant="small" block className={classNames.modelBehaviorDescription}>{description}</Text>
    </div>
  );
}

const SuggestedPromptsEditor: React.FunctionComponent<{
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: AdminConfigViewProps['strings'];
  settings: { readonly [key in SettingKey]: string };
  setSettings: React.Dispatch<React.SetStateAction<{ readonly [key in SettingKey]: string }>>;
  canEdit: boolean;
}> = ({ classNames, strings, settings, setSettings, canEdit }) => {
  const theme = useTheme();

  const defaultSuggestedPrompts = useMemo(() => [
    { label: strings.AISettings.labels.suggestedPromptDefault1Label, prompt: strings.AISettings.labels.suggestedPromptDefault1Prompt },
    { label: strings.AISettings.labels.suggestedPromptDefault2Label, prompt: strings.AISettings.labels.suggestedPromptDefault2Prompt },
  ], [strings]);

  type PromptWithId = { id: number; label: string; prompt: string };
  const nextId = useRef(0);

  const assignIds = (items: Array<{ label: string; prompt: string }>): PromptWithId[] =>
    items.map((item) => ({ ...item, id: nextId.current++ }));

  const parsePrompts = (json: string): Array<{ label: string; prompt: string }> => {
    if (!json) return [];
    try {
      const parsed = JSON.parse(json);
      if (Array.isArray(parsed)) {
        return parsed
          .filter((item: any) => typeof item === 'object' && item !== null)
          .map((item: any) => ({
            label: typeof item.label === 'string' ? item.label : '',
            prompt: typeof item.prompt === 'string' ? item.prompt : '',
          }));
      }
    } catch { /* invalid JSON */ }
    return [];
  };

  const [prompts, setPrompts] = useState<PromptWithId[]>(() =>
    assignIds(parsePrompts(settings[SettingKey.CopilotSuggestedPrompts]))
  );

  const syncToSettings = (updated: PromptWithId[]) => {
    setPrompts(updated);
    setSettings((prev) => ({
      ...prev,
      [SettingKey.CopilotSuggestedPrompts]: JSON.stringify(
        updated.map(({ label, prompt }) => ({ label, prompt }))
      ),
    }));
  };

  const handleFieldChange = (id: number, field: 'label' | 'prompt', value: string) => {
    const updated = prompts.map((p) => (p.id === id ? { ...p, [field]: value } : p));
    syncToSettings(updated);
  };

  const handleRemove = (id: number) => {
    const updated = prompts.filter((p) => p.id !== id);
    syncToSettings(updated);
  };

  const handleAdd = () => {
    syncToSettings([...prompts, { id: nextId.current++, label: '', prompt: '' }]);
  };

  const handlePopulateDefaults = () => {
    syncToSettings(assignIds(defaultSuggestedPrompts));
  };

  return (
    <div>
      <div className={classNames.suggestedPromptsGrid}>
      {prompts.map((p) => (
        <div key={p.id} className={classNames.suggestedPromptRow}>
          <TextField
            styles={{ root: classNames.suggestedPromptLabelField }}
            placeholder={strings.AISettings.labels.suggestedPromptLabelPlaceholder}
            value={p.label}
            disabled={!canEdit}
            onChange={(_, val) => handleFieldChange(p.id, 'label', val ?? '')}
          />
          <TextField
            styles={{ root: classNames.suggestedPromptPromptField }}
            placeholder={strings.AISettings.labels.suggestedPromptPromptPlaceholder}
            value={p.prompt}
            disabled={!canEdit}
            onChange={(_, val) => handleFieldChange(p.id, 'prompt', val ?? '')}
          />
          <IconButton
            iconProps={{ iconName: 'Delete' }}
            title={strings.AISettings.labels.suggestedPromptRemove}
            ariaLabel={strings.AISettings.labels.suggestedPromptRemove}
            disabled={!canEdit}
            onClick={() => handleRemove(p.id)}
            className={classNames.suggestedPromptRemoveButton}
          />
        </div>
      ))}
      </div>
      <div className={classNames.suggestedPromptsActions}>
        <DefaultButton
          iconProps={{ iconName: 'Add' }}
          text={strings.AISettings.labels.suggestedPromptAdd}
          disabled={!canEdit}
          onClick={handleAdd}
          className={classNames.suggestedPromptAddButton}
          styles={{
            label: { color: theme.palette.neutralPrimary },
            icon: { color: theme.palette.themePrimary },
            iconHovered: { color: theme.palette.themePrimary },
            iconPressed: { color: theme.palette.themePrimary },
          }}
        />
        <ActionButton
          iconProps={{ iconName: 'Refresh' }}
          text={strings.AISettings.labels.suggestedPromptPopulateDefaults}
          disabled={!canEdit}
          onClick={handlePopulateDefaults}
          styles={{
            root: { color: theme.palette.neutralPrimary },
            label: { color: theme.palette.neutralPrimary },
            rootHovered: { color: theme.palette.neutralPrimary },
            labelHovered: { color: theme.palette.neutralPrimary },
          }}
        />
      </div>
    </div>
  );
};
