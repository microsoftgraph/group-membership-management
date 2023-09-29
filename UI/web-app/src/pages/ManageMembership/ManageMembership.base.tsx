// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { IProcessedStyleSet, PrimaryButton, classNamesFunction, useTheme, Dropdown, IDropdownOption, ActionButton, MessageBar, MessageBarType, MessageBarButton, Icon, DropdownMenuItemType, ComboBox, IComboBoxOption } from '@fluentui/react';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { PageSection } from '../../components/PageSection';
import { IManageMembershipProps, IManageMembershipStyleProps, IManageMembershipStyles } from './ManageMembership.types';
import { useTranslation } from 'react-i18next';
import { HelpPanel } from '../../components/HelpPanel';
import { IGroup } from '../../interfaces/IGroup.interfaces';

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

  const groupA: IGroup = { Id: '21830b1f-100f-40ee-a34b-fd3fa738bc76', Alias: 'abril@microsoft.com', Name: 'Abrils Group' };

  const optionsDestinationType: IDropdownOption[] = [
    { key: 'Group', text: 'Group' },
    { key: 'Channel', text: 'Channel' }
  ];
  const optionsSearchDestination: IComboBoxOption[] = [
    { key: 'GroupP', text: `${groupA.Name} Alias: ${groupA.Alias} ID: ${groupA.Id}}`, itemType: DropdownMenuItemType.Divider },
  ];

  const [selectedSearchDestination, setSelectedSearchDestination] = useState<string | undefined>(undefined);
  const [isPanelOpen, setIsPanelOpen] = useState(false);

  const togglePanel = () => {
    setIsPanelOpen(!isPanelOpen);
  };

  const needHelpButton: React.ReactNode = (
    <ActionButton iconProps={{ iconName: 'help' }} text={t('needHelp') as string} onClick={togglePanel}></ActionButton>
  );


  const onSearchDestinationChange = (event: React.FormEvent<HTMLDivElement>, option?: IDropdownOption) => {
    if (option) {
      setSelectedSearchDestination(option.text);
    } else {
      setSelectedSearchDestination(undefined);
    }
  };

  const SharePointDomain: string = `${process.env.REACT_APP_SHAREPOINTDOMAIN}`;
  const domainName: string = `${process.env.REACT_APP_DOMAINNAME}`;
  const groupName: string | undefined = selectedSearchDestination?.replace(/\s/g, '');

  const openOutlookLink = (): void => {
    const url = `https://outlook.office.com/mail/group/${domainName}/${groupName}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const openSharePointLink = (): void => {
    const url = `https://${SharePointDomain}/sites/${groupName}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const openYammerLink = (): void => {
    const domainName: string = `${process.env.REACT_APP_DOMAINNAME}`
    const url = `https://www.yammer.com/${domainName}/groups/${groupName}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const onClickOutlookWarning = (): void => {
    const url = `https://msn.com`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  return (
    <Page>
      <PageHeader rightButton={needHelpButton} />
      <HelpPanel togglePanel={togglePanel} isPanelOpen={isPanelOpen}></HelpPanel>
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
            <div>
              <Dropdown
                placeholder="Select an option"
                label={t('ManageMembership.labels.selectDestinationType') as string}
                options={optionsDestinationType}
                styles={{ title: classNames.dropdownTitle, dropdown: classNames.dropdownField }}
                required
              />
              <div>
                {t('ManageMembership.labels.searchDestination') as string}
                <ComboBox
                  placeholder="Search for a group"
                  options={optionsSearchDestination}
                  // styles={{ title: classNames.dropdownTitle, dropdown: classNames.dropdownField }}
                  required
                  // onChange={onSearchDestinationChange}
                />
              </div>
              Selected Destination: {selectedSearchDestination}
              {selectedSearchDestination && (
                <div>
                  {t('ManageMembership.labels.appsUsed')}
                  {selectedSearchDestination ? (
                    <div className={classNames.endpointsContainer}>
                      {/* {selectedSearchDestination?.endpoints?.includes("Outlook") && ( */}
                      <div className={classNames.outlookContainer}>
                        <ActionButton
                          iconProps={{ iconName: 'OutlookLogo' }}
                          onClick={() => openOutlookLink()}
                        >
                          Outlook
                        </ActionButton>
                        <MessageBar
                          messageBarType={MessageBarType.warning}
                          className={classNames.outlookWarning}
                          isMultiline={false}
                          actions={
                            <MessageBarButton onClick={() => onClickOutlookWarning()}>{t('learnMore')}</MessageBarButton>
                          }>
                          {t('ManageMembership.labels.outlookWarning')}
                        </MessageBar>
                      </div>
                      {/* )} */}
                      {/* {selectedSearchDestination?.endpoints?.includes("SharePoint") && ( */}
                      <ActionButton
                        iconProps={{ iconName: 'SharePointLogo' }}
                        onClick={() => openSharePointLink()}
                      >
                        SharePoint
                      </ActionButton>
                      {/* )} */}
                      {/* {selectedSearchDestination?.endpoints?.includes("Yammer") && ( */}
                      <ActionButton
                        iconProps={{ iconName: 'YammerLogo' }}
                        onClick={() => openYammerLink()}
                      >
                        Yammer
                      </ActionButton>
                      {/* )} */}
                    </div>
                  ) : null}
                </div>
              )}
            </div>
          </PageSection>
        </div>
        <div className={classNames.bottomContainer}>
          <div className={classNames.circlesContainer}>
            <Icon iconName='CircleFill' className={classNames.circleIcon}></Icon>
            <Icon iconName='CircleRing' className={classNames.circleIcon}></Icon>
            <Icon iconName='CircleRing' className={classNames.circleIcon}></Icon>
            <Icon iconName='CircleRing' className={classNames.circleIcon}></Icon>
          </div>
          <div className={classNames.nextButtonContainer}>
            <PrimaryButton text={t('next') as string} onClick={() => onClick()} disabled></PrimaryButton>
          </div>
        </div>
      </div >
    </Page>
  )
};
