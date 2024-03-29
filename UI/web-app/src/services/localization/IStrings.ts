// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type IStrings = {
  emptyList: string;
  loading: string;
  addOwner204Message: string;
  addOwner400Message: string;
  addOwner403Message: string;
  addOwnerErrorMessage: string;
  bannerMessageStart: string;
  clickHere: string;
  bannerMessageEnd: string;
  okButton: string;
  groupIdHeader: string;
  groupIdPlaceHolder: string;
  addOwnerButton: string;
  membershipManagement: string;
  learnMembershipManagement: string;
  AdminConfig: {
    labels: {
      pageTitle: string;
      hyperlinks: string;
      description: string;
      saveButton: string;
      saveSuccess: string;
    },
    hyperlinkContainer: {
      address: string;
      addHyperlink: string;
      dashboardTitle: string;
      dashboardDescription: string;
      invalidUrl: string;
    }
  },
  Authentication: {
    loginFailed: string;
  },
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
    Errors:{
      jobInProgress: string;
      notGroupOwner: string;
      internalError: string;
    }
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
        destinationType: {
          label: string;
          options: {
            all: string;
            channel: string;
            group: string;
          };
        };
        destinationName: {
          label: string;
          placeholder: string;
        };
        ownerPeoplePicker: {
          label: string;
          suggestionsHeaderText:string;
          noResultsFoundText: string;
          loadingText: string;
          selectionAriaLabel: string;
          removeButtonAriaLabel: string;
        };
      };
      filterButtonText: string;
      clearButtonTooltip: string;
    };
    NoResults: string;
  };
  ManageMembership: {
    manageMembershipButton: string;
    labels: {
      abandonOnboarding: string;
      abandonOnboardingDescription: string;
      confirmAbandon: string;
      pageTitle: string;
      step1title: string;
      step1description: string;
      selectDestinationType: string;
      selectDestinationTypePlaceholder: string;
      searchDestination: string;
      searchDestinationPlaceholder: string;
      noResultsFound: string;
      appsUsed: string;
      outlookWarning: string;
      ownershipWarning: string;
      step2title: string;
      step2description: string;
      advancedQuery: string;
      advancedView: string;
      query: string;
      validQuery: string;
      invalidQuery: string;
      validateQuery: string;
      step3title: string;
      step3description: string;
      selectStartDate: string;
      ASAP: string;
      requestedDate: string;
      selectRequestedStartDate: string;
      from: string;
      selectFrequency: string;
      hrs: string;
      frequency: string;
      preventAutomaticSync: string;
      increase: string;
      decrease: string;
      step4title: string;
      step4description: string;
      objectId: string;
      sourceParts: string;
      noThresholdSet: string;
      savingSyncJob: string;
      group: string;
    }
  };
  edit: string;
  submit: string;
  needHelp: string;
  next: string;
  close: string;
  cancel: string;
  learnMore: string;
  errorItemNotFound: string;
  welcome: string;
  back: string;
  backToDashboard: string;
  version: string;
  yes: string;
  no: string;
};
