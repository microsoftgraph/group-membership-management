// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react'

import { JobsList } from '../components/JobsList/JobsList'
import { Page } from '../components/Page'

export const JobsPage: React.FunctionComponent = () => {
  return (
    <Page>
      <JobsList/>
    </Page>
  )
}
