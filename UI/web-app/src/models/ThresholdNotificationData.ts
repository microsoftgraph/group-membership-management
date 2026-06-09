// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface ThresholdNotificationData {
  notificationId?: string;
  changeQuantityForAdditions: number;
  changePercentageForAdditions: number;
  thresholdPercentageForAdditions: number;
  changeQuantityForRemovals: number;
  changePercentageForRemovals: number;
  thresholdPercentageForRemovals: number;
  purgeDate?: string;
}
