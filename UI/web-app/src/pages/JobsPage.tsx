// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';

import { JobsList } from '../components/JobsList/JobsList';
import { Page } from '../components/Page';
import { PageHeader } from '../components/PageHeader';
import { WelcomeName } from '../components/WelcomeName';
import { Banner } from '../components/Banner';
import { Stack } from '@fluentui/react';
import { PageSection } from '../components/PageSection';

export const JobsPage: React.FunctionComponent = () => {
  return (
    <Page>
      <PageHeader backButtonHidden>
        <Stack horizontalAlign="space-between" horizontal style={{padding: '19px 0px 19px 36px'}}>
          <WelcomeName />
          <Banner />
        </Stack>
      </PageHeader>
      <PageSection>
        <JobsList />
      </PageSection>
    </Page>
  );
};
