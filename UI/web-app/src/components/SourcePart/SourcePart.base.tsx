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
  TextField,
} from '@fluentui/react';
import { ActionButton, DefaultButton, IconButton } from '@fluentui/react/lib/Button';
import { useTheme } from '@fluentui/react/lib/Theme';
import { v4 as uuidv4 } from 'uuid';
import { SourcePartStyleProps, SourcePartStyles, SourcePartProps } from './SourcePart.types';
import { AppDispatch } from '../../store';
import { updateSourcePart, copySourcePart, updateSourcePartType } from '../../store/manageMembership.slice';
import { useStrings } from '../../store/hooks';
import { ISourcePart } from '../../models/ISourcePart';
import { HRQuerySource } from '../HRQuerySource';
import { HRSourcePart, HRSourcePartSource } from '../../models/HRSourcePart';
import { GroupQuerySource } from '../GroupQuerySource';
import { SourcePartType } from '../../models/SourcePartType';
import { SourcePartQuery } from '../../models/SourcePartQuery';
import { AdvancedViewSourcePart } from '../AdvancedViewSourcePart';
import { selectSource } from '../../store/sqlMembershipSources.slice';
import { SqlMembershipSource } from '../../models';
import { selectIsJobTenantWriter, selectIsJobWriter } from '../../store/roles.slice';
import { selectIsAITitleEnabled } from '../../store/settings.slice';

const getClassNames = classNamesFunction<SourcePartStyleProps, SourcePartStyles>();

