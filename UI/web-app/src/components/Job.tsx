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
    { key: 'column1', name: 'Identifier', fieldName: 'targetGroupId', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column2', name: 'ObjectType', fieldName: 'targetGroupType', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column3', name: 'InitialOnboardingTime', fieldName: 'startDate', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column4', name: 'LastSuccessfulStartTime', fieldName: 'lastSuccessfulStartTime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column5', name: 'LastSuccessfulRunTime', fieldName: 'lastSuccessfulRunTime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column6', name: 'EstimatedNextRunTime', fieldName: 'estimatedNextRunTime', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column7', name: 'ThresholdIncrease', fieldName: 'thresholdPercentageForAdditions', minWidth: 100, maxWidth: 200, isResizable: true },
    { key: 'column8', name: 'ThresholdDecrease', fieldName: 'thresholdPercentageForRemovals', minWidth: 100, maxWidth: 200, isResizable: true }
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