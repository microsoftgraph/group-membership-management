import { FontWeights } from '@fluentui/react';
import { IJobsListStyleProps, IJobsListStyles } from './JobsList.types';

export const getStyles = (props: IJobsListStyleProps): IJobsListStyles => {
  const { className, theme } = props;

  return {
    root: [
      {
        
      },
      className
    ]
  };
};
