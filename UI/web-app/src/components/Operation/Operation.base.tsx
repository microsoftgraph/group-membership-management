// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { classNamesFunction, type IProcessedStyleSet, DefaultButton } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type {
  OperationProps,
  OperationStyleProps,
  OperationStyles,
} from './Operation.types';
import { useStrings } from '../../store/hooks';
import { ServiceStatuses } from '../../models/ServiceStatuses';
import { fetchServiceStatus, processOperation } from '../../store/operations.api';
import { resetError, selectOperationStatus, selectOperationIsLoading, selectOperationError } from '../../store/operations.slice';
import { AppDispatch } from '../../store';
import { Operations } from '../../models/Operations';

export const getClassNames = classNamesFunction<OperationStyleProps, OperationStyles>();

export const OperationBase: React.FunctionComponent<OperationProps> = (props: OperationProps) => {
  const { title, description, className, styles } = props;
  const classNames: IProcessedStyleSet<OperationStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  const dispatch: AppDispatch = useDispatch();
  const status = useSelector(selectOperationStatus);
  const isLoading = useSelector(selectOperationIsLoading);
  const error = useSelector(selectOperationError);
  const strings = useStrings();

  useEffect(() => {
    dispatch(fetchServiceStatus());
  }, [dispatch]);

  const handleStop = async () => {
    dispatch(processOperation(Operations.Stop));
  };

  const handleReset = async () => {
    dispatch(processOperation(Operations.Reset));
  };

  const handleStart = async () => {
    dispatch(processOperation(Operations.Start));
  };

  useEffect(() => {
    if (error) {
      console.error('Operation error:', error);
      dispatch(resetError());
    }
  }, [error, dispatch]);

  const isButtonDisabled = isLoading || status === ServiceStatuses.Stopping || status === ServiceStatuses.Resetting || status === ServiceStatuses.Starting || !!error;

  return (
    <div className={classNames.card}>
      <div className={classNames.title}>{title}</div>
      <div className={classNames.description}>{description}</div>
      <div className={classNames.buttonContainer}>
        {status === ServiceStatuses.Stopped || status === ServiceStatuses.Starting ? (
          <DefaultButton
            text={status === ServiceStatuses.Starting ? 'Starting...' : 'Start GMM'} 
            onClick={handleStart}
            disabled={isButtonDisabled}
            className={classNames.button}
          />
        ) : (
          <DefaultButton
            text={status === ServiceStatuses.Stopping ? 'Stopping...' : 'Stop GMM'} 
            onClick={handleStop}
            disabled={isButtonDisabled || status !== ServiceStatuses.Running}
            className={classNames.button}
          />
        )}
        <DefaultButton
          text={status === ServiceStatuses.Resetting ? 'Resetting...' : 'Reset GMM'}
          onClick={handleReset}
          disabled={isButtonDisabled || status !== ServiceStatuses.Running}
          className={classNames.button}
        />
      </div>
    </div>
  );
};
