// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { useState, useEffect } from 'react';
import { ActionButton, MessageBar, MessageBarType, Stack, TextField } from '@fluentui/react';
import { IProcessedStyleSet, classNamesFunction, useTheme } from '@fluentui/react';
import { ICreateGroupProps, ICreateGroupStyleProps, ICreateGroupStyles } from './CreateGroup.types';
import { useStrings } from '../../store/hooks';
import { useDispatch, useSelector } from 'react-redux';
import { manageMembershipCreatedGroupId, manageMembershipCreateGroupErrorMessage, manageMembershipCreateGroupLoading, setCreateGroupErrorMessage } from '../../store/manageMembership.slice';
import { AppDispatch } from '../../store';
import { GroupSetting } from '../GroupSetting';

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

  const isCreateGroupLoading = useSelector(manageMembershipCreateGroupLoading);
  const errorMessage = useSelector(manageMembershipCreateGroupErrorMessage);
  const newGroupId = useSelector(manageMembershipCreatedGroupId)

  const sanitizeAlias = (name: string): string => {
    return name.replace(/[^a-zA-Z0-9\.\-]/g, '');
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
    onGroupCreated(groupName, groupAlias);
  };

  return (
    <div className={classNames.root}>
      <Stack>
        <Stack.Item>
          <TextField
            label={strings.ManageMembership.CreateGroup.groupName}
            placeholder={strings.ManageMembership.CreateGroup.groupNamePlaceholder}
            styles={{ fieldGroup: classNames.textField }}
            required
            value={groupName}
            onChange={handleGroupNameChange}
          />
          <TextField
            label={strings.ManageMembership.CreateGroup.groupAlias}
            placeholder={strings.ManageMembership.CreateGroup.groupAliasPlaceholder}
            styles={{ fieldGroup: classNames.textField }}
            required
            value={groupAlias}
            onChange={handleGroupAliasChange}
          />
          <GroupSetting />
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
