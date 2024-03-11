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
import Ajv, { ErrorObject } from 'ajv';
import {
  IAdvancedQueryProps,
  IAdvancedQueryStyleProps,
  IAdvancedQueryStyles,
} from './AdvancedQuery.types';
import { useStrings } from "../../store/hooks";
import schemaDefinition from '../../models/schemas/Query.json';
import { AppDispatch } from '../../store';
import {
  manageMembershipIsEditingExistingJob,
  setAdvancedViewQuery,
  setIsAdvancedQueryValid,
} from '../../store/manageMembership.slice';
import { removeUnusedProperties } from '../../utils/sourcePartUtils';

const getClassNames = classNamesFunction<
  IAdvancedQueryStyleProps,
  IAdvancedQueryStyles
>();

interface ExtendedErrorObject extends ErrorObject<string, Record<string, any>, unknown> {
  dataPath: string;
}

export const AdvancedQueryBase: React.FunctionComponent<IAdvancedQueryProps> = (props) => {
  const { className, styles, query, onQueryChange } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<IAdvancedQueryStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const defaultAdvancedViewQuery: string = `[
    {
      "type": "SqlMembership",
      "source": {
        "manager": {
          "id": 0,
          "depth": 0
        },    
        filter: "" 
      },
    },
    {
      "type": "GroupMembership",
      "source": "00000000-0000-0000-0000-000000000000"
    },
    {
      "type": "GroupOwnership",
      "source": ["All"]
    }
  ]`;

  const dispatch = useDispatch<AppDispatch>();
  const [validationMessage, setValidationMessage] = useState<React.ReactNode | null>(null);
  const [localQuery, setLocalQuery] = useState<string>(query === '' ? defaultAdvancedViewQuery : query);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const schema = schemaDefinition;
  const ajv = new Ajv();

  useEffect(() => {
    if (query && query.trim().length > 0) {
      try {
        let jsonArray = JSON.parse(query);
        let modifiedArray = jsonArray.map(removeUnusedProperties);
        let modifiedQuery = JSON.stringify(modifiedArray);
        setLocalQuery(modifiedQuery);
      } catch (error) {
        throw new Error('Error parsing query');
      }
    }
  }, [query]);

  const formatErrors = (errors: (ErrorObject<string, Record<string, any>, unknown> & { dataPath: string })[] | null | undefined) => {
    if (!errors || errors.length === 0) return null;

    return (
      <div>
        {errors.map((error, index) => {
          let message = error.message;
          if (error.keyword === 'type') {
            message = `Expected ${error.schema} but got type ${typeof error.data} at ${error.dataPath}..`;
          }
          return (
            <div key={index}>
              {message}
            </div>
          );
        })}
      </div>
    );
  };

  const handleQueryChange = (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => {
    setLocalQuery(newValue || '');
    onQueryChange(event, newValue);
  };

  const onValidateQuery = () => {
    try {
      const parsedQuery = JSON.parse(localQuery || '[]');
      const validate = ajv.compile(schema);
      const isValid = validate(parsedQuery);

      if (isValid) {
        dispatch(setAdvancedViewQuery(localQuery || '[]'));
        setValidationMessage(strings.ManageMembership.labels.validQuery);
      } else {
        const errorsFromAjv = validate.errors;
        const formattedErrors = formatErrors(errorsFromAjv as ExtendedErrorObject[] | null | undefined);
        setValidationMessage(formattedErrors);
      }
      dispatch(setIsAdvancedQueryValid(isValid));
    } catch (error) {
      console.error('Error validating query:', error);
      setValidationMessage(strings.ManageMembership.labels.invalidQuery);
      dispatch(setIsAdvancedQueryValid(false));
    }
  };

  const handleBlur = () => {
    onValidateQuery();
  };

  return (
    <div className={classNames.root}>
      {strings.ManageMembership.labels.query}
      <TextField
        title={strings.ManageMembership.labels.query}
        styles={{ root: classNames.textField, fieldGroup: classNames.textFieldGroup }}
        multiline
        rows={25}
        resizable
        value={localQuery}
        onChange={handleQueryChange}
        onBlur={handleBlur}
        disabled={isEditingExistingJob}
      />
      {validationMessage && (
        <div className={validationMessage === strings.ManageMembership.labels.validQuery ? classNames.successMessage : classNames.errorMessage}>
          {validationMessage}
        </div>
      )}
    </div>
  );
};
