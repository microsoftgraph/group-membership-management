// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { useState, useEffect } from 'react';
import { ActionButton, ChoiceGroup, IChoiceGroupOption, MessageBar, MessageBarType, Stack, TextField } from '@fluentui/react';
import { IProcessedStyleSet, classNamesFunction, useTheme } from '@fluentui/react';
import { ICreateGroupProps, ICreateGroupStyleProps, ICreateGroupStyles } from './CreateGroup.types';
import { useStrings } from '../../store/hooks';
import { useDispatch, useSelector } from 'react-redux';
import { manageMembershipCreatedGroupId, manageMembershipCreateGroupErrorMessage, manageMembershipCreateGroupLoading, setCreateGroupErrorMessage, setGroupSettings } from '../../store/manageMembership.slice';
import { AppDispatch } from '../../store';
import { GroupSetting } from '../GroupSetting';
import { NewGroupType } from '../../models/NewGroupType';

const getClassNames = classNamesFunction<ICreateGroupStyleProps, ICreateGroupStyles>();

export const CreateGroupBase: React.FunctionComponent<ICreateGroupProps> = (props) => {
  const { className, styles, onGroupCreated } = props;
  const strings = useStrings();
  const dispatch = useDispatch<AppDispatch>();
  const classNames: IProcessedStyleSet<ICreateGroupStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const [groupName, setGroupName] = useState('');
  const [groupAlias, setGroupAlias] = useState('');
  const [groupAliasEdited, setGroupAliasEdited] = useState(false);
  const [groupType, setGroupType] = useState<NewGroupType>(NewGroupType.Microsoft365);

  const isSecurityGroup = groupType === NewGroupType.Security;

  const isCreateGroupLoading = useSelector(manageMembershipCreateGroupLoading);
  const errorMessage = useSelector(manageMembershipCreateGroupErrorMessage);
  const newGroupId = useSelector(manageMembershipCreatedGroupId)

  const sanitizeAlias = (name: string): string => {
    return name.replace(/[^a-zA-Z0-9\.\-]/g, '');
  };

  const groupTypeOptions: IChoiceGroupOption[] = [
    {
      key: NewGroupType.Microsoft365.toString(),
      text: strings.ManageMembership.CreateGroup.groupTypeMicrosoft365,
    },
    {
      key: NewGroupType.Security.toString(),
      text: strings.ManageMembership.CreateGroup.groupTypeSecurity,
    },
  ];

  const handleGroupTypeChange = (
    event?: React.FormEvent<HTMLElement | HTMLInputElement>,
    option?: IChoiceGroupOption
  ) => {
    if (!option) {
      return;
    }
    const newGroupType = Number(option.key) as NewGroupType;
    setGroupType(newGroupType);
    if (newGroupType === NewGroupType.Security) {
      // Clear Microsoft 365 settings so they aren't submitted for a security group.
      dispatch(setGroupSettings(undefined));
    }
    dispatch(setCreateGroupErrorMessage(''));
  };

  const handleGroupNameChange = async (
    event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>,
    newValue?: string
  ) => {
    const newGroupName = newValue || '';
    setGroupName(newGroupName);
    if (!groupAliasEdited) {
      const sanitizedAlias = sanitizeAlias(newGroupName);
      setGroupAlias(sanitizedAlias);
    }
    dispatch(setCreateGroupErrorMessage(''));
  };

  const handleGroupAliasChange = (
    event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>,
    newValue?: string
  ) => {
    setGroupAlias(newValue || '');
    setGroupAliasEdited(true);
  };

  const handleCreateGroup = async () => {
    // Microsoft Graph requires a mail nickname on every group, security groups included.
    // Security groups do not surface the alias field, so derive it from the name instead
    // of sending the value a user may have typed while Microsoft 365 was selected.
    const aliasToSubmit = isSecurityGroup ? sanitizeAlias(groupName) : groupAlias;
    onGroupCreated(groupName, aliasToSubmit, groupType);
  };

  return (
    <div className={classNames.root}>
      <Stack>
        <Stack.Item>
          <ChoiceGroup
            label={strings.ManageMembership.CreateGroup.groupType}
            options={groupTypeOptions}
            selectedKey={groupType.toString()}
            onChange={handleGroupTypeChange}
          />
          <TextField
            label={strings.ManageMembership.CreateGroup.groupName}
            placeholder={strings.ManageMembership.CreateGroup.groupNamePlaceholder}
            styles={{ fieldGroup: classNames.textField }}
            required
            value={groupName}
            onChange={handleGroupNameChange}
          />
          {!isSecurityGroup && (
            <TextField
              label={strings.ManageMembership.CreateGroup.groupAlias}
              placeholder={strings.ManageMembership.CreateGroup.groupAliasPlaceholder}
              styles={{ fieldGroup: classNames.textField }}
              required
              value={groupAlias}
              onChange={handleGroupAliasChange}
            />
          )}
          {/* Authorized senders, welcome message and Exchange visibility are Microsoft 365
              concepts that do not apply to a security group. */}
          {!isSecurityGroup && <GroupSetting />}
        </Stack.Item>
        { (
          <Stack.Item>
            <ActionButton
              iconProps={{ iconName: isCreateGroupLoading ? 'Clock' : newGroupId ? 'CheckMark' : 'Add' }}
              onClick={handleCreateGroup}
              disabled={isCreateGroupLoading}
            >
              {isCreateGroupLoading ? strings.ManageMembership.CreateGroup.creating : newGroupId ? strings.ManageMembership.CreateGroup.created : strings.ManageMembership.CreateGroup.createGroup}
            </ActionButton>
            {errorMessage && <MessageBar messageBarType={MessageBarType.error}>{errorMessage}</MessageBar>}
          </Stack.Item>
        )}
      </Stack>
    </div>
  );
};
