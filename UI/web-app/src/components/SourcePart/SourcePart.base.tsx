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
  IPersonaProps,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { SourcePartStyleProps, SourcePartStyles, SourcePartProps } from './SourcePart.types';
import { AppDispatch } from '../../store';
import { updateSourcePart, updateSourcePartType } from '../../store/manageMembership.slice';
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
import { extractExclusionaryFromTitle, removeExclusionaryPrefix } from '../../utils/titleGenerator';

const getClassNames = classNamesFunction<SourcePartStyleProps, SourcePartStyles>();

export const SourcePartBase: React.FunctionComponent<SourcePartProps> = (props: SourcePartProps) => {
  const { className, styles, partId, query, part, isEditable, detailsOnly } = props;
  const classNames: IProcessedStyleSet<SourcePartStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const [hrSourcePartSource, setHRSourcePartSource] = useState<HRSourcePartSource>(query.source as HRSourcePartSource);

  const inclusionaryOptions: IChoiceGroupOption[] = [
    { key: 'Yes', text: strings.ManageMembership.labels.yesInclusionary },
    { key: 'No', text: strings.ManageMembership.labels.noInclusionary },
  ];

  const dispatch = useDispatch<AppDispatch>();
  const [isInclusionary, setIsInclusionary] = useState(!(part.query.exclusionary ?? false));
  const isJobWriter = useSelector(selectIsJobWriter);
  const isJobTenantWriter = useSelector(selectIsJobTenantWriter);
  const [, setIsEditEnabled] = useState<boolean>(false);
  const [expanded, setExpanded] = useState(part.isExpanded);
  const hrSource = useSelector(selectSource);

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
      ...part,
      title: title ?? "",
      query: newQuery
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
      const currentTitle = part.title || '';
      let newTitle = currentTitle;
      const currentlyHasExcludePrefix = extractExclusionaryFromTitle(currentTitle, strings.excludePrefix);

      if (!isInclusionarySelected && !currentlyHasExcludePrefix) {
        newTitle = `${strings.excludePrefix} ${currentTitle}`;
      } else if (isInclusionarySelected && currentlyHasExcludePrefix) {
        newTitle = removeExclusionaryPrefix(currentTitle, strings.excludePrefix);
      }

      const updatedQuery: SourcePartQuery = {
        ...query,
        exclusionary: !isInclusionarySelected
      };

      const updatedSourcePart: ISourcePart = {
        id: partId,
        title: newTitle,
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
    <div className={detailsOnly ? classNames.root : classNames.card}>
      {(expanded || detailsOnly) &&
        <div className={classNames.content}>
          <div className={classNames.controls}>
            {!detailsOnly && (
              <div>
                <ChoiceGroup
                  options={inclusionaryOptions}
                  label={strings.ManageMembership.labels.includeSourcePart}
                  onChange={handleInclusionaryChange}
                  selectedKey={isInclusionary ? 'Yes' : 'No'}
                  disabled={!isJobWriter || !isEditable}
                />
                <Dropdown
                  styles={{ title: classNames.dropdownTitle }}
                  options={getOptions(hrSource)}
                  label={strings.ManageMembership.labels.sourceType}
                  required={true}
                  selectedKey={part.query.type}
                  onChange={handleSourceTypeChanged}
                  disabled={!isJobWriter || !isEditable}
                />
              </div>
            )}
          </div>

          {part.query.type === SourcePartType.HR && (
            <div key={SourcePartType.HR} className={classNames.advancedQuery}>
              <HRQuerySource
                source={hrSourcePartSource}
                title={part.title || props.title}
                partId={partId}
                exclusionary={part.query.exclusionary}
                onSourceChange={handleSourceChange}
                onEnableEdit={handleEnableEdit}
                isEditable={isEditable}
                detailsOnly={detailsOnly}
                useOrgStructure={part.useOrgStructure}
                managerToAutoSelect={part.managerToAutoSelect}
                depthToAutoSelect={part.depthToAutoSelect}
              />
            </div>
          )}
          {part.query.type === SourcePartType.GroupMembership && (
            <GroupQuerySource part={part} isEditable={isEditable} onSourceChange={handleGroupMembershipSourceChange} />
          )}
          {part.query.type === SourcePartType.GroupOwnership && (
            <AdvancedViewSourcePart key={SourcePartType.GroupOwnership} part={part} isEditable={isEditable} />
          )}
          {part.query.type === SourcePartType.PlaceMembership && (
            <AdvancedViewSourcePart key={SourcePartType.PlaceMembership} part={part} isEditable={isEditable} />
          )}
        </div>
      }
    </div>
  );
};
