// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { classNamesFunction, type IProcessedStyleSet, DefaultButton } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type {
  OperationProps,
  OperationStyleProps,
  OperationStyles,
} from './Operation.types';
import { useStrings } from '../../store/hooks';
import { OperationStatus } from '../../models/OperationStatus';
import { fetchOperationStatus,stopOperation, resetOperation } from '../../store/operations.api';
import { resetError, selectOperationStatus, selectOperationIsLoading, selectOperationError } from '../../store/operations.slice';
import { AppDispatch } from '../../store';

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
    dispatch(fetchOperationStatus());
  }, [dispatch]);

  const handleStop = async () => {
    dispatch(stopOperation());
  };

  const handleReset = async () => {
    dispatch(resetOperation());
  };

  useEffect(() => {
    if (error) {
      console.error('Operation error:', error);
      dispatch(resetError());
    }
  }, [error, dispatch]);

  return (
    <div className={classNames.card}>
      <div className={classNames.title}>{title}</div>
      <div className={classNames.buttonContainer}>
        <DefaultButton
          text={status === OperationStatus.Stopped ? 'Stopped' : 'Stop GMM'}
          onClick={handleStop}
          disabled={isLoading || status === OperationStatus.Stopped}
          className={classNames.button}
        />
        <DefaultButton
          text={status === OperationStatus.Stopping ? 'Resetting...' : 'Reset GMM'}
          onClick={handleReset}
          disabled={isLoading || status !== OperationStatus.Stopped}
          className={classNames.button}
        />
      </div>
    </div>
  );
};