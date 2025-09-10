// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  IProcessedStyleSet,
  TextField,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import {
  IAdvancedQueryProps,
  IAdvancedQueryStyleProps,
  IAdvancedQueryStyles,
} from './AdvancedQuery.types';
import { useStrings, useQueryValidation } from "../../store/hooks";
import { AppDispatch } from '../../store';
import {
  manageMembershipAdvancedViewQuery,
  setAdvancedViewQuery,
} from '../../store/manageMembership.slice';
import { removeUnusedProperties } from '../../utils/sourcePartUtils';
import { selectIsJobWriter } from '../../store/roles.slice';
import { SourcePartQuery } from '../../models/SourcePartQuery';

const getClassNames = classNamesFunction<
  IAdvancedQueryStyleProps,
  IAdvancedQueryStyles
>();

export const AdvancedQueryBase: React.FunctionComponent<IAdvancedQueryProps> = (props) => {
  const { className, styles, query, onQueryChange, isEditable } = props;
  const strings = useStrings();
  const { validateQuery } = useQueryValidation();
  const classNames: IProcessedStyleSet<IAdvancedQueryStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const defaultAdvancedViewQuery = `[
    {
      "type": "SqlMembership",
      "source": {
        "manager": { "id": 1, "depth": 1 },
        "filter": "EmployeeId < 0"
      },
      "exclusionary": false
    },
    {
      "type": "GroupMembership",
      "source": "123e4567-e89b-12d3-a456-426614174000"
    },
    {
      "type": "GroupOwnership",
      "source": ["All"]
    },
    {
      "type": "PlaceMembership",
      "source": "SomePlace",
      "exclusionary": false
    }
  ]`;

  const dispatch = useDispatch<AppDispatch>();
  const [validationMessage, setValidationMessage] = useState<React.ReactNode | null>(null);
  const [localQuery, setLocalQuery] = useState<string>(query === '' ? defaultAdvancedViewQuery : query);
  const advancedViewQueryFromStore = useSelector(manageMembershipAdvancedViewQuery);
  const isJobWriter = useSelector(selectIsJobWriter);

  useEffect(() => {
    setLocalQuery(advancedViewQueryFromStore || defaultAdvancedViewQuery);
  }, [advancedViewQueryFromStore, defaultAdvancedViewQuery]);

  const hasValidStructure = (item: unknown): boolean => {
    return item !== null && 
           typeof item === 'object' && 
           'type' in item && 
           'source' in item;
  };

  useEffect(() => {
    if (query && query.trim().length > 0) {
      try {
        const jsonArray = JSON.parse(query);
        // Only process if the array contains valid objects with required properties
        const modifiedArray = jsonArray.map((item: unknown) => {
          // Check if the item has basic SourcePartQuery structure before processing
          if (hasValidStructure(item)) {
            try {
              return removeUnusedProperties(item as SourcePartQuery);
            } catch {
              // If removeUnusedProperties fails, return the original item
              return item;
            }
          }
          return item;
        });
        const modifiedQuery = JSON.stringify(modifiedArray);
        setLocalQuery(modifiedQuery);
      } catch (error) {
        console.error('Error parsing query:', error);
        setLocalQuery(query);
      }
    }
  }, [query]);

  const handleQueryChange = (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => {
    setLocalQuery(newValue || '');
    onQueryChange(event, newValue);
  };

  const onValidateQuery = async () => {
    try {
      const parsedQuery = JSON.parse(localQuery || '[]');
      dispatch(setAdvancedViewQuery(localQuery || '[]'));
      
      // Use the shared validation hook
      await validateQuery(parsedQuery, setValidationMessage);
    } catch (error) {
      console.error('Error validating query:', error);
      setValidationMessage(strings.ManageMembership.labels.invalidQuery);
    }
  };

  const handleBlur = () => {
    onValidateQuery();
  };

  return (
    <div className={classNames.root}>
      {strings.ManageMembership.labels.query}
      <TextField
        id="advancedQueryTextField"
        title={strings.ManageMembership.labels.query}
        styles={{ root: classNames.textField, fieldGroup: classNames.textFieldGroup }}
        multiline
        rows={25}
        resizable
        value={localQuery}
        onChange={handleQueryChange}
        onBlur={handleBlur}
        disabled={!isJobWriter || !isEditable}
      />
      {validationMessage && (
        <div className={validationMessage === strings.ManageMembership.labels.validQuery ? classNames.successMessage : classNames.errorMessage}>
          {validationMessage}
        </div>
      )}
    </div>
  );
};
