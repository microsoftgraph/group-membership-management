// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { Page } from '../components/Page';
import { JobsList } from '../components/JobsList/JobsList';

export const JobsPage: React.FunctionComponent = () => {

  return (
    <Page>
      <JobsList/>
    </Page>
  );
};