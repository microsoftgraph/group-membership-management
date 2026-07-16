// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { classNamesFunction, Toggle, IProcessedStyleSet, Pivot, PivotItem, PrimaryButton, TextField, Text, IColumn, SelectionMode, ShimmeredDetailsList, Dropdown, Spinner, IRenderFunction, ISelectableDroppableTextProps, IDropdown, Slider, Icon, IconButton, ActionButton } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import {
  AdminConfigStyleProps,
  AdminConfigStyles,
  AdminConfigViewProps,
  CustomLabelCellProps,
  AttributeValuesCellProps,
  CustomSourceSettingsProps,
  HyperlinkSettingsProps,
  OperationsProps,
  GeneralSettingsProps,
  AISettingsProps } from './AdminConfig.types';
import { PageSection } from '../../components/PageSection';
import { HyperlinkSetting } from '../../components/HyperlinkSetting';
import { Operation } from '../../components/Operation';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { SettingKey, SettingKeyMap, SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import { GeneralSetting } from '../../components/GeneralSetting';
import { AlertBannerAdmin } from './AlertBannerAdmin';

const getClassNames = classNamesFunction<AdminConfigStyleProps, AdminConfigStyles>();

export const AdminConfigView: React.FunctionComponent<AdminConfigViewProps> = (props: AdminConfigViewProps) => {
  // extract props
  const { className, isSaving, onSave, handleGetValues, settings, sqlMembershipSource, sqlMembershipSourceAttributes, strings, styles,
    isHyperlinkAdmin,
    isCustomMembershipProviderAdmin,
    isOperationsResetAdministrator,
    isGeneralSettingsAdministrator,
    isAISettingsAdministrator,
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
    return hasSettingsChanges || hasSourceChanges || hasAttributesChanges;
  };

  // setup ui event handlers
  const handleOnSaveButtonClick = () => {
    onSave(newSettings, newSource, newAttributes);
  };

  return (
    <Page>
      <PageHeader />
      <div className={classNames.root}>
        <div className={classNames.card}>
          <PageSection>
            <div className={classNames.title}>{strings.labels.pageTitle}</div>
          </PageSection>
        </div>
        <div className={classNames.card}>
          <PageSection>
            <Pivot>
              {isHyperlinkAdmin &&
                <PivotItem
                  headerText={strings.HyperlinkSettings.labels.hyperlinks}
                  headerButtonProps={{
                    'data-order': 1,
                    'data-title': strings.HyperlinkSettings.labels.hyperlinks,
                  }}
                >
                  <HyperlinkSettings
                    classNames={classNames}
                    strings={strings}
                    settings={newSettings}
                    setSettings={setNewSettings}
                    setHasValidationErrors={setHasUrlValidationErrors} />

                </PivotItem>
              }
              {isCustomMembershipProviderAdmin &&
                <PivotItem
                  headerText={'Custom Source'}
                  headerButtonProps={{
                    'data-order': 2,
                    'data-title': 'Custom Source',
                  }}
                >
                  <CustomSourceSettings
                    classNames={classNames}
                    sqlMembershipSource={sqlMembershipSource}
                    sqlMembershipSourceAttributes={sqlMembershipSourceAttributes}
                    setNewAttributes={setNewAttributes}
                    setNewSource={setNewSource}
                    handleGetValues={handleGetValues}
                    strings={strings} />
                </PivotItem>
              }
              {isOperationsResetAdministrator &&
                <PivotItem
                  headerText={strings.Operations.labels.operations}
                  headerButtonProps={{
                    'data-order': 1,
                    'data-title': strings.Operations.labels.operations,
                  }}
                >
                  <Operations
                    classNames={classNames}
                    strings={strings} />
                </PivotItem>
              }
              {isGeneralSettingsAdministrator &&
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
                    setSettings={setNewSettings} />
                </PivotItem>
              }
              {isAISettingsAdministrator &&
                <PivotItem
                  headerText={strings.AISettings.labels.aiSettings}
                  headerButtonProps={{
                    'data-order': 5,
                    'data-title': strings.AISettings.labels.aiSettings,
                  }}
                >
                  <AISettings
                    classNames={classNames}
                    strings={strings}
                    settings={newSettings}
                    setSettings={setNewSettings}
                    defaultAIPrompt={defaultAIPrompt} />
                </PivotItem>
              }
              {isGeneralSettingsAdministrator &&
                <PivotItem
                  headerText={'Alert Banner'}
                  headerButtonProps={{
                    'data-order': 6,
                    'data-title': 'Alert Banner',
                  }}
                >
                  <AlertBannerAdmin />
                </PivotItem>
              }
            </Pivot>
          </PageSection>
        </div>
        <div className={classNames.bottomContainer}>
          <PrimaryButton
            text={strings.labels.saveButton}
            onClick={handleOnSaveButtonClick}
            disabled={!hasChanges() || hasUrlValidationErrors || isSaving}
          ></PrimaryButton>
        </div>
      </div>
    </Page>
  );
};

const Operations: React.FunctionComponent<OperationsProps> = (props: OperationsProps) => {
  const { classNames, strings} = props;
  return (
    <div>
      <Operation
          title={strings.Operations.labels.title}
          description={strings.Operations.labels.description}
          buttonText={strings.Operations.buttons}
        ></Operation>
      </div>
  );
}

const GeneralSettings: React.FunctionComponent<GeneralSettingsProps> = (props: GeneralSettingsProps) => {
  const { strings, settings, setSettings } = props;

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  return (
    <div>
      <GeneralSetting
        id={SettingKeyMap[SettingKey.DashboardUrl]}
        title={strings.GeneralSettings.labels.reviewOwnSubmissionTitle}
        description={strings.GeneralSettings.labels.reviewOwnSubmissionDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.CanReviewOwnSubmissions)}
        generalSettingValue={settings[SettingKey.CanReviewOwnSubmissions]}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.CreateGroupFeatureEnabled]}
        title={strings.GeneralSettings.labels.createGroupTitle}
        description={strings.GeneralSettings.labels.createGroupDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.CreateGroupFeatureEnabled)}
        generalSettingValue={settings[SettingKey.CreateGroupFeatureEnabled]}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsBusinessJustificationRequired]}
        title={strings.GeneralSettings.labels.businessJustificationTitle}
        description={strings.GeneralSettings.labels.businessJustificationDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsBusinessJustificationRequired)}
        generalSettingValue={settings[SettingKey.IsBusinessJustificationRequired]}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsDisclaimerEnabled]}
        title={strings.GeneralSettings.labels.isDisclaimerEnabledTitle}
        description={strings.GeneralSettings.labels.isDisclaimerEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsDisclaimerEnabled)}
        generalSettingValue={settings[SettingKey.IsDisclaimerEnabled]}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]}
        title={strings.GeneralSettings.labels.isAutoApprovalForGroupBasedSyncsEnabledTitle}
        description={strings.GeneralSettings.labels.isAutoApprovalForGroupBasedSyncsEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled)}
        generalSettingValue={settings[SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]}
        title={strings.GeneralSettings.labels.isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledTitle}
        description={strings.GeneralSettings.labels.isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled)}
        generalSettingValue={settings[SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]}
      />
    </div>
  );
}

