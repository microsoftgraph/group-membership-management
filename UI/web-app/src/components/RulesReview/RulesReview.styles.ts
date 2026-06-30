// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { RulesReviewStyleProps, RulesReviewStyles } from './RulesReview.types';

export const getStyles = (props: RulesReviewStyleProps): RulesReviewStyles => {
  const { className, theme } = props;
  return {
    root: [className, {}],
    header: {
      marginBottom: 12,
    },
    title: {
      color: theme.palette.themePrimary,
      textTransform: 'uppercase',
      fontWeight: 600,
      fontSize: 14,
      letterSpacing: '0.5px',
    },
    description: {
      color: theme.palette.neutralSecondary,
      fontSize: 13,
      marginTop: 2,
    },
    details: {
      marginTop: 16,
      paddingTop: 18,
      paddingBottom: 18,
      paddingLeft: 22,
      paddingRight: 22,
      borderRadius: 10,
      backgroundColor: theme.palette.white,
    },
  };
};
