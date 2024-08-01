import React, { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { classNamesFunction, DefaultButton } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { fetchServiceStatus, processOperation } from '../../store/operations.api';
import { resetError, selectOperationDisplayStatus, selectOperationIsLoading, selectOperationError, selectOperationInProgress } from '../../store/operations.slice';
import { AppDispatch } from '../../store';
import { ServiceStatuses } from '../../models/ServiceStatuses';
import { Operations } from '../../models/Operations';
import type { OperationProps, OperationStyles, OperationStyleProps } from './Operation.types';

export const getClassNames = classNamesFunction<OperationStyleProps, OperationStyles>();

export const OperationBase: React.FunctionComponent<OperationProps> = (props: OperationProps) => {
  const { title, description, className, styles } = props;
  const classNames = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  const dispatch: AppDispatch = useDispatch();
  const displayStatus = useSelector(selectOperationDisplayStatus);
  const isLoading = useSelector(selectOperationIsLoading);
  const error = useSelector(selectOperationError);
  const isOperationInProgress = useSelector(selectOperationInProgress);

  useEffect(() => {
    dispatch(fetchServiceStatus());
  }, [dispatch]);

  const handleOperation = (operation: Operations) => {
    dispatch(processOperation(operation))
      .then(() => dispatch(fetchServiceStatus()));
  };

  useEffect(() => {
    if (error) {
      console.error('Operation error:', error);
      dispatch(resetError());
    }
  }, [error, dispatch]);

  const isButtonDisabled = isLoading || isOperationInProgress || !!error;

  return (
    <div className={classNames.card}>
      <div className={classNames.title}>{title}</div>
      <div className={classNames.description}>{description}</div>
      <div className={classNames.buttonContainer}>
        {displayStatus === ServiceStatuses.Stopped || displayStatus === ServiceStatuses.Starting ? (
          <DefaultButton
            text={displayStatus === ServiceStatuses.Starting ? 'Starting...' : 'Start GMM'}
            onClick={() => handleOperation(Operations.Start)}
            disabled={isButtonDisabled || displayStatus == ServiceStatuses.Starting}
            className={classNames.button}
          />
        ) : (
          <DefaultButton
            text={displayStatus === ServiceStatuses.Stopping ? 'Stopping...' : 'Stop GMM'}
            onClick={() => handleOperation(Operations.Stop)}
            disabled={isButtonDisabled || displayStatus !== ServiceStatuses.Running}
            className={classNames.button}
          />
        )}
        <DefaultButton
          text={displayStatus === ServiceStatuses.Resetting ? 'Resetting...' : 'Reset GMM'}
          onClick={() => handleOperation(Operations.Reset)}
          disabled={isButtonDisabled || displayStatus !== ServiceStatuses.Running}
          className={classNames.button}
        />
      </div>
    </div>
  );
};
