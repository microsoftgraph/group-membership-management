// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import React, { useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import { useLocation } from 'react-router-dom';
import { clearMessages, closePanel } from '../../store/copilot.slice';

import { ManageMembershipBase } from './ManageMembership.base';
import { getStyles } from './ManageMembership.styles';
import {
  type IManageMembershipProps,
  type IManageMembershipStyleProps,
  type IManageMembershipStyles,
} from './ManageMembership.types';

const StyledManageMembership = styled<
  IManageMembershipProps,
  IManageMembershipStyleProps,
  IManageMembershipStyles
>(ManageMembershipBase, getStyles, undefined, {
  scope: 'ManageMembership',
});

const MembershipSession: React.FunctionComponent<IManageMembershipProps> = (props) => {
  const dispatch = useDispatch();
  const [ready, setReady] = useState(false);

  useEffect(() => {
    dispatch(clearMessages());
    dispatch(closePanel());
    setReady(true);
    return () => {
      dispatch(clearMessages());
      dispatch(closePanel());
    };
  }, [dispatch]);

  // Do not mount the editor or its Copilot effects with another session's state.
  return ready ? <StyledManageMembership {...props} /> : null;
};

export const ManageMembership: React.FunctionComponent<IManageMembershipProps> = (props) => {
  const location = useLocation();
  // A new navigation also discards panel-local drafts and cached descriptions.
  return <MembershipSession key={location.key} {...props} />;
};
