// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { classNamesFunction, IProcessedStyleSet, Pivot, PivotItem, PrimaryButton } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { AdminConfigStyleProps, AdminConfigStyles, AdminConfigViewProps } from './AdminConfig.types';
import { PageSection } from '../../components/PageSection';
import { HyperlinkSetting } from '../../components/HyperlinkSetting';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { SettingName } from '../../models';

const getClassNames = classNamesFunction<AdminConfigStyleProps, AdminConfigStyles>();

export const AdminConfigView: React.FunctionComponent<AdminConfigViewProps> = (props: AdminConfigViewProps) => {
  // extract props
  const { className, isSaving, onSave, settings, strings, styles } = props;

  // generate class names
  const classNames: IProcessedStyleSet<AdminConfigStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  
  // setup the ui state
  const [newSettings, setNewSettings] = useState(settings);
  const [validations, setValidations] = useState<{ readonly [key in SettingName]: boolean }>({
    [SettingName.DashboardUrl]: true,
    [SettingName.OutlookWarningUrl]: true,
  });

  // setup ui event handler
  const handleSettingChange = (settingName: SettingName) => (newValue: string) => {
    setNewSettings((settings) => ({ ...settings, [settingName]: newValue }));
  };

  const handleSettingValidation = (settingName: SettingName) => (isValid: boolean) => {
    setValidations((validations) => ({ ...validations, [settingName]: isValid }));
  };

  const handleOnSaveButtonClick = () => {
    onSave(newSettings);
  };

  // create state helpers
  const hasValidationErrors = () => Object.values(validations).some((value) => value !== true);
  const hasChanges = () => Object.entries(newSettings).some(([key, value]) => value !== settings[key as SettingName]);

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
              <PivotItem
                headerText={strings.labels.hyperlinks}
                headerButtonProps={{
                  'data-order': 1,
                  'data-title': strings.labels.hyperlinks,
                }}
              >
                <div className={classNames.description}>{strings.labels.description}</div>
                <div className={classNames.tiles}>
                  <HyperlinkSetting
                    title={strings.hyperlinkContainer.dashboardTitle}
                    description={strings.hyperlinkContainer.dashboardDescription}
                    link={newSettings[SettingName.DashboardUrl]}
                    required={false}
                    onLinkChange={handleSettingChange(SettingName.DashboardUrl)}
                    onValidation={handleSettingValidation(SettingName.DashboardUrl)}
                  ></HyperlinkSetting>
                  <HyperlinkSetting
                    title={strings.hyperlinkContainer.outlookWarningTitle}
                    description={strings.hyperlinkContainer.outlookWarningDescription}
                    link={newSettings[SettingName.OutlookWarningUrl]}
                    required={false}
                    onLinkChange={handleSettingChange(SettingName.OutlookWarningUrl)}
                    onValidation={handleSettingValidation(SettingName.OutlookWarningUrl)}
                  ></HyperlinkSetting>
                </div>
              </PivotItem>
            </Pivot>
          </PageSection>
        </div>
        <div className={classNames.bottomContainer}>
          <PrimaryButton
            text={strings.labels.saveButton}
            onClick={handleOnSaveButtonClick}
            disabled={!hasChanges() || hasValidationErrors() || isSaving}
          ></PrimaryButton>
        </div>
      </div>
    </Page>
  );
};
