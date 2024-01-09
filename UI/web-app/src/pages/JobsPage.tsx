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
import { useSelector } from 'react-redux';
import { selectDashboardUrl } from '../store/settings.slice';

export const JobsPage: React.FunctionComponent = () => {
  const dashboardUrl = useSelector(selectDashboardUrl);
  
  return (
    <Page>
      <PageHeader backButtonHidden>
        <Stack horizontalAlign="space-between" horizontal style={{padding: '19px 0px 19px 36px'}}>
          <WelcomeName />
          {dashboardUrl ? <Banner /> : null}
        </Stack>
      </PageHeader>
      <PageSection>
        <JobsList />
      </PageSection>
    </Page>
  );
};