export const SourcePartBase: React.FunctionComponent<SourcePartProps> = (props: SourcePartProps) => {
  const { className, styles, partId, totalSourceParts, onDelete, query, part, isEditable } = props;
  const classNames: IProcessedStyleSet<SourcePartStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const toggleExpand = () => {
    setExpanded(!expanded);
    dispatch(updateSourcePart({ ...part, isExpanded: !expanded }));
  };

  const [hrSourcePartSource, setHRSourcePartSource] = useState<HRSourcePartSource>(query.source as HRSourcePartSource);

  const inclusionaryOptions: IChoiceGroupOption[] = [
    { key: 'Yes', text: strings.ManageMembership.labels.yesInclusionary },
    { key: 'No', text: strings.ManageMembership.labels.noInclusionary },
  ];

  const dispatch = useDispatch<AppDispatch>();
  const [isInclusionary, setIsInclusionary] = useState(!(part.query.exclusionary ?? false));
  const [errorMessage, setErrorMessage] = useState<string>('');
  const isJobWriter = useSelector(selectIsJobWriter);
  const isJobTenantWriter = useSelector(selectIsJobTenantWriter);
  const [isEditEnabled, setIsEditEnabled] = useState<boolean>(false);
  const [isEditButtonClicked, setIsEditButtonClicked] = useState<boolean>(false);
  const [expanded, setExpanded] = useState(part.isExpanded);
  const hrSource = useSelector(selectSource);
  const isAITitleEnabled = useSelector(selectIsAITitleEnabled);
  
  useEffect(() => {
    if (part.isNew) {
      dispatch(updateSourcePart({ ...part, isNew: false }));
    }
  }, [dispatch, part]);

  useEffect(() => {
    setExpanded(part.isExpanded);
  }, [part.isExpanded]);

  const handleSourceTypeChanged = (event: React.FormEvent<HTMLDivElement>, item: IDropdownOption | undefined): void => {
    if (!item) return;

    dispatch(updateSourcePartType({ partId: partId, type: item.key as SourcePartType }));

    if (item.key === SourcePartType.HR) {
      setHRSourcePartSource({ manager: { id: undefined, depth: undefined }, filter: "" });
    }
  }

  const handleGroupMembershipSourceChange = (sourceId: string, title: string) => {
    const newQuery: ISourcePart = {
      ...part,
      title: title,
      query: {
        type: SourcePartType.GroupMembership,
        source: sourceId,
        exclusionary: !isInclusionary,
      },
    };
    dispatch(updateSourcePart(newQuery));
  };

  const handleDelete = () => {
    if (totalSourceParts > 1) {
      onDelete(partId);
    } else {
      setErrorMessage(strings.ManageMembership.labels.deleteLastSourcePartWarning);
    }
  }

  const handleCopy = () => {
    const newQuery: HRSourcePart = {
      type: SourcePartType.HR,
      source: part.query.source as HRSourcePartSource,
      exclusionary: !isInclusionary
    }
    const newPart: ISourcePart = {
      id: uuidv4(),
      title: part.title || "",
      query: newQuery,
      isExpanded: true,
      isNew: true
    };
    dispatch(copySourcePart(newPart));
  };

  useEffect(() => {
    setErrorMessage('');
  }, [query, expanded]);

  const onEditButtonClick = (partId: string, partTitle: string) => {
    setIsEditButtonClicked(true);
  };

  const onTitleChange = (partId: string, partTitle: string) => {
    dispatch(updateSourcePart({ ...part, title: partTitle }));
  };

  const handleBlur = () => {
    setIsEditButtonClicked(false);
  };

  const getOptions = (hrSource?: SqlMembershipSource): IDropdownOption[] => {
    const sourceTypeOptions: IDropdownOption[] = [];
    if (hrSource) {
      sourceTypeOptions.push({
        key: SourcePartType.HR,
        text: hrSource.customLabel || hrSource.name,
      });
    } else {
      sourceTypeOptions.push( { key: SourcePartType.HR, text: strings.ManageMembership.labels.HR });
    }
    sourceTypeOptions.push( { key: SourcePartType.GroupMembership, text: strings.ManageMembership.labels.groupMembership });
    if (isJobTenantWriter) {
      sourceTypeOptions.push( { key: SourcePartType.GroupOwnership, text: strings.ManageMembership.labels.groupOwnership });
      sourceTypeOptions.push( { key: SourcePartType.PlaceMembership, text: strings.ManageMembership.labels.placeMembership });
    }
    return sourceTypeOptions;
  };

  useEffect(() => {
    setIsInclusionary(!(part.query.exclusionary ?? false));
    if (part.query.type === SourcePartType.HR) {
      setHRSourcePartSource(part.query.source as HRSourcePartSource);
    }
  }, [part.query.type, part.query.exclusionary, part.query.source]);


  const handleSourceChange = (source: HRSourcePartSource, partId: string, title?: string) => {
    const newQuery: HRSourcePart = {
      type: SourcePartType.HR,
      source: source,
      exclusionary: !isInclusionary
    }
    const newPart: ISourcePart = {
      id: partId,
      title: title ?? "",
      query: newQuery,
      isExpanded: true,
      isNew: false
    };
    dispatch(updateSourcePart(newPart));
  };

  const handleEnableEdit = (isEditEnabled: boolean) => {
    setIsEditEnabled(isEditEnabled);
  };

  const handleInclusionaryChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption): void => {
    if (!option) return;
    const isInclusionarySelected = option.key === 'Yes';

    setIsInclusionary(isInclusionarySelected);
    try {
      const updatedQuery: SourcePartQuery = {
        ...query,
        exclusionary: !isInclusionarySelected
      };

      const updatedSourcePart: ISourcePart = {
        id: partId,
        title: part.title,
        query: updatedQuery,
        isExpanded: true,
        isNew: false
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
        <div className={classNames.existingTitle}>{strings.ManageMembership.labels.sourcePart}</div>

        {isAITitleEnabled && (
          <>
            {!isEditButtonClicked && (part.title || props.title) && (
              <div className={classNames.generatedTitle}>: {part.title || props.title}</div>
            )}
            {isEditButtonClicked && (
              <div>
                <TextField
                  value={part.title || props.title}
                  onChange={(event, newValue) => onTitleChange(part.id, newValue || '')}
                  onBlur={() => handleBlur()}
                  styles={{
                    fieldGroup: classNames.titleTextField,
                  }}
                />
              </div>
            )}
            {isEditEnabled && (
              <div className={classNames.editButton}>
                <ActionButton
                  iconProps={{ iconName: 'Edit' }}
                  styles={{ root: { fontSize: 12, height: 14 }, icon: { fontSize: 10 }}}
                  onClick={() => onEditButtonClick(part.id, part.title)}>
                  {strings.edit}
                </ActionButton>
              </div>
            )}
          </>
        )}

        </div>
        <IconButton
          className={classNames.expandButton}
          iconProps={{ iconName: expanded ? 'ChevronUp' : 'ChevronDown' }}
          onClick={toggleExpand}
          title={expanded ? strings.ManageMembership.labels.collapse : strings.ManageMembership.labels.expand}
        />
      </div>
      {expanded &&
        <div className={classNames.content}>
          <div className={classNames.controls}>
            <div>
              <Dropdown
                styles={{ title: classNames.dropdownTitle }}
                options={getOptions(hrSource)}
                label={strings.ManageMembership.labels.sourceType}
                required={true}
                selectedKey={part.query.type}
                onChange={handleSourceTypeChanged}
                disabled={!isJobWriter || !isEditable}
              />
              <ChoiceGroup
                options={inclusionaryOptions}
                label={strings.ManageMembership.labels.includeSourcePart}
                required={true}
                onChange={handleInclusionaryChange}
                selectedKey={isInclusionary ? 'Yes' : 'No'}
                disabled={!isJobWriter || !isEditable}
              />
            </div>
            {isEditable &&
              <DefaultButton 
                  iconProps={{ iconName: 'Delete' }} 
                  className={classNames.deleteButton} 
                  onClick={handleDelete}
                  disabled={!isJobWriter || !isEditable}
                >
                {strings.delete}
              </DefaultButton>
            }
          </div>

          {part.query.type === SourcePartType.HR && (
            <div key={SourcePartType.HR} className={classNames.advancedQuery}>
              <HRQuerySource
                source={hrSourcePartSource}
                title={part.title || props.title}
                partId={partId}
                onSourceChange={handleSourceChange}
                onEnableEdit={handleEnableEdit}
                isEditable={isEditable}
              />
            </div>
          )}
          {part.query.type === SourcePartType.GroupMembership && (
            <GroupQuerySource part={part} onSourceChange={handleGroupMembershipSourceChange} />
          )}
          {part.query.type === SourcePartType.GroupOwnership && (
            <AdvancedViewSourcePart key={SourcePartType.GroupOwnership} part={part} isEditable={isEditable} />
          )}
          {part.query.type === SourcePartType.PlaceMembership && (
            <AdvancedViewSourcePart key={SourcePartType.PlaceMembership} part={part} isEditable={isEditable} />
          )}
          <div className={classNames.error}>
            {errorMessage}
          </div>
          {part.query.type === SourcePartType.HR && 
            isEditable &&
            (part.query.source.filter !== "" || part.query.source.manager?.id !== undefined) && (
          <div><ActionButton
            iconProps={{ iconName: "Copy" }}
            onClick={handleCopy}
            disabled={!isJobWriter || !isEditable}
          >
            {strings.copy}
        </ActionButton></div>
        )}
        </div>
      }
    </div>
  );
};
