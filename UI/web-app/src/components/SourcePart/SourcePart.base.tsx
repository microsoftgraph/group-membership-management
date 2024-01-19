// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import { ChoiceGroup, classNamesFunction, Dropdown, IChoiceGroupOption, IDropdownOption, IProcessedStyleSet } from '@fluentui/react';
import { DefaultButton, IconButton } from '@fluentui/react/lib/Button';
import { useTheme } from '@fluentui/react/lib/Theme';
import { SourcePartStyleProps, SourcePartStyles, SourcePartProps } from './SourcePart.types';
import { AdvancedQuery } from '../AdvancedQuery';
import { AppDispatch } from '../../store';
import { updateSourcePart, updateSourcePartValidity } from '../../store/manageMembership.slice';
import { useStrings } from '../../store/hooks';
import { HRSourcePart } from '../../models/HRSourcePart';

const getClassNames = classNamesFunction<SourcePartStyleProps, SourcePartStyles>();

export const SourcePartBase: React.FunctionComponent<SourcePartProps> = (props: SourcePartProps) => {
  const { className, styles, index, totalSourceParts, onDelete, query, onQueryChange, part } = props;
  const classNames: IProcessedStyleSet<SourcePartStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const toggleExpand = () => {
    setExpanded(!expanded);
  };
  const options: IChoiceGroupOption[] = [
    { key: 'Yes', text: 'Yes' },
    { key: 'No', text: 'No' },
  ];

  const sourceTypeOptions: IDropdownOption[] = [
    { key: 'HR', text: 'HR' },
    { key: 'Groups', text: 'Groups', disabled: true }
  ];

  const dispatch = useDispatch<AppDispatch>();
  const [expanded, setExpanded] = useState(false);
  const [sourceType, setSourceType] = useState<IDropdownOption>({ key: 'HR', text: 'HR' });
  const [isExclusionary, setIsExclusionary] = useState(part.isExclusionary);
  const [errorMessage, setErrorMessage] = useState<string>('');

  console.log("SourcePart query:", JSON.stringify(query, null, 2));
  console.log("double",  JSON.stringify( JSON.stringify(query, null, 2), null, 2))
  const handleSourceTypeChanged = (event: React.FormEvent<HTMLDivElement>, item: IDropdownOption | undefined): void => {
    if (item) {
      setSourceType(item);
    }
  }

  const handleDelete = () => {
    if (totalSourceParts > 1) {
      onDelete(part.id);
    } else {
      setErrorMessage("Cannot delete the last source part.");
    }
  }

  useEffect(() => {
    setErrorMessage('');
  }, [sourceType, expanded]);

  useEffect(() => {
    setIsExclusionary(part.isExclusionary);
  }, [part.isExclusionary]);

  const handleQueryValidation = (isValid: boolean, partId: number) => {
    dispatch(updateSourcePartValidity({ partId: partId, isValid: isValid }));
  };

  const handleExclusionaryChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption): void => {
    if (!option) return;
    const isExclusionarySelected = option.key === 'Yes';

    setIsExclusionary(isExclusionarySelected);
    try {
      const updatedQuery: HRSourcePart = {
        ...part.query,
        exclusionary: isExclusionarySelected
      };

      dispatch(updateSourcePart({
        id: part.id,
        query: updatedQuery,
        isValid: part.isValid,
        isExclusionary: isExclusionarySelected
      }));
    } catch (error) {
      console.error(`Error updating source part query:`, error);
    }
  };

  return (
    <div className={classNames.card}>
      <div className={classNames.header}>
        <div className={classNames.title}>
          Source Part {index}
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
              options={sourceTypeOptions}
              label="Source Type"
              required={true}
              selectedKey={sourceType.key}
              onChange={handleSourceTypeChanged}
            />
            <ChoiceGroup
              className={classNames.exclusionaryPart}
              options={options}
              label="Exclude source part"
              required={true}
              onChange={handleExclusionaryChange}
              selectedKey={isExclusionary ? strings.yes : strings.no}
            />
            <DefaultButton iconProps={{ iconName: 'Delete' }} className={classNames.deleteButton} onClick={handleDelete} >
              {strings.delete}
            </DefaultButton>
          </div>
          {sourceType.key === 'HR' && (
            <div className={classNames.advancedQuery}>
              <AdvancedQuery
                query={query}
                onQueryChange={onQueryChange}
                partId={part.id}
                onValidate={handleQueryValidation}
              />
            </div>
          )}
          <div className={classNames.error}>
            {errorMessage}
          </div>
        </div>
      }
    </div>
  );
};
