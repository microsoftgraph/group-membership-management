// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IEndpointsListStyleProps, type IEndpointsListStyles } from './EndpointsList.types';

export const getStyles = (props: IEndpointsListStyleProps): IEndpointsListStyles => {
  const { className } = props;

  return {
    root: [{}, className],
    endpointsContainer: {
      display: 'flex',
      flexDirection: 'column',
      gap: 16,
    },
    itemTitle: {
      fontSize: 14,
      fontWeight: 600,
      marginTop: 10,
    },
    itemData: {
      paddingTop: 10,
      fontSize: 14,
    },
    outlookWarning: {
      width: 'fit-content',
      display: 'flex',
      alignItems: 'center'
    },
    outlookContainer: {
      display: 'flex',
      flexDirection: 'row',
      gap: 70
    },
    yammerContainer: {
      height: '38px',
      display: 'flex',
      alignItems: 'center',
      marginLeft: '4px'
    }
  };
};
