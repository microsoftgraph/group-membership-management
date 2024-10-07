// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { classNamesFunction, Toggle, IProcessedStyleSet, Pivot, PivotItem, PrimaryButton, TextField, Text, IColumn, SelectionMode, ShimmeredDetailsList, Dropdown, Spinner, IRenderFunction, ISelectableDroppableTextProps, IDropdown } from '@fluentui/react';
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
  GeneralSettingsProps } from './AdminConfig.types';
import { PageSection } from '../../components/PageSection';
import { HyperlinkSetting } from '../../components/HyperlinkSetting';
import { Operation } from '../../components/Operation';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { SettingKey, SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import { GeneralSetting } from '../../components/GeneralSetting';

const getClassNames = classNamesFunction<AdminConfigStyleProps, AdminConfigStyles>();

export const AdminConfigView: React.FunctionComponent<AdminConfigViewProps> = (props: AdminConfigViewProps) => {
  // extract props
  const { className, isSaving, onSave, handleGetValues, settings, sqlMembershipSource, sqlMembershipSourceAttributes, strings, styles, 
    isHyperlinkAdmin, 
    isCustomMembershipProviderAdmin,
    isOperationsResetAdministrator,
    isGeneralSettingsAdministrator } = props;

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
        title={strings.GeneralSettings.labels.reviewOwnSubmissionTitle}
        description={strings.GeneralSettings.labels.reviewOwnSubmissionDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.CanReviewOwnSubmissions)}
        generalSettingValue={settings[SettingKey.CanReviewOwnSubmissions]}
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
          customLabel: (attributeMap && attributeMap[name]?.customLabel) || currentItem.customLabel
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

  const onRenderList: IRenderFunction<ISelectableDroppableTextProps<IDropdown, HTMLDivElement>> = (props, defaultRender) => {

    const isLoading = props?.options?.length === 0;

    return (
      <div >
        {isLoading ? (
                <Spinner styles={{ root: classNames.valuesDropdownSpinner }} label={strings.CustomSourceSettings.labels.valuesDropdownSpinnerLabel} />
            ) : (
                defaultRender!(props)
            )}
      </div>
    );
};

  return (
    <Dropdown
      placeholder={strings.CustomSourceSettings.labels.valuesDropdownPlaceholder}
      onRenderList={onRenderList}
      onClick={onDropdownClick}
      options={getDropdownOptions(values)}
      dropdownWidth={'auto'}
      styles={{ dropdown: classNames.valuesDropdown, title: classNames.valuesDropdownTitle }}
    />
  );
});