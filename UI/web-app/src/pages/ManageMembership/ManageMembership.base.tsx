// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { IProcessedStyleSet, PrimaryButton, classNamesFunction, useTheme, Stack, Dropdown, IDropdownOption, IStackTokens, IDropdownStyles, ActionButton } from '@fluentui/react';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { PageSection } from '../../components/PageSection';
import { IManageMembershipProps, IManageMembershipStyleProps, IManageMembershipStyles } from './ManageMembership.types';
import { useTranslation } from 'react-i18next';

const getClassNames = classNamesFunction<
  IManageMembershipStyleProps,
  IManageMembershipStyles
>();

export const ManageMembershipBase: React.FunctionComponent<IManageMembershipProps> = (
  props: IManageMembershipProps
) => {
  const { className, styles } = props;

  const classNames: IProcessedStyleSet<IManageMembershipStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const { t } = useTranslation();

  const onClick = () => {
    window.alert('Hi Paul! 👁️👄👁️');
  };
  
  const optionsDestinationType: IDropdownOption[] = [
    { key: 'Group', text: 'Group' },
    { key: 'Channel', text: 'Channel' }
  ];
  const optionsSearchDestination: IDropdownOption[] = [
    { key: 'GroupP', text: 'Pauls Group\n Alias: paul@microsoft.com\n ID: 3fef58bd-4fbc-40d7-bf38-766e7c3b653f' },
    { key: 'GroupA', text: 'Abrils Group\n Alias: abril@microsoft.com\n ID: 21830b1f-100f-40ee-a34b-fd3fa738bc76' }
  ];
  const stackTokens: IStackTokens = { childrenGap: 20 };
  const dropdownStyles: Partial<IDropdownStyles> = {
    dropdown: { width: 300 },
  };

  const needHelpButton: React.ReactNode = (
    <ActionButton iconProps={{iconName: 'help'}} text={t('ManageMembership.labels.needHelp') as string} onClick={() => onClick()}></ActionButton>
  );

  const [selectedSearchDestination, setSelectedSearchDestination] = useState<string | undefined>(undefined);

  const onSearchDestinationChange = (event: React.FormEvent<HTMLDivElement>, option?: IDropdownOption) => {
    if (option) {
      setSelectedSearchDestination(option.text);
    } else {
      setSelectedSearchDestination(undefined);
    }
  };

  return (
    <Page>
      <PageHeader rightButton={needHelpButton} />
      <div className={classNames.root}>
        <div className={classNames.card}>
          <PageSection>
            <div className={classNames.title}>{t('ManageMembership.labels.pageTitle')}</div>
            <div className={classNames.stepTitle}>{t('ManageMembership.labels.step1title')}</div>
            <div className={classNames.stepDescription}>{t('ManageMembership.labels.step1description')}</div>
          </PageSection>
        </div>
        <div className={classNames.card}>
          <PageSection>
            <Stack tokens={stackTokens}>
              <Dropdown
                placeholder="Select an option"
                label={t('ManageMembership.labels.selectDestinationType') as string}
                options={optionsDestinationType}
                styles={dropdownStyles}
                required
              />
              <Dropdown
                placeholder="Select options"
                label={t('ManageMembership.labels.searchDestination') as string}
                options={optionsSearchDestination}
                styles={dropdownStyles}
                required
                onChange={onSearchDestinationChange}
              />
              {selectedSearchDestination && (
                <div>
                  Selected Destination: {selectedSearchDestination}. Endpoints to come soon.
                </div>
              )}
            </Stack>
          </PageSection>
        </div>
        <div className={classNames.bottomContainer}>
          <PrimaryButton text={t('next') as string} onClick={() => onClick()} disabled></PrimaryButton>
        </div>
      </div >
    </Page>
  )
};
