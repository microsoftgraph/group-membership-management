// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect } from 'react';
import {
  IProcessedStyleSet,
  classNamesFunction,
  format,
  useTheme,
} from '@fluentui/react';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { INotFoundProps, INotFoundStyleProps, INotFoundStyles } from './NotFound.types';
import { useStrings } from '../../store/hooks';
import { useLocation, useNavigate } from 'react-router-dom';
import { setPagingBarVisible } from '../../store/pagingBar.slice';
import { useDispatch } from 'react-redux';
import { AppDispatch } from '../../store';
import { Job } from '../../models/Job';

const getClassNames = classNamesFunction<
  INotFoundStyleProps,
  INotFoundStyles
>();

export const NotFoundBase: React.FunctionComponent<INotFoundProps> = (
  props: INotFoundProps
) => {
  const { className, styles } = props;

  const classNames: IProcessedStyleSet<INotFoundStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const dispatch = useDispatch<AppDispatch>();
  const location = useLocation();
  const job: Job = location.state.item;
  const strings = useStrings();
  const navigate = useNavigate();

  const handleBackToDashboardButtonClick = () => {
    navigate('/');
  };

  useEffect(() => {
    dispatch(setPagingBarVisible(false));
  }, [dispatch]);

  return (
    <Page>
      <PageHeader onBackToDashboardButtonClick={handleBackToDashboardButtonClick} />
        <div className={classNames.root}>
          <div className={classNames.container}>
            {format(strings.JobDetails.notFound, job.targetGroupId)}
          </div>
        </div>
    </Page>
  )
};