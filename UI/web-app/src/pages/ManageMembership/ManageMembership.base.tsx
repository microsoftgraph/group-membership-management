// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { IProcessedStyleSet, PrimaryButton, classNamesFunction, useTheme, Dropdown, IDropdownOption, ActionButton, MessageBar, MessageBarType, MessageBarButton, Icon, DropdownMenuItemType, ComboBox, IComboBoxOption, DefaultButton, IComboBox, Spinner, Dialog, DialogType, DialogFooter } from '@fluentui/react';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { PageSection } from '../../components/PageSection';
import { IManageMembershipProps, IManageMembershipStyleProps, IManageMembershipStyles } from './ManageMembership.types';
import { useTranslation } from 'react-i18next';
import { HelpPanel } from '../../components/HelpPanel';
import { searchGroups } from '../../store/groups.api';
import { selectGroups } from '../../store/groups.slice';
import { isAppIDOwnerOfGroup, getGroupEndpoints } from '../../store/groups.api';
import { useNavigate } from 'react-router-dom';
import { AppDispatch } from '../../store';

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
  const navigate = useNavigate();

  const onClick = () => {
    window.alert('To be configured later');
  };

  const optionsDestinationType: IDropdownOption[] = [
    { key: 'Group', text: 'Group' },
    { key: 'Channel', text: 'Channel' }
  ];

  const dispatch = useDispatch<AppDispatch>();
  const [selectedSearchDestination, setSelectedSearchDestination] = useState<string | undefined>(undefined);
  const [groupEndpoints, setGroupEndpoints] = useState<string[]>([]);
  const [isPanelOpen, setIsPanelOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [isOwner, setIsOwner] = useState<boolean | null>(null);
  const [showDialog, setShowDialog] = useState(false);
  const groupsState = useSelector(selectGroups);

  const togglePanel = () => {
    setIsPanelOpen(!isPanelOpen);
  };

  const onBackButtonClick = () => {
    setShowDialog(true);
  };

  const handleDialogClose = () => {
    setShowDialog(false);
  };

  const handleConfirmExit = () => {
    navigate(-1);
    setShowDialog(false);
  };

  const needHelpButton: React.ReactNode = (
    <ActionButton iconProps={{ iconName: 'help' }} text={t('needHelp') as string} onClick={togglePanel}></ActionButton>
  );

  const debounce = (func: (...args: any[]) => void, delay: number) => {
    let timeout: NodeJS.Timeout;
    return (...args: any[]) => {
      clearTimeout(timeout);
      timeout = setTimeout(() => func(...args), delay);
    };
  };

  const handleSearch = debounce(() => {
    if (searchQuery.length > 3) {
      dispatch(searchGroups(searchQuery));
    }
  }, 200);

  const checkOwnership = async (groupId: string) => {
    try {
      const action = await dispatch(isAppIDOwnerOfGroup(groupId));
      if (isAppIDOwnerOfGroup.fulfilled.match(action)) {
        const ownershipStatus: boolean = action.payload;
        setIsOwner(ownershipStatus);
        console.log("group id to check ownership of: " + groupId, "ownershipStatus: ", ownershipStatus);

        if (ownershipStatus) {
          console.log('Service principal is the owner of the group: ', groupId);
        } else {
          console.log('Service principal is not the owner of the group: ', groupId);
        }
      }
    } catch (error) {
      setIsOwner(null);
      console.error('Error checking ownership:', error);
    }
  };

  const onSearchDestinationChange = async (event: React.FormEvent<IComboBox>, option?: IComboBoxOption) => {
    if (option) {
      const selectedGroupId = option.key as string;
      setSelectedSearchDestination(option.text);

      checkOwnership(selectedGroupId);

      try {
        const action = await dispatch(getGroupEndpoints(selectedGroupId));

        if (getGroupEndpoints.fulfilled.match(action)) {
          setGroupEndpoints(action.payload);
        }
      } catch (error) {
        console.error('Error fetching group endpoints:', error);
      }
    }
  };

  const hasRequiredEndpoints = () => {
    if (!groupEndpoints) return false;
    return ["Outlook", "Yammer", "SharePoint"].some(endpoint => groupEndpoints.includes(endpoint));
};


  const ownershipWarning = isOwner === false ? (
    <div className={classNames.ownershipWarning}>
      Warning: GMM is not the owner of this group! It will not be able to manage membership for this group until you add it <a href="">here</a>.
    </div>
  ) : null;

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
      <PageHeader rightButton={needHelpButton} onBackButtonClick={onBackButtonClick} />
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
                  styles={{
                    root: classNames.searchField, container: classNames.comboBoxContainer,
                    input: classNames.comboBoxInput
                  }}
                  placeholder="Search for a group"
                  useComboBoxAsMenuWidth
                  options={groupsState?.searchResults?.map(group => ({ key: group.id, text: group.name })) || []}
                  required
                  allowFreeform
                  onChange={onSearchDestinationChange}
                  onPendingValueChanged={(option?: IComboBoxOption, index?: number, value?: string) => {
                    if (value !== undefined) {
                      setSearchQuery(value);
                      handleSearch();
                    }
                  }}
                />
              </div>
              {groupsState.loading ? (
                <Spinner />
              ) : (
                <div>
                  {selectedSearchDestination && hasRequiredEndpoints() ? (
                    <div className={classNames.endpointsContainer}>
                      {groupEndpoints ?? t('ManageMembership.labels.appsUsed')}
                      {groupEndpoints.includes("Outlook") && (
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
                        </div>)}
                      {groupEndpoints.includes("SharePoint") && (
                        <ActionButton
                          iconProps={{ iconName: 'SharePointLogo' }}
                          onClick={() => openSharePointLink()}
                        >
                          SharePoint
                        </ActionButton>
                      )}
                      {groupEndpoints.includes("Yammer") && (
                        <ActionButton
                          iconProps={{ iconName: 'YammerLogo' }}
                          onClick={() => openYammerLink()}
                        >
                          Yammer
                        </ActionButton>
                      )}
                      {ownershipWarning}
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
      <Dialog
        hidden={!showDialog}
        onDismiss={handleDialogClose}
        dialogContentProps={{
          type: DialogType.normal,
          title: 'Abandon Onboarding?',
          subText: 'Are you sure you want to abandon the in-progress onboarding and go back?',
        }}
        modalProps={{
          isBlocking: true,
          styles: { main: { maxWidth: 450 } },
        }}
      >
        <DialogFooter>
          <PrimaryButton onClick={handleConfirmExit} text="Yes, go back" />
          <DefaultButton onClick={handleDialogClose} text="Cancel" />
        </DialogFooter>
      </Dialog>
    </Page>
  )
};
