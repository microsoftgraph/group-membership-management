// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
import { DetailsList, DetailsListLayoutMode } from '@fluentui/react/lib/DetailsList';
import { IJob } from "../interfaces/IJob.interfaces";
import { useTranslation } from 'react-i18next';

export interface IDetailsListBasicExampleProps {
  jobs: IJob[];
}

export function Job(props:IDetailsListBasicExampleProps) {

  const { t } = useTranslation();
  var toggleSelection = t('toggleSelection');
  var toggleAllSelection = t('toggleAllSelection');
  var selectRow = t('selectRow');

  var columns = [
    { key: 'column1', name: 'Identifier', fieldName: 'Identifier', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column2', name: 'ObjectType', fieldName: 'ObjectType', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column3', name: 'InitialOnboardingTime', fieldName: 'InitialOnboardingTime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column4', name: 'LastAttemptedRunTime', fieldName: 'LastAttemptedRunTime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column5', name: 'LastSuccessfulRuntime', fieldName: 'LastSuccessfulRuntime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column6', name: 'EstimatedNextRuntime', fieldName: 'EstimatedNextRuntime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column7', name: 'Status', fieldName: 'Status', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column8', name: 'ThresholdIncrease', fieldName: 'ThresholdIncrease', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column9', name: 'ThresholdDecrease', fieldName: 'ThresholdDecrease', minWidth: 100, maxWidth: 200, isResizable: true },
  ];

  return (
    <div>
        <br />
          <DetailsList
            items={props.jobs}
            columns={columns}
            setKey="set"
            layoutMode={DetailsListLayoutMode.justified}
            selectionPreservedOnEmptyClick={true}
            ariaLabelForSelectionColumn={toggleSelection}
            ariaLabelForSelectAllCheckbox={toggleAllSelection}
            checkButtonAriaLabel={selectRow}
        />
        <br />
      </div>
  );
}