// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
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
import { GroupMembershipSourcePart } from '../../models/GroupMembershipSourcePart';
import { IsGroupMembershipSourcePartQuery, SourcePartType } from '../../models/ISourcePart';
import { selectedGroups } from '../../store/groupPart.slice';

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
  const selectedGroupsPersona = useSelector(selectedGroups);
  const searchResults = useSelector(manageMembershipSearchResults);

  const selectedGroupPersona = selectedGroupsPersona?.find((group) => group.id === groupId);

  const groupPersona: IPersonaProps = {
    id: groupId,
    text: selectedGroupPersona?.text,
  };

  const [selectedGroup, setSelectedGroup] = useState<IPersonaProps[]>(groupId && groupPersona.text ? [groupPersona] : []);

  useEffect(() => {
    const initializeSelectedGroup = async () => {
      if (IsGroupMembershipSourcePartQuery(part.query) && part.query.source) {
        dispatch(searchDestinations(part.query.source));
      }
    };

    initializeSelectedGroup();
  }, [part.query.source]);

  useEffect(() => {
    if (selectedGroupPersona && selectedGroupPersona.id === groupId) {
      setSelectedGroup([selectedGroupPersona]);
    }
  }, [selectedGroupPersona, groupId]);


  const handleGroupSearchInputChanged = (input: string): string => {
    dispatch(searchDestinations(input));
    return input;
  };

  const getPickerSuggestions = async (
    text: string,
    currentGroups: IPersonaProps[] | undefined
  ): Promise<IPersonaProps[]> => {
    return text && searchResults ? searchResults : [];
  };

  const handleGroupPickerChange = (items?: IPersonaProps[]): void => {
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
  };

  return (
    <div>
      <Label required>{strings.ManageMembership.labels.searchGroupName}</Label>
      <NormalPeoplePicker
        onResolveSuggestions={getPickerSuggestions}
        pickerSuggestionsProps={{
          suggestionsHeaderText: strings.ManageMembership.labels.searchGroupSuggestedText,
          noResultsFoundText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.noResultsFoundText,
          loadingText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.loadingText,
        }}
        key={'normal'}
        selectionAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.selectionAriaLabel}
        removeButtonAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.removeButtonAriaLabel}
        resolveDelay={300}
        itemLimit={1}
        onInputChange={handleGroupSearchInputChanged}
        onChange={handleGroupPickerChange}
        selectedItems={selectedGroup}
        styles={{ text: classNames.peoplePicker }}
        pickerCalloutProps={{ calloutMinWidth: 500 }}
      />
    </div>
  );
};