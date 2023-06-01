// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { DetailsList, DetailsListLayoutMode, IColumn } from '@fluentui/react/lib/DetailsList';
import { useTranslation } from 'react-i18next';
import '../../i18n/config';
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
import {ReportHackedIcon } from '@fluentui/react-icons-mdl2';

const getClassNames = classNamesFunction<
  IJobsListStyleProps,
  IJobsListStyles
>();

const buildColumns = (): IColumn[] => {
  const columns: IColumn[] = [];
  columns.push({
    key: "type",
    name: "Type",
    fieldName: "targetGroupType",
    minWidth: 100,
    maxWidth: 100,
    isResizable: false
  });
  columns.push({
    key: "lastRun",
    name: "Last Run",
    fieldName: "lastSuccessfulRunTime",
    minWidth: 100,
    maxWidth: 100,
    isResizable: false
  });
  columns.push({
    key: "nextRun",
    name: "Next Run",
    fieldName: "estimatedNextRunTime",
    minWidth: 100,
    maxWidth: 100,
    isResizable: false
  });
  columns.push({
    key: "status",
    name: "Status",
    fieldName: "enabledOrNot",
    minWidth: 75,
    maxWidth: 75,
    isResizable: false
  });
  columns.push({
    key: "actionRequired",
    name: "Action Required",
    fieldName: "actionRequired",
    minWidth: 200,
    maxWidth: 200,
    isResizable: false
  });
  return columns;
}

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
  var columns = buildColumns();

  const dispatch = useDispatch<AppDispatch>()
  const jobs = useSelector(selectAllJobs)
  const navigate = useNavigate()

  useEffect(() => {
    if (!jobs){
      dispatch(fetchJobs())
    }
  }, [dispatch, jobs]);

  const onItemClicked = (item?: any, index?: number, ev?: React.FocusEvent<HTMLElement>): void => {
    navigate('/JobDetailsPage', { replace: false, state: {item: item} })
  }

  const _renderItemColumn = (item?: any, index?: number, column?: IColumn): JSX.Element => {
    const fieldContent = item[column?.fieldName as keyof any] as string;

    switch (column?.key) {
      case 'lastRun':
      case 'nextRun':
        const spaceIndex = fieldContent.indexOf(" ");
        const isEmpty = fieldContent === "";
        const lastOrNextRunDate = isEmpty ? "-" : fieldContent.substring(0, spaceIndex);
        const hoursAgoOrHoursLeft = isEmpty ? "" : fieldContent.substring(spaceIndex + 1);

        return (
          <div>
            <div>{lastOrNextRunDate}</div>
            <div>{hoursAgoOrHoursLeft}</div>
          </div>
        );

      case 'status':
        return (
          <div>
            {fieldContent === "Disabled" ? (
              <div className={classNames.disabled}> {fieldContent}</div>
            ) : (
              <div className={classNames.enabled}> {fieldContent}</div>
            )}
          </div>
        );

        case 'actionRequired':
        return (
          <div>
            {fieldContent ? (
              <div className={classNames.actionRequired}>  <ReportHackedIcon /> {fieldContent}</div>
            ) : (
              <div className={classNames.actionRequired}> {fieldContent}</div>
            )}
          </div>
        );

      default:
        return <span>{fieldContent}</span>;
    }
  };

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
              onRenderItemColumn={_renderItemColumn}
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