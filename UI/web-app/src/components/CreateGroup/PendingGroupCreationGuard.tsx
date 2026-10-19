// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useCallback, useEffect } from 'react';
import { useSelector } from 'react-redux';
import { useBeforeUnload, useBlocker } from 'react-router-dom';
import { manageMembershipCreateGroupLoading } from '../../store/manageMembership.slice';

export const PendingGroupCreationGuard = () => {
  const pending = useSelector(manageMembershipCreateGroupLoading);
  const blocker = useBlocker(pending);

  useEffect(() => {
    if (blocker.state === 'blocked') blocker.reset();
  }, [blocker]);

  useBeforeUnload(useCallback((event: BeforeUnloadEvent) => {
    if (pending) {
      event.preventDefault();
      event.returnValue = '';
    }
  }, [pending]));

  return null;
};
