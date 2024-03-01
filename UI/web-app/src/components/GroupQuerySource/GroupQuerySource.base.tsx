// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
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
import { IsGroupMembershipSourcePartQuery } from '../../models/GroupMembershipSourcePart';
import { useSelectedGroupById } from '../../store/groupPart.slice';
import { searchGroups } from '../../store/groups.api';

export const getClassNames = classNamesFunction<GroupQuerySourceStyleProps, GroupQuerySourceStyles>();

export const GroupQuerySourceBase: React.FunctionComponent<GroupQuerySourceProps> = (props: GroupQuerySourceProps) => {
  const { className, styles, part, onSourceChange } = props;
  const classNames: IProcessedStyleSet<GroupQuerySourceStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();
  const dispatch = useDispatch<AppDispatch>();

  const groupId: string = IsGroupMembershipSourcePartQuery(part.query) ? part.query.source : '';
  const [localSearchResults, setLocalSearchResults] = useState<IPersonaProps[]>([]);
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
    if (!input) {
      setLocalSearchResults([]);
      return '';
    }
    dispatch(searchGroups(input)).then(results => {
      setLocalSearchResults(results.payload as IPersonaProps[]);
    }).catch(error => {
      console.error("Error fetching search destinations", error);
      setLocalSearchResults([]);
    });
    return input;
  }, [dispatch]);

  const getPickerSuggestions = useCallback(async (
    text: string,
    currentGroups: IPersonaProps[] | undefined
  ): Promise<IPersonaProps[]> => {
    return text && localSearchResults ? localSearchResults : [];
  }, [localSearchResults]);

  const handleGroupPickerChange = useCallback((items?: IPersonaProps[]): void => {
    if (items && items.length > 0) {
      setSelectedGroup(items);
      onSourceChange(items[0].id ?? '');
    } else {
      setSelectedGroup([]);
      onSourceChange('');
    }
  }, [onSourceChange]);

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
        aria-label={strings.Components.GroupQuerySource.searchGroupName}
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