// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { useDispatch, useSelector } from 'react-redux';
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
import { useStrings } from "../../store/hooks";
import schemaDefinition from '../../Query.json';
import HRPartQuery from '../../HRPartQuery.json';
import { AppDispatch } from '../../store';
import { 
  buildCompositeQuery,
  getSourcePartsFromState,
  manageMembershipIsAdvancedView,
  setCompositeQuery,
  setIsQueryValid,
  updateSourcePartValidity
} from '../../store/manageMembership.slice';

const getClassNames = classNamesFunction<
  IAdvancedQueryStyleProps,
  IAdvancedQueryStyles
>();

interface ExtendedErrorObject extends ErrorObject<string, Record<string, any>, unknown> {
  dataPath: string;
}

export const AdvancedQueryBase: React.FunctionComponent<IAdvancedQueryProps> = (props) => {
  const { className, styles, query, onQueryChange, partId, onValidate } = props;
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
  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
  const sourceParts = useSelector(getSourcePartsFromState);
  const schema = isAdvancedView ? schemaDefinition : HRPartQuery;
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

  const handleQueryChange = (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => {
    onQueryChange(event, newValue);
    setValidationMessage(null);

    if (isAdvancedView) {
        dispatch(setIsQueryValid(false));
    } else {
        dispatch(updateSourcePartValidity({ partId, isValid: false }));
    }
  };

  const onValidateQuery = () => {
    try {
      const validate = ajv.compile(schema);
      const parsedQuery = JSON.parse(query);
      const isValid = validate(parsedQuery);
      if (!isAdvancedView) {
        const compositeQuery = buildCompositeQuery(sourceParts);
        dispatch(setCompositeQuery(compositeQuery));
        dispatch(updateSourcePartValidity({ partId, isValid }));
      }
      else{
        dispatch(setIsQueryValid(isValid));
      }

      if (!isValid) {
        const errorsFromAjv = validate.errors;
        const formattedErrors = formatErrors(errorsFromAjv as ExtendedErrorObject[] | null | undefined);
        setValidationMessage(formattedErrors);
      } else {
        setValidationMessage(strings.ManageMembership.labels.validQuery);
      }
      onValidate(isValid, partId)
    }
    catch (error) {
      console.error('Error building composite query:', error);
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
        onChange={handleQueryChange}
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
