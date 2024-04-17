// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  classNamesFunction,
  ChoiceGroup,
  IChoiceGroupOption,
  Dropdown,
  IDropdownOption,
  IProcessedStyleSet,
} from '@fluentui/react';
import { ActionButton, DefaultButton, IconButton } from '@fluentui/react/lib/Button';
import { useTheme } from '@fluentui/react/lib/Theme';
import { SourcePartStyleProps, SourcePartStyles, SourcePartProps } from './SourcePart.types';
import { AppDispatch } from '../../store';
import { manageMembershipIsEditingExistingJob, updateSourcePart, copySourcePart, updateSourcePartType } from '../../store/manageMembership.slice';
import { useStrings } from '../../store/hooks';
import { ISourcePart } from '../../models/ISourcePart';
import { HRQuerySource } from '../HRQuerySource';
import { HRSourcePart, HRSourcePartSource } from '../../models/HRSourcePart';
import { GroupQuerySource } from '../GroupQuerySource';
import { SourcePartType } from '../../models/SourcePartType';
import { SourcePartQuery } from '../../models/SourcePartQuery';
import { AdvancedViewSourcePart } from '../AdvancedViewSourcePart';

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
    { key: SourcePartType.GroupMembership, text: strings.ManageMembership.labels.groupMembership },
    { key: SourcePartType.GroupOwnership, text: strings.ManageMembership.labels.groupOwnership }
  ];

  const dispatch = useDispatch<AppDispatch>();
  const [isExclusionary, setIsExclusionary] = useState(query.exclusionary);
  const [errorMessage, setErrorMessage] = useState<string>('');
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const [expanded, setExpanded] = useState(isEditingExistingJob);

  const handleSourceTypeChanged = (event: React.FormEvent<HTMLDivElement>, item: IDropdownOption | undefined): void => {
    if (!item) return;

    dispatch(updateSourcePartType({ partId: index, type: item.key as SourcePartType }));

    if (item.key === SourcePartType.HR) {
      setHRSourcePartSource({ manager: { id: undefined, depth: undefined }, filter: "" });
    }
  }

  const handleGroupMembershipSourceChange = (sourceId: string) => {
    const newQuery: ISourcePart = {
      ...part,
      query: {
        type: SourcePartType.GroupMembership,
        source: sourceId,
        exclusionary: isExclusionary
      },
    };
    dispatch(updateSourcePart(newQuery));
  };

  const handleDelete = () => {
    if (totalSourceParts > 1) {
      onDelete(index);
    } else {
      setErrorMessage(strings.ManageMembership.labels.deleteLastSourcePartWarning);
    }
  }

  const handleCopy = () => {
    const newQuery: HRSourcePart = {
      type: SourcePartType.HR,
      source: part.query.source as HRSourcePartSource,
      exclusionary: isExclusionary
    }
    const newPart: ISourcePart = {
      id: index + 1,
      query: newQuery
    };
    dispatch(copySourcePart(newPart));
  };

  useEffect(() => {
    setErrorMessage('');
  }, [query, expanded]);

  useEffect(() => {
    setIsExclusionary(part.query.exclusionary ?? false);
    if (part.query.type === SourcePartType.HR) {
      setHRSourcePartSource(part.query.source as HRSourcePartSource);
    }
  }, [part.query.type, part.query.exclusionary, part.query.source]);


  const handleSourceChange = (source: HRSourcePartSource, partId: number) => {
    const newQuery: HRSourcePart = {
      type: SourcePartType.HR,
      source: source,
      exclusionary: isExclusionary
    }
    const newPart: ISourcePart = {
      id: partId,
      query: newQuery
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
        query: updatedQuery
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
              disabled={isEditingExistingJob}
            />
            <ChoiceGroup
              className={classNames.exclusionaryPart}
              options={options}
              label="Exclude source part"
              required={true}
              onChange={handleExclusionaryChange}
              selectedKey={isExclusionary ? 'Yes' : 'No'}
              disabled={isEditingExistingJob}
            />
            {isEditingExistingJob ?
              <></>
              : <DefaultButton iconProps={{ iconName: 'Delete' }} className={classNames.deleteButton} onClick={handleDelete} >
                {strings.delete}
              </DefaultButton>
            }
          </div>

          {part.query.type === SourcePartType.HR && (
            <div key={SourcePartType.HR} className={classNames.advancedQuery}>
              <HRQuerySource source={hrSourcePartSource} partId={index} onSourceChange={isEditingExistingJob ? () => { } : handleSourceChange} />
            </div>
          )}
          {part.query.type === SourcePartType.GroupMembership && (
            <GroupQuerySource part={part} onSourceChange={handleGroupMembershipSourceChange} />
          )}
          {part.query.type === SourcePartType.GroupOwnership && (
            <AdvancedViewSourcePart key={SourcePartType.GroupOwnership} part={part} />
          )}
          <div className={classNames.error}>
            {errorMessage}
          </div>
          {part.query.type === SourcePartType.HR && (part.query.source.filter !== "" || part.query.source.manager?.id !== undefined) && (totalSourceParts === part.id) && (
          <ActionButton
            iconProps={{ iconName: "Copy" }}
            onClick={handleCopy}>
            {strings.copy}
        </ActionButton>
        )}
        </div>
      }
    </div>
  );
};
