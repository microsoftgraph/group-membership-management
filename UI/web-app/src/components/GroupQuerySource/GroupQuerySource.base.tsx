// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  classNamesFunction,
  type IProcessedStyleSet,
  Label,
  NormalPeoplePicker,
  IPersonaProps
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type {
  GroupQuerySourceProps,
  GroupQuerySourceStyleProps,
  GroupQuerySourceStyles,
} from './GroupQuerySource.types';
import { useStrings } from '../../store/hooks';
import { AppDispatch } from '../../store';
import { searchDestinations } from '../../store/manageMembership.api';
import { manageMembershipSearchResults, updateSourcePart } from '../../store/manageMembership.slice';
import { GroupMembershipSourcePart, IsGroupMembershipSourcePartQuery } from '../../models/GroupMembershipSourcePart';
import { useSelectedGroupById } from '../../store/groupPart.slice';
import { SourcePartType } from '../../models/SourcePartType';

export const getClassNames = classNamesFunction<GroupQuerySourceStyleProps, GroupQuerySourceStyles>();


export const GroupQuerySourceBase: React.FunctionComponent<GroupQuerySourceProps> = (props: GroupQuerySourceProps) => {
  const { className, styles, part } = props;
  const classNames: IProcessedStyleSet<GroupQuerySourceStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();
  const dispatch = useDispatch<AppDispatch>();


  const groupId: string = IsGroupMembershipSourcePartQuery(part.query) ? part.query.source : '';
  const searchResults = useSelector(manageMembershipSearchResults);
  const selectedGroupPersona = useSelectedGroupById(groupId);

  const groupPersona: IPersonaProps = {
    id: groupId,
    text: selectedGroupPersona?.text,
  };

  const [selectedGroup, setSelectedGroup] = useState<IPersonaProps[]>(groupId && groupPersona.text ? [groupPersona] : []);

  const initializeSelectedGroup = useCallback(async () => {
    if (IsGroupMembershipSourcePartQuery(part.query) && part.query.source) {
      dispatch(searchDestinations(part.query.source));
    }
  }, [part.query.source]);

  useEffect(() => {
    initializeSelectedGroup();
  }, [part.query.source]);

  useEffect(() => {
    if (selectedGroupPersona && selectedGroupPersona.id === groupId) {
      setSelectedGroup([selectedGroupPersona]);
    }
  }, [selectedGroupPersona, groupId]);


  const handleGroupSearchInputChanged = useCallback((input: string): string => {
    dispatch(searchDestinations(input));
    return input;
  }, [dispatch]);

  const getPickerSuggestions = useCallback(async (
    text: string,
    currentGroups: IPersonaProps[] | undefined
  ): Promise<IPersonaProps[]> => {
    return text && searchResults ? searchResults : [];
  }, [searchResults]);

  const handleGroupPickerChange = useCallback((items?: IPersonaProps[]): void => {
    if (items !== undefined && items.length > 0) {
      setSelectedGroup(items);
      dispatch(updateSourcePart({
        id: part.id,
        query: ({
          type: SourcePartType.GroupMembership,
          source: items[0].id as string,
          exclusionary: part.query.exclusionary
        } as GroupMembershipSourcePart),
        isValid: true,
      }));
    }
    else {
      setSelectedGroup([]);
      dispatch(updateSourcePart({
        id: part.id,
        query: ({
          type: SourcePartType.GroupMembership,
          source: '',
          exclusionary: part.query.exclusionary
        } as GroupMembershipSourcePart),
        isValid: false
      }));
    }
  }, [dispatch, part.id, part.query.exclusionary]);

  return (
    <div>
      <Label required>{strings.Components.GroupQuerySource.searchGroupName}</Label>
      <NormalPeoplePicker
        onResolveSuggestions={getPickerSuggestions}
        pickerSuggestionsProps={{
          suggestionsHeaderText: strings.Components.GroupQuerySource.searchGroupSuggestedText,
          noResultsFoundText: strings.Components.GroupQuerySource.noResultsFoundText,
          loadingText: strings.Components.GroupQuerySource.loadingText,
        }}
        key={'normal'}
        selectionAriaLabel={strings.Components.GroupQuerySource.selectionAriaLabel}
        removeButtonAriaLabel={strings.Components.GroupQuerySource.removeButtonAriaLabel}
        resolveDelay={300}
        itemLimit={1}
        onInputChange={handleGroupSearchInputChanged}
        onChange={handleGroupPickerChange}
        selectedItems={selectedGroup}
        styles={{ text: classNames.groupPicker }}
        pickerCalloutProps={{ calloutMinWidth: 500 }}
      />
    </div>
  );
};