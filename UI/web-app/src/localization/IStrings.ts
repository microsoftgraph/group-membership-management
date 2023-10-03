// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type IStrings = {
  emptyList: string;
  loading: string;
  addOwner204Message: string;
  addOwner400Message: string;
  addOwner403Message: string;
  addOwnerErrorMessage: string;
  bannerMessage: string;
  okButton: string;
  groupIdHeader: string;
  groupIdPlaceHolder: string;
  addOwnerButton: string;
  membershipManagement: string;
  learnMembershipManagement: string;
  JobDetails: {
    labels: {
      pageTitle: string;
      sectionTitle: string;
      lastModifiedby: string;
      groupLinks: string;
      destination: string;
      type: string;
      name: string;
      ID: string;
      configuration: string;
      startDate: string;
      endDate: string;
      lastRun: string;
      nextRun: string;
      frequency: string;
      frequencyDescription: string;
      requestor: string;
      increaseThreshold: string;
      decreaseThreshold: string;
      thresholdViolations: string;
      sourceParts: string;
      membershipStatus: string;
      sync: string;
      enabled: string;
      disabled: string;
    };
    descriptions: {
      lastModifiedby: string;
      startDate: string;
      endDate: string;
      type: string;
      id: string;
      lastRun: string;
      nextRun: string;
      frequency: string;
      requestor: string;
      increaseThreshold: string;
      decreaseThreshold: string;
      thresholdViolations: string;
    };
    MessageBar: {
      dismissButtonAriaLabel: string;
    };
    openInAzure: string;
    viewDetails: string;
    editButton: string;
  };
  JobsList: {
    listOfMemberships: string;
    ShimmeredDetailsList: {
      toggleSelection: string;
      toggleAllSelection: string;
      selectRow: string;
      ariaLabelForShimmer: string;
      ariaLabelForGrid: string;
      columnNames: {
        name: string;
        type: string;
        lastRun: string;
        nextRun: string;
        status: string;
        actionRequired: string;
      };
    };
    MessageBar: {
      dismissButtonAriaLabel: string;
    };
    PagingBar: {
      previousPage: string;
      nextPage: string;
      page: string;
      of: string;
      display: string;
      items: string;
    };
    JobsListFilter: {
      filters: {
        ID: {
          label: string;
          placeholder: string;
          validationErrorMessage: string;
        };
        status: {
          label: string;
          options: {
            all: string;
            enabled: string;
            disabled: string;
          };
        };
        actionRequired: {
          label: string;
          options: {
            all: string;
            thresholdExceeded: string;
            customerPaused: string;
            membershipDataNotFound: string;
            destinationGroupNotFound: string;
            notOwnerOfDestinationGroup: string;
            securityGroupNotFound: string;
          };
        };
      };
      filterButtonText: string;
      clearButtonTooltip: string;
    };
    NoResults: string;
  };
  welcome: string;
  back: string;
  version: string;
};
