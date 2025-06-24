import React from 'react';
import { useSelector } from 'react-redux';
import { selectOperationError, selectOperationStatus } from '../store/operations.slice';
import { ServiceStatuses } from '../models';
import { MaintenancePage } from './MaintenancePage';

export const MaintenanceCheckWrapper = <P extends object>(Component: React.ComponentType<P>) => {
  return (props: P) => {
    const operationStatusError = useSelector(selectOperationError);
    const operationStatus = useSelector(selectOperationStatus);

    if (operationStatusError != null || (operationStatus != null && operationStatus != ServiceStatuses.Running)) {
      return (
        <MaintenancePage />
      );
    }

    return <Component {...props} />;
  };
};