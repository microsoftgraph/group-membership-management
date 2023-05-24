// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IJobsListStyleProps, IJobsListStyles } from './JobsList.types';

export const getStyles = (props: IJobsListStyleProps): IJobsListStyles => {
  const { className } = props;

  return {
    root: [
      {

      },
      className
    ],
    enabled: {
      color: 'black',
      backgroundColor: 'lightGreen',
      borderRadius: 50,
      textAlign: 'center',
      height: 20
    },
    disabled: {
      color: 'black',
      backgroundColor: 'lightGrey',
      borderRadius: 50,
      textAlign: 'center',
      height: 20
    },
    actionRequired: {
      color: 'darkRed'
    }
  };
};
