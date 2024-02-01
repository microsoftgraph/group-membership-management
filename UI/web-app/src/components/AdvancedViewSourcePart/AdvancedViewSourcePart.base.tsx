// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import {
  IProcessedStyleSet,
  TextField,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import Ajv, { ErrorObject } from 'ajv';
import {
  IAdvancedViewSourcePartProps,
  IAdvancedViewSourcePartStyleProps,
  IAdvancedViewSourcePartStyles,
} from './AdvancedViewSourcePart.types';
import { useStrings } from "../../store/hooks";
import SqlMembershipSchema from '../../SqlMembershipSchema.json';
import { AppDispatch } from '../../store';
import {
  updateSourcePart,
  updateSourcePartValidity
} from '../../store/manageMembership.slice';
import { ISourcePart, SourcePartQuery } from '../../models/ISourcePart';

const getClassNames = classNamesFunction<
  IAdvancedViewSourcePartStyleProps,
  IAdvancedViewSourcePartStyles
>();

interface ExtendedErrorObject extends ErrorObject<string, Record<string, any>, unknown> {
  dataPath: string;
}

export const AdvancedViewSourcePartBase: React.FunctionComponent<IAdvancedViewSourcePartProps> = (props) => {
  const { className, styles, query, partId, onValidate } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<IAdvancedViewSourcePartStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const dispatch = useDispatch<AppDispatch>();
  const [validationMessage, setValidationMessage] = useState<React.ReactNode | null>(null);
  const [localQuery, setLocalQuery] = useState<SourcePartQuery | undefined>(query);
  const schema = SqlMembershipSchema;
  const ajv = new Ajv();

  useEffect(() => {
    setLocalQuery(query);
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
    setLocalQuery(JSON.parse(newValue ?? ''));
  };

  const onValidateQuery = () => {
    try {
      const validate = ajv.compile(schema);
      const isValid = validate(localQuery);

      if(isValid) {
        const updatedSourcePart: ISourcePart = {
          id: partId,
          query: localQuery,
          isValid: true
        };
        dispatch(updateSourcePart(updatedSourcePart));
        setValidationMessage(strings.ManageMembership.labels.validQuery);
      }
      else{
        const errorsFromAjv = validate.errors;
        const formattedErrors = formatErrors(errorsFromAjv as ExtendedErrorObject[] | null | undefined);
        setValidationMessage(formattedErrors);
      } 
      dispatch(updateSourcePartValidity({ partId, isValid }));
      onValidate(isValid, partId)
    }
    catch (error) {
      console.error('Error validating query:', error);
      setValidationMessage(strings.ManageMembership.labels.invalidQuery);
    }
  }

  const handleBlur = () => {
    onValidateQuery();
  };

  return (
    <div className={classNames.root}>
      {strings.ManageMembership.labels.query}
      <TextField
        styles={{ root: classNames.textField, fieldGroup: classNames.textFieldGroup }}
        multiline
        rows={25}
        resizable
        value={JSON.stringify(localQuery, null, 2)}
        onChange={handleQueryChange}
        onBlur={handleBlur}
      />
      {validationMessage && (
        <div className={validationMessage === strings.ManageMembership.labels.validQuery ? classNames.successMessage : classNames.errorMessage}>
          {validationMessage}
        </div>
      )}
    </div>
  );
};
