// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';

import { JobsList } from '../components/JobsList/JobsList';
import { Page } from '../components/Page';
import { PageHeader } from '../components/PageHeader';
import { WelcomeName } from '../components/WelcomeName';

export const JobsPage: React.FunctionComponent = () => {
  return (
    <Page>
      <PageHeader backButtonHidden>
        <WelcomeName />
      </PageHeader>
      <JobsList />
    </Page>
  );
};
