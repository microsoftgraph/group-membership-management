// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type TypedUseSelectorHook, useDispatch, useSelector } from 'react-redux';
import { useCallback, useMemo } from 'react';
import Ajv from 'ajv';

import type { RootState, AppDispatch } from './store';
import { IStrings } from '../services/localization';
import { selectStrings } from './localization.slice';
import { setIsAdvancedQueryValid } from './manageMembership.slice';
import schemaDefinition from '../models/schemas/Query.json';
import { SyncJobQuery } from '../models/SyncJobQuery';
import { SourcePartQuery } from '../models/SourcePartQuery';
import { SourcePartType } from '../models/SourcePartType';
import { validateGroup } from './groups.api';
import { validateSqlFilters } from './sqlMembershipSources.api';

// Use throughout the app instead of plain `useDispatch` and `useSelector`
export const useAppDispatch: () => AppDispatch = useDispatch;
export const useAppSelector: TypedUseSelectorHook<RootState> = useSelector;

// Localization hooks
export const useStrings: () => IStrings = () => useAppSelector(selectStrings);

// Query validation hook - shared between AdvancedQuery and MembershipConfiguration
export const useQueryValidation = () => {
  const dispatch = useAppDispatch();
  const strings = useStrings();

  // Validation constants (memoized to prevent re-creation)
  const schema = useMemo(() => schemaDefinition, []);
  const ajv = useMemo(() => new Ajv(), []);
  const PLACEHOLDER_GROUP_IDS = useMemo(() => [
    "123e4567-e89b-12d3-a456-426614174000"
  ], []);

  const validateQuery = useCallback(async (queryToValidate: SyncJobQuery, showValidationMessage?: (message: React.ReactNode) => void): Promise<boolean> => {
    try {
      // Check for empty query (null, undefined, empty array, or empty object)
      if (!queryToValidate || 
          !Array.isArray(queryToValidate) || 
          queryToValidate.length === 0 ||
          (typeof queryToValidate === 'object' && Object.keys(queryToValidate).length === 0)) {
        if (showValidationMessage) {
          showValidationMessage(strings.ManageMembership.labels.invalidQuery || 'Query cannot be empty. Please add at least one source part.');
        }
        dispatch(setIsAdvancedQueryValid(false));
        return false;
      }

      const validate = ajv.compile(schema);
      let isValid = validate(queryToValidate);

      if (isValid) {
        // Validate groups
        const groupValidationResults = await Promise.all(
          (queryToValidate as Array<SourcePartQuery>).map(async (part) => {
            if (
              part.type === SourcePartType.GroupMembership &&
              part.source &&
              !PLACEHOLDER_GROUP_IDS.includes(part.source)
            ) {
              const result = await dispatch(validateGroup(part.source)).unwrap();
              return result;
            }
            // Skip validation for placeholder group IDs
            return { groupId: part.source, isValid: true };
          })
        );

        const invalidGroups = groupValidationResults.filter(result => !result.isValid);
        if (invalidGroups.length > 0) {
          isValid = false;
          if (showValidationMessage) {
            const invalidGroupIds = invalidGroups.map(result => result.groupId).join(', ');
            showValidationMessage(`${strings.ManageMembership.labels.invalidGroups} ${invalidGroupIds}`);
          }
        } else {
          // Validate SQL filters
          const sqlFiltersToValidate = new Map<number, string>();
          (queryToValidate as Array<SourcePartQuery>).forEach((part, index) => {
            if (part.type === SourcePartType.HR && part.source && part.source.filter) {
              sqlFiltersToValidate.set(index, part.source.filter);
            }
          });

          if (sqlFiltersToValidate.size > 0) {
            const sqlValidationResponse = await dispatch(validateSqlFilters(sqlFiltersToValidate)).unwrap();
            if (!sqlValidationResponse.isValid) {
              isValid = false;
              if (showValidationMessage) {
                const errorsMap = new Map<number, string>(
                  Object.entries(sqlValidationResponse.errors).map(([key, value]) => [Number(key), value])
                );
                const sqlErrorsMessage = (
                  <div>
                    <div>{strings.ManageMembership.labels.invalidSqlFilters}</div>
                    {Array.from(errorsMap.entries()).map((error) => {
                      const message = `For Source Part ${error[0] + 1}: ${error[1]}..`;
                      return <div key={error[0]}>{message}</div>;
                    })}
                  </div>
                );
                showValidationMessage(sqlErrorsMessage);
              }
            }
          }

          if (isValid && showValidationMessage) {
            showValidationMessage(strings.ManageMembership.labels.validQuery || 'Query is valid');
          }
        }
      } else {
        if (showValidationMessage) {
          const errorsFromAjv = validate.errors;
          const formattedErrors = errorsFromAjv?.map((error, index) => {
            let message = error.message;
            if (error.keyword === 'type') {
              message = `Expected ${error.schema} but got type ${typeof error.data} at ${error.instancePath}..`;
            }
            return <div key={index}>{message}</div>;
          });
          showValidationMessage(<div>{formattedErrors}</div>);
        }
      }

      dispatch(setIsAdvancedQueryValid(isValid));
      return isValid;
    } catch (error) {
      console.error('Error validating query:', error);
      if (showValidationMessage) {
        showValidationMessage(strings.ManageMembership.labels.invalidQuery || 'Invalid query format');
      }
      dispatch(setIsAdvancedQueryValid(false));
      return false;
    }
  }, [ajv, schema, PLACEHOLDER_GROUP_IDS, dispatch, strings]);

  return { validateQuery };
};
