// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import {
  IProcessedStyleSet,
  TextField,
  classNamesFunction,
  format,
  useTheme,
} from '@fluentui/react';
import Ajv, { ErrorObject } from 'ajv';
import {
  IAdvancedViewSourcePartProps,
  IAdvancedViewSourcePartStyleProps,
  IAdvancedViewSourcePartStyles,
} from './AdvancedViewSourcePart.types';
import { useStrings } from "../../store/hooks";
import GroupOwnershipSchema from '../../models/schemas/GroupOwnershipSchema.json';
import PlaceMembershipSchema from '../../models/schemas/PlaceMembershipSchema.json';
import { AppDispatch } from '../../store';
import {
  setIsAdvancedQueryValid,
  updateSourcePart,
} from '../../store/manageMembership.slice';
import { ISourcePart } from '../../models/ISourcePart';
import { GroupOwnershipSourcePart } from '../../models/GroupOwnershipSourcePart';
import { PlaceMembershipSourcePart } from '../../models/PlaceMembershipSourcePart';

const getClassNames = classNamesFunction<
  IAdvancedViewSourcePartStyleProps,
  IAdvancedViewSourcePartStyles
>();

interface ExtendedErrorObject extends ErrorObject<string, Record<string, any>, unknown> {
  dataPath: string;
}

export const AdvancedViewSourcePartBase: React.FunctionComponent<IAdvancedViewSourcePartProps> = (props) => {
  const { className, styles, part } = props;
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
  const [localQuery, setLocalQuery] = useState<string | undefined>(JSON.stringify(part.query));
  const schema = part.query.type === 'GroupOwnership' ? GroupOwnershipSchema : PlaceMembershipSchema;
  const ajv = new Ajv();

  useEffect(() => {
    setLocalQuery(JSON.stringify(part.query));
  }, [part.query]);

  const formatErrors = (errors: (ErrorObject<string, Record<string, any>, unknown> & { dataPath: string })[] | null | undefined) => {
    if (!errors || errors.length === 0) return null;

    return (
      <div>
        {errors.map((error, index) => {
          let message = error.message;
          if (error.keyword === 'type') {
            format(strings.ManageMembership.labels.errorOnSchema, error.schema, error.data, error.dataPath)
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
  };

  const onValidateQuery = () => {
    try {
      const parsedQuery = JSON.parse(localQuery || '{}');
      const validate = ajv.compile(schema);
      const isValid = validate(parsedQuery);

      if(isValid) {
        const updatedSourcePart: ISourcePart = {
          id: part.id,
          query: JSON.parse(localQuery ?? '{}') as GroupOwnershipSourcePart | PlaceMembershipSourcePart
        };
        dispatch(updateSourcePart(updatedSourcePart));
        setValidationMessage(strings.ManageMembership.labels.validQuery);
      }
      else{
        const errorsFromAjv = validate.errors;
        const formattedErrors = formatErrors(errorsFromAjv as ExtendedErrorObject[] | null | undefined);
        setValidationMessage(formattedErrors);
      }
      dispatch(setIsAdvancedQueryValid(isValid));
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
        title={strings.ManageMembership.labels.query}
        styles={{ root: classNames.textField, fieldGroup: classNames.textFieldGroup }}
        multiline
        rows={25}
        resizable
        value={localQuery}
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
