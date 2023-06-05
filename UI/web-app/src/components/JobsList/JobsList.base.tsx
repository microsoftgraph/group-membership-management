// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  DetailsListLayoutMode,
  IColumn,
} from "@fluentui/react/lib/DetailsList";
import { useTranslation } from "react-i18next";
import "../../i18n/config";
import { useEffect } from "react";
import { useSelector, useDispatch } from "react-redux";
import { fetchJobs } from "../../store/jobs.api";
import {
  selectAllJobs,
  selectGetJobsError,
  setGetJobsError,
} from "../../store/jobs.slice";
import { AppDispatch } from "../../store";

import { useNavigate } from "react-router-dom";
import {
  classNamesFunction,
  IProcessedStyleSet,
  MessageBar,
  MessageBarType,
  IconButton,
  IIconProps,
} from "@fluentui/react";
import { useTheme } from "@fluentui/react/lib/Theme";
import { ShimmeredDetailsList } from "@fluentui/react/lib/ShimmeredDetailsList";
import {
  IJobsListProps,
  IJobsListStyleProps,
  IJobsListStyles,
} from "./JobsList.types";
import { ReportHackedIcon } from "@fluentui/react-icons-mdl2";

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
  const dispatch = useDispatch<AppDispatch>();
  const jobs = useSelector(selectAllJobs);
  const navigate = useNavigate();

  const columns = [
    {
      key: "type",
      name: t("JobsList.ShimmeredDetailsList.columnNames.destinationType"),
      fieldName: "targetGroupType",
      minWidth: 100,
      maxWidth: 100,
      isResizable: false,
    },
    {
      key: "lastRun",
      name: t("JobsList.ShimmeredDetailsList.columnNames.lastRun"),
      fieldName: "lastSuccessfulRunTime",
      minWidth: 100,
      maxWidth: 100,
      isResizable: false,
    },
    {
      key: "nextRun",
      name: t("JobsList.ShimmeredDetailsList.columnNames.nextRun"),
      fieldName: "estimatedNextRunTime",
      minWidth: 100,
      maxWidth: 100,
      isResizable: false,
    },
    {
      key: "status",
      name: t("JobsList.ShimmeredDetailsList.columnNames.status"),
      fieldName: "enabledOrNot",
      minWidth: 75,
      maxWidth: 75,
      isResizable: false,
    },
    {
      key: "actionRequired",
      name: t("JobsList.ShimmeredDetailsList.columnNames.actionRequired"),
      fieldName: "actionRequired",
      minWidth: 200,
      maxWidth: 200,
      isResizable: false,
    },
  ];

  const error = useSelector(selectGetJobsError);

  const onDismiss = (): void => {
    dispatch(setGetJobsError());
  };

  useEffect(() => {
    if (!jobs) {
      dispatch(fetchJobs());
    }
  }, [dispatch, jobs]);

  const onItemClicked = (
    item?: any,
    index?: number,
    ev?: React.FocusEvent<HTMLElement>
  ): void => {
    navigate("/JobDetailsPage", { replace: false, state: { item: item } });
  };

  const onRefreshClicked = (
    item?: any,
    index?: number,
    ev?: React.FocusEvent<HTMLElement>
  ): void => {
    dispatch(fetchJobs());
  };

  const refreshIcon: IIconProps = { iconName: "Refresh" };

  const _renderItemColumn = (
    item?: any,
    index?: number,
    column?: IColumn
  ): JSX.Element => {
    const fieldContent = item[column?.fieldName as keyof any] as string;

    switch (column?.key) {
      case "lastRun":
      case "nextRun":
        const spaceIndex = fieldContent.indexOf(" ");
        const isEmpty = fieldContent === "";
        const lastOrNextRunDate = isEmpty
          ? "-"
          : fieldContent.substring(0, spaceIndex);
        const hoursAgoOrHoursLeft = isEmpty
          ? ""
          : fieldContent.substring(spaceIndex + 1);

        return (
          <div>
            <div>{lastOrNextRunDate}</div>
            <div>{hoursAgoOrHoursLeft}</div>
          </div>
        );

      case "status":
        return (
          <div>
            {fieldContent === "Disabled" ? (
              <div className={classNames.disabled}> {fieldContent}</div>
            ) : (
              <div className={classNames.enabled}> {fieldContent}</div>
            )}
          </div>
        );

      case "actionRequired":
        return (
          <div>
            {fieldContent ? (
              <div className={classNames.actionRequired}>
                {" "}
                <ReportHackedIcon /> {fieldContent}
              </div>
            ) : (
              <div className={classNames.actionRequired}> {fieldContent}</div>
            )}
          </div>
        );

      default:
        return <span>{fieldContent}</span>;
    }
  };

  return (
    <div className={classNames.root}>
      {error && (
        <MessageBar
          messageBarType={MessageBarType.error}
          isMultiline={false}
          onDismiss={onDismiss}
          dismissButtonAriaLabel={
            t("JobsList.MessageBar.dismissButtonAriaLabel") as
              | string
              | undefined
          }
        >
          {error}
        </MessageBar>
      )}

      <div className={classNames.tabContent}>
        <ShimmeredDetailsList
          setKey="set"
          items={jobs || []}
          columns={columns}
          enableShimmer={!jobs || jobs.length === 0}
          layoutMode={DetailsListLayoutMode.justified}
          ariaLabelForShimmer="Content is being fetched"
          ariaLabelForGrid="Item details"
          selectionPreservedOnEmptyClick={true}
          ariaLabelForSelectionColumn={
            t("JobsList.ShimmeredDetailsList.toggleSelection") as
              | string
              | undefined
          }
          ariaLabelForSelectAllCheckbox={
            t("JobsList.ShimmeredDetailsList.toggleAllSelection") as
              | string
              | undefined
          }
          checkButtonAriaLabel={
            t("JobsList.ShimmeredDetailsList.selectRow") as string | undefined
          }
          onActiveItemChanged={onItemClicked}
          onRenderItemColumn={_renderItemColumn}
        />
      </div>

      <div className={classNames.tabContent}>
        <div className={classNames.refresh}>
          <IconButton
            iconProps={refreshIcon}
            title="Refresh"
            ariaLabel="Refresh"
            onClick={onRefreshClicked}
          />
        </div>
      </div>
    </div>
  );
};