const HyperlinkSettings: React.FunctionComponent<HyperlinkSettingsProps> = (props: HyperlinkSettingsProps) => {

  const { classNames, strings, settings, setSettings, setHasValidationErrors } = props;

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
    [SettingKey.IsAITitleEnabled]: true,
    [SettingKey.IsAICopilotEnabled]: true,
    [SettingKey.IsAISearchForUserEnabled]: true,
    [SettingKey.IsAIRunExplanationEnabled]: true,
    [SettingKey.RunHistoryOpenViewingAndUnifiedTab]: true,
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
      <div className={classNames.description}>{strings.HyperlinkSettings.labels.description}</div>
      <div className={classNames.tiles}>
        <HyperlinkSetting
          title={strings.HyperlinkSettings.dashboardLink.title}
          description={strings.HyperlinkSettings.dashboardLink.description}
          link={settings[SettingKey.DashboardUrl]}
          onLinkChange={handleSettingChange(SettingKey.DashboardUrl)}
          onValidation={handleUrlSettingValidation(SettingKey.DashboardUrl)}
        ></HyperlinkSetting>
        <HyperlinkSetting
          title={strings.HyperlinkSettings.outlookWarningLink.title}
          description={strings.HyperlinkSettings.outlookWarningLink.description}
          link={settings[SettingKey.OutlookWarningUrl]}
          onLinkChange={handleSettingChange(SettingKey.OutlookWarningUrl)}
          onValidation={handleUrlSettingValidation(SettingKey.OutlookWarningUrl)}
        ></HyperlinkSetting>
        <HyperlinkSetting
          title={strings.HyperlinkSettings.privacyPolicyLink.title}
          description={strings.HyperlinkSettings.privacyPolicyLink.description}
          link={settings[SettingKey.PrivacyPolicyUrl]}
          onLinkChange={handleSettingChange(SettingKey.PrivacyPolicyUrl)}
          onValidation={handleUrlSettingValidation(SettingKey.PrivacyPolicyUrl)}
        ></HyperlinkSetting>
      </div>
    </div>
  );
}

