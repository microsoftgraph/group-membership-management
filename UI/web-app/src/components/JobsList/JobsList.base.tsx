// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { DetailsList, DetailsListLayoutMode } from '@fluentui/react/lib/DetailsList';
import { useTranslation } from 'react-i18next';
import { useEffect } from 'react'
import { useSelector, useDispatch } from 'react-redux'
import { fetchJobs } from '../../store/jobs.api'
import { selectAllJobs } from '../../store/jobs.slice'
import { AppDispatch } from "../../store";
import Loader from '../Loader';
import { useNavigate } from 'react-router-dom';
import { classNamesFunction, IProcessedStyleSet } from "@fluentui/react";
import { useTheme } from "@fluentui/react/lib/Theme";
import {
  IJobsListProps,
  IJobsListStyleProps,
  IJobsListStyles,
} from "./JobsList.types";

  
const getClassNames = classNamesFunction<
  IJobsListStyleProps,
  IJobsListStyles
>();

export const JobsListBase: React.FunctionComponent<IJobsListProps> = (
  props: IJobsListProps
) => {
  const { className, styles } = props;

  const classNames: IProcessedStyleSet<IJobsListStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  const { t } = useTranslation();
  var toggleSelection = t('toggleSelection');
  var toggleAllSelection = t('toggleAllSelection');
  var selectRow = t('selectRow');

  var columns = [
    { key: 'column1', name: 'Identifier', fieldName: 'targetGroupId', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column2', name: 'ObjectType', fieldName: 'targetGroupType', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column3', name: 'InitialOnboardingTime', fieldName: 'startDate', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column4', name: 'Status', fieldName: 'status', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column5', name: 'LastSuccessfulStartTime', fieldName: 'lastSuccessfulStartTime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column6', name: 'LastSuccessfulRunTime', fieldName: 'lastSuccessfulRunTime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column7', name: 'EstimatedNextRunTime', fieldName: 'estimatedNextRunTime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column8', name: 'ThresholdIncrease', fieldName: 'thresholdPercentageForAdditions', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column9', name: 'ThresholdDecrease', fieldName: 'thresholdPercentageForRemovals', minWidth: 100, maxWidth: 200, isResizable: true }
  ];

  const dispatch = useDispatch<AppDispatch>()
  const jobs = useSelector(selectAllJobs)
  const navigate = useNavigate()
  
  useEffect(() => {
    if (!jobs){
      dispatch(fetchJobs())
    }
}, [dispatch])

const onItemClicked = (item?: any, index?: number, ev?: React.FocusEvent<HTMLElement>): void => {
  navigate('/JobDetailsPage', { replace: false, state: {item: item} })
} 

if (jobs && jobs.length > 0) {
  return (
    <div className={classNames.root}>
          <DetailsList
            items={jobs}
            columns={columns}
            setKey="set"
            layoutMode={DetailsListLayoutMode.justified}
            selectionPreservedOnEmptyClick={true}
            ariaLabelForSelectionColumn={toggleSelection}
            ariaLabelForSelectAllCheckbox={toggleAllSelection}
            checkButtonAriaLabel={selectRow}
            onActiveItemChanged={onItemClicked} 
        />
      </div>
  );
}
else {
  return(
    <div>
      <Loader />
    </div>
  );
}
  
}