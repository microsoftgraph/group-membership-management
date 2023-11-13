// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { useDispatch } from 'react-redux';
import {
  DefaultButton,
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
import { useStrings } from "../../localization/";
import schemaDefinition from '../../Query.json';
import { AppDispatch } from '../../store';
import { setIsQueryValid, setNewJobQuery } from '../../store/manageMembership.slice';

const getClassNames = classNamesFunction<
  IAdvancedQueryStyleProps,
  IAdvancedQueryStyles
>();

interface ExtendedErrorObject extends ErrorObject<string, Record<string, any>, unknown> {
  dataPath: string;
}

export const AdvancedQueryBase: React.FunctionComponent<IAdvancedQueryProps> = (props) => {
  const { className, styles, query } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<IAdvancedQueryStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const dispatch = useDispatch<AppDispatch>();
  const [validationMessage, setValidationMessage] = React.useState<React.ReactNode | null>(null);
  const ajv = new Ajv();

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

  const onQueryChange = (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => {
    dispatch(setNewJobQuery(newValue || ""));
    dispatch(setIsQueryValid(false))
    setValidationMessage(null);
  };

  const onValidateQuery = () => {
    try {
      const validate = ajv.compile(schemaDefinition);
      const parsedQuery = JSON.parse(query);
      const isValid = validate(parsedQuery);
      if (!isValid) {
        const errorsFromAjv = validate.errors;
        const formattedErrors = formatErrors(errorsFromAjv as ExtendedErrorObject[] | null | undefined);
        setValidationMessage(formattedErrors);
        dispatch(setIsQueryValid(false));
      } else {
        setValidationMessage(strings.ManageMembership.labels.validQuery);
        dispatch(setIsQueryValid(true));
      }
    }
    catch (error) {
      setValidationMessage(strings.ManageMembership.labels.invalidQuery);
      dispatch(setIsQueryValid(false));
    }
  }

  return (
    <div className={classNames.root}>
      {strings.ManageMembership.labels.query}
      <TextField
        styles={{ root: classNames.textField, fieldGroup: classNames.textFieldGroup }}
        multiline
        rows={25}
        resizable
        value={query}
        onChange={onQueryChange}
      />
      <DefaultButton className={classNames.button} onClick={onValidateQuery}>{strings.ManageMembership.labels.validateQuery}</DefaultButton>
      {validationMessage && (
        <div className={validationMessage === strings.ManageMembership.labels.validQuery ? classNames.successMessage : classNames.errorMessage}>
          {validationMessage}
        </div>
      )}

    </div>
  );
};