const CustomSourceSettings: React.FunctionComponent<CustomSourceSettingsProps> = (props: CustomSourceSettingsProps) => {

  const { classNames, sqlMembershipSource, sqlMembershipSourceAttributes, setNewAttributes, setNewSource, handleGetValues, strings } = props;

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
              styles={{ fieldGroup: classNames.descriptionTextField }}
            />
          );
        case 'enabled':
          return (
            <Toggle
              title={strings.CustomSourceSettings.labels.enabledToggleTitle}
              checked={fieldContent !== undefined ? Boolean(fieldContent) : true}
              onChange={(e, checked) => handleFieldChange(item.name, column.fieldName, checked)}
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
      key: 'name',
      name: strings.CustomSourceSettings.labels.attributeColumn,
      fieldName: 'name',
      minWidth: 160,
      maxWidth: 240,
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
      maxWidth: 170,
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
      maxWidth: 170,
    },
    {
      key: 'description',
      name: strings.CustomSourceSettings.labels.descriptionColumn,
      fieldName: 'description',
      minWidth: 160,
      maxWidth: 240,
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
    return 0;
  });

  return (
    <div>
      <div className={classNames.sourceNameDescriptionContainer}>
        <Text styles={{ root: classNames.descriptionText }} variant="medium" block>
          {strings.CustomSourceSettings.labels.sourceDescription}
        </Text>
      </div>

      <div className={classNames.sourceNameTextFieldContainer}>
        <TextField
          label={strings.CustomSourceSettings.labels.sourceCustomLabelInput}
          required={!sourceNameValue.trim()}
          value={sourceNameValue}
          disabled={sqlMembershipSource === undefined}
          onChange={onSourceNameChange}
          placeholder={strings.CustomSourceSettings.labels.customLabelInputPlaceHolder}

          styles={{ fieldGroup: classNames.sourceNameTextField }}
        />
      </div>

      <div className={classNames.listOfAttributesTitleDescriptionContainer}>
        <Text variant="large" block>
          {strings.CustomSourceSettings.labels.listOfAttributes}
        </Text>
        <Text styles={{ root: classNames.descriptionText }} variant="medium" block>
          {strings.CustomSourceSettings.labels.listOfAttributesDescription}
        </Text>
      </div>

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
    </div>
  );
}

