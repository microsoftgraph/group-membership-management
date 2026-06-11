// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { useSelector } from 'react-redux';
import { selectOperationError, selectOperationStatus } from '../../store/operations.slice';
import { ServiceStatuses } from '../../models';
import { Maintenance } from './Maintenance';

export const MaintenanceCheckWrapper = <P extends object>(Component: React.ComponentType<P>) => {
  return (props: P) => {
    const operationStatusError = useSelector(selectOperationError);
    const operationStatus = useSelector(selectOperationStatus);

    const allowedStatuses = [ServiceStatuses.Running, ServiceStatuses.Rescheduling];

    if (operationStatusError != null || (operationStatus != null && !allowedStatuses.includes(operationStatus))) {
      return (
        <Maintenance />
      );
    }

    return <Component {...props} />;
  };
};