// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';

import { Owner } from '../components/Owner/Owner';
import { Page } from '../components/Page';
import { PageHeader } from '../components/PageHeader';

export const OwnerPage: React.FunctionComponent = () => {
  return (
    <Page>
      <PageHeader />
      <Owner />
    </Page>
  );
};