const CustomLabelCell = React.memo((props: CustomLabelCellProps) => {
  const { className, value, onChange, placeholder } = props;
  return (
    <TextField
      value={value}
      styles={{ fieldGroup: className }}
      placeholder={placeholder}
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
  const { classNames, strings, settings, setSettings, defaultAIPrompt } = props;
  const [showDefaults, setShowDefaults] = useState(false);

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  return (
    <div>
      <div className={classNames.aiSettingsIntro}>
        <Text variant="medium">{strings.AISettings.labels.description}</Text>
      </div>
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAITitleEnabled]}
        title={strings.AISettings.labels.isAITitleEnabledTitle}
        description={strings.AISettings.labels.isAITitleEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAITitleEnabled)}
        generalSettingValue={settings[SettingKey.IsAITitleEnabled]}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAICopilotEnabled]}
        title={strings.AISettings.labels.isAICopilotEnabledTitle}
        description={strings.AISettings.labels.isAICopilotEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAICopilotEnabled)}
        generalSettingValue={settings[SettingKey.IsAICopilotEnabled]}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAISearchForUserEnabled]}
        title={strings.AISettings.labels.isAISearchForUserEnabledTitle}
        description={strings.AISettings.labels.isAISearchForUserEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAISearchForUserEnabled)}
        generalSettingValue={settings[SettingKey.IsAISearchForUserEnabled]}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAIRunExplanationEnabled]}
        title={strings.AISettings.labels.isAIRunExplanationEnabledTitle}
        description={strings.AISettings.labels.isAIRunExplanationEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAIRunExplanationEnabled)}
        generalSettingValue={settings[SettingKey.IsAIRunExplanationEnabled]}
      />
      <div className={classNames.aiSettingsInstructionsSection}>
        <Text variant="mediumPlus" className={classNames.aiSettingsSectionTitle}>{strings.AISettings.labels.copilotInstructionsPromptTitle}</Text>
        <Text variant="small" block className={classNames.aiSettingsSectionDescription}>{strings.AISettings.labels.copilotInstructionsPromptDescription}</Text>
        <Text variant="small" block className={classNames.aiSettingsLeaveEmptyNote}>
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
          onChange={(_, newValue) => handleSettingChange(SettingKey.CopilotInstructions)(newValue ?? '')}
        />
      </div>
      <div className={classNames.aiSettingsSliderSection}>
        <Text variant="mediumPlus" className={classNames.aiSettingsSectionTitle}>{strings.AISettings.labels.copilotTemperatureTitle}</Text>
        <Text variant="small" block className={classNames.aiSettingsSectionDescription}>{strings.AISettings.labels.copilotTemperatureDescription}</Text>
        <Slider
          min={0}
          max={1}
          step={0.05}
          value={parseFloat(settings[SettingKey.CopilotTemperature]) || 0.7}
          showValue
          onChange={(value) => handleSettingChange(SettingKey.CopilotTemperature)(value.toString())}
        />
      </div>
      <div className={classNames.aiSettingsSliderSection}>
        <Text variant="mediumPlus" className={classNames.aiSettingsSectionTitle}>{strings.AISettings.labels.copilotTopPTitle}</Text>
        <Text variant="small" block className={classNames.aiSettingsSectionDescription}>{strings.AISettings.labels.copilotTopPDescription}</Text>
        <Slider
          min={0}
          max={1}
          step={0.05}
          value={parseFloat(settings[SettingKey.CopilotTopP]) || 0.9}
          showValue
          onChange={(value) => handleSettingChange(SettingKey.CopilotTopP)(value.toString())}
        />
      </div>
      <SuggestedPromptsEditor strings={strings} settings={settings} setSettings={setSettings} />
    </div>
  );
}

const DEFAULT_SUGGESTED_PROMPTS = [
  { label: 'Include all reports who roll up to an employee', prompt: 'Include all reports who roll up to an employee' },
  { label: 'Include members of a group', prompt: 'Include all members of a specific group' },
];

const SuggestedPromptsEditor: React.FunctionComponent<{
  strings: AdminConfigViewProps['strings'];
  settings: { readonly [key in SettingKey]: string };
  setSettings: React.Dispatch<React.SetStateAction<{ readonly [key in SettingKey]: string }>>;
}> = ({ strings, settings, setSettings }) => {

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
    <div style={{ marginTop: '20px' }}>
      <Text variant="mediumPlus" style={{ fontWeight: 600 }}>{strings.AISettings.labels.suggestedPromptsTitle}</Text>
      <Text variant="small" block style={{ marginBottom: '12px' }}>{strings.AISettings.labels.suggestedPromptsDescription}</Text>
      {prompts.map((p) => (
        <div key={p.id} style={{ display: 'flex', gap: '8px', alignItems: 'flex-start', marginBottom: '8px' }}>
          <TextField
            style={{ flex: 1 }}
            placeholder={strings.AISettings.labels.suggestedPromptLabelPlaceholder}
            value={p.label}
            onChange={(_, val) => handleFieldChange(p.id, 'label', val ?? '')}
          />
          <TextField
            style={{ flex: 2 }}
            placeholder={strings.AISettings.labels.suggestedPromptPromptPlaceholder}
            value={p.prompt}
            onChange={(_, val) => handleFieldChange(p.id, 'prompt', val ?? '')}
          />
          <IconButton
            iconProps={{ iconName: 'Delete' }}
            title="Remove"
            onClick={() => handleRemove(p.id)}
            styles={{ root: { marginTop: '2px' } }}
          />
        </div>
      ))}
      <div style={{ display: 'flex', gap: '8px', marginTop: '8px' }}>
        <ActionButton
          iconProps={{ iconName: 'Add' }}
          text={strings.AISettings.labels.suggestedPromptAdd}
          onClick={handleAdd}
        />
        <ActionButton
          iconProps={{ iconName: 'Refresh' }}
          text={strings.AISettings.labels.suggestedPromptPopulateDefaults}
          onClick={handlePopulateDefaults}
        />
      </div>
    </div>
  );
};
