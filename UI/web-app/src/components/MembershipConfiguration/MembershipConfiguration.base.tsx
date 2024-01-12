// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { useDispatch } from 'react-redux';
import { MembershipConfigurationProps } from './MembershipConfiguration.types';
import { AppDispatch } from '../../store';
import { MembershipConfigurationView } from './MembershipConfiguration.view';

export const MembershipConfigurationBase: React.FunctionComponent<MembershipConfigurationProps> = (props: MembershipConfigurationProps) => {
  const dispatch = useDispatch<AppDispatch>();
  // const strings = useStrings().MembershipConfiguration;

  return (
    <MembershipConfigurationView
      {...props}
    />
  );
};
