// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface ConfiguredAttributeApprovalRow {
  attribute: string;
  values: string[];
}

export interface ConfiguredAttributeApprovalConfiguration {
  enabled: boolean;
  schemaVersion: number;
  attributes: ConfiguredAttributeApprovalRow[];
}

export interface ConfiguredAttributeApprovalError {
  status: number;
  message: string;
}

export interface IConfiguredAttributeApprovalApi {
  fetchConfiguration(): Promise<ConfiguredAttributeApprovalConfiguration>;
  saveConfiguration(configuration: ConfiguredAttributeApprovalConfiguration): Promise<ConfiguredAttributeApprovalConfiguration>;
  }
