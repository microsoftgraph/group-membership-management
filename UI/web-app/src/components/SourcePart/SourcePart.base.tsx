// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import {
  classNamesFunction,
  ChoiceGroup,
  IChoiceGroupOption,
  Dropdown,
  IDropdownOption,
  IProcessedStyleSet,
} from '@fluentui/react';
import { DefaultButton, IconButton } from '@fluentui/react/lib/Button';
import { useTheme } from '@fluentui/react/lib/Theme';
import { SourcePartStyleProps, SourcePartStyles, SourcePartProps } from './SourcePart.types';
import { AppDispatch } from '../../store';
import { updateSourcePart, updateSourcePartType } from '../../store/manageMembership.slice';
import { useStrings } from '../../store/hooks';
import { ISourcePart, SourcePartQuery, SourcePartType } from '../../models/ISourcePart';
import { HRQuerySource } from '../HRQuerySource';
import { HRSourcePart, HRSourcePartSource } from '../../models/HRSourcePart';
import { GroupQuerySource } from '../GroupQuerySource';

const getClassNames = classNamesFunction<SourcePartStyleProps, SourcePartStyles>();

export const SourcePartBase: React.FunctionComponent<SourcePartProps> = (props: SourcePartProps) => {
  const { className, styles, index, totalSourceParts, onDelete, query, part } = props;
  const classNames: IProcessedStyleSet<SourcePartStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const toggleExpand = () => {
    setExpanded(!expanded);
  };

  const [hrSourcePartSource, setHRSourcePartSource] = useState<HRSourcePartSource>(query.source as HRSourcePartSource);

  const options: IChoiceGroupOption[] = [
    { key: 'Yes', text: strings.yes },
    { key: 'No', text: strings.no },
  ];

  const sourceTypeOptions: IDropdownOption[] = [
    { key: SourcePartType.HR, text: strings.ManageMembership.labels.HR },
    { key: SourcePartType.GroupMembership, text: strings.ManageMembership.labels.groupMembership},
    { key: SourcePartType.GroupOwnership, text: strings.ManageMembership.labels.groupOwnership, disabled: true}
  ];

  const dispatch = useDispatch<AppDispatch>();
  const [expanded, setExpanded] = useState(false);
  const [isExclusionary, setIsExclusionary] = useState(query.exclusionary);
  const [errorMessage, setErrorMessage] = useState<string>('');

  const handleSourceTypeChanged = (event: React.FormEvent<HTMLDivElement>, item: IDropdownOption | undefined): void => {
    if (item) {
      dispatch(updateSourcePartType({ partId: index, type: item.key as SourcePartType }));
    }
  }

  const handleDelete = () => {
    if (totalSourceParts > 1) {
      onDelete(index);
    } else {
      setErrorMessage(strings.ManageMembership.labels.deleteLastSourcePartWarning);
    }
  }

  useEffect(() => {
    setErrorMessage('');
  }, [query, expanded]);

  useEffect(() => {
    setIsExclusionary(query.exclusionary);
  }, [query.exclusionary]);

  useEffect(() => {
    setHRSourcePartSource(query.source as HRSourcePartSource);
  }, [query]);

  const handleSourceChange = (source: HRSourcePartSource, partId: number) => {
    const newQuery: HRSourcePart = {
      type: SourcePartType.HR,
      source: source,
      exclusionary: isExclusionary
    }
    const newPart: ISourcePart = {
      id: partId,
      query: newQuery,
      isValid: true
    };
    dispatch(updateSourcePart(newPart));
  };

  const handleExclusionaryChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption): void => {
    if (!option) return;
    const isExclusionarySelected = option.key === 'Yes';

    setIsExclusionary(isExclusionarySelected);
    try {
      const updatedQuery: SourcePartQuery = {
        ...query,
        exclusionary: isExclusionarySelected
      };

      const updatedSourcePart: ISourcePart = {
        id: index,
        query: updatedQuery,
        isValid: true
      };

      dispatch(updateSourcePart(updatedSourcePart));
    } catch (error) {
      console.error(`Error updating source part query:`, error);
    }
  };

  return (
    <div className={classNames.card}>
      <div className={classNames.header}>
        <div className={classNames.title}>
          {strings.ManageMembership.labels.sourcePart} {index}
        </div>
        <IconButton
          className={classNames.expandButton}
          iconProps={{ iconName: expanded ? 'ChevronUp' : 'ChevronDown' }}
          onClick={toggleExpand}
          title={strings.ManageMembership.labels.expandCollapse}
        />
      </div>
      {expanded &&
        <div className={classNames.content}>
          <div className={classNames.controls}>
            <Dropdown
              styles={{ title: classNames.dropdownTitle }}
              options={sourceTypeOptions}
              label="Source Type"
              required={true}
              selectedKey={part.query.type}
              onChange={handleSourceTypeChanged}
            />
            <ChoiceGroup
              className={classNames.exclusionaryPart}
              options={options}
              label="Exclude source part"
              required={true}
              onChange={handleExclusionaryChange}
              selectedKey={isExclusionary ? 'Yes' : 'No'}
            />
            <DefaultButton iconProps={{ iconName: 'Delete' }} className={classNames.deleteButton} onClick={handleDelete} >
              {strings.delete}
            </DefaultButton>
          </div>

          {part.query.type === SourcePartType.HR && (
            <div className={classNames.advancedQuery}>
            <HRQuerySource source={hrSourcePartSource} partId={index} onSourceChange={handleSourceChange}/>
          </div>
          )}
          {part.query.type === SourcePartType.GroupMembership && (
              <GroupQuerySource part={part}/>
          )}
          <div className={classNames.error}>
            {errorMessage}
          </div>
        </div>
      }
    </div>
  );
};
