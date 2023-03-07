// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
import React from "react";
import { DetailsList, DetailsListLayoutMode, IColumn } from '@fluentui/react/lib/DetailsList';
import { IJob } from "../interfaces/IJob.interfaces";

export interface IDetailsListBasicExampleProps {
  jobs: IJob[];
}

export default class Job extends React.Component<IDetailsListBasicExampleProps> {
  private _columns: IColumn[];

  constructor(props:IDetailsListBasicExampleProps) {
    super(props);

    this._columns = [
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
  }

  public render(): JSX.Element {
      return(
        <div>
        <br />
          <DetailsList
            items={this.props.jobs}
            columns={this._columns}
            setKey="set"
            layoutMode={DetailsListLayoutMode.justified}
            selectionPreservedOnEmptyClick={true}
            ariaLabelForSelectionColumn="Toggle selection"
            ariaLabelForSelectAllCheckbox="Toggle selection for all items"
            checkButtonAriaLabel="select row"
        />
        <br />
          </div>
      );
  }
}