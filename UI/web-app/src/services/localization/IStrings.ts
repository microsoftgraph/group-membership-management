// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type IStrings = {
  emptyList: string;
  loading: string;
  addOwner204Message: string;
  addOwner400Message: string;
  addOwner403Message: string;
  addOwnerErrorMessage: string;
  okButton: string;
  groupIdHeader: string;
  groupIdPlaceHolder: string;
  addOwnerButton: string;
  membershipManagement: string;
  learnMembershipManagement: string;
  permissionDenied: string;
  HROnboarding: {
    orgLeader: string;
    orgLeaderPlaceHolder: string;
    orgLeaderInfo: string;
    depth: string;
    depthPlaceHolder: string;
    depthInfo: string;
    incrementButtonAriaLabel: string;
    decrementButtonAriaLabel: string;
    filter: string;
    filterPlaceHolder: string;
    filterInfo: string;
    includeOrg: string;
    includeFilter: string;
    includeLeader: string;
    group: string;
    ungroup: string;
    attributeTitle: string;
    addAttribute: string;
    attribute: string;
    equalityOperator: string;
    attributeValue: string;
    orAndOperator: string;
    attributeInfo: string;
    equalityOperatorInfo: string;
    attributeValueInfo: string;
    orAndOperatorInfo: string;
    missingAttributeErrorMessage: string;
    customOrgLeaderMissingErrorMessage: string;
    orgLeaderMissingErrorMessage: string;
    source: string;
    invalidInputErrorMessage: string;
  },
  Components: {
    AppHeader: {
      title: string;
      settings: string;
    },
    Banner: {
      bannerMessageStart: string;
      clickHere: string;
      bannerMessageEnd: string;
      expandBanner: string;
    },
    HyperlinkSetting: {
      address: string;
      addHyperlink: string;
      invalidUrl: string;
    },
    GroupQuerySource: {
      searchGroupSuggestedText: string;
      noResultsFoundText: string;
      loadingText: string;
      selectionAriaLabel: string;
      removeButtonAriaLabel: string;
      validQuery: string;
      invalidQuery: string;
      searchGroupName: string;
    }
  },
  AdminConfig: {
    labels: {
      pageTitle: string;
      saveButton: string;
      saveSuccess: string;
    },
    HyperlinkSettings: {
      labels: {
        hyperlinks: string;
        description: string;
      },
      dashboardLink: {
        title: string;
        description: string;
      },
      outlookWarningLink: {
        title: string;
        description: string;
      }
      privacyPolicyLink: {
        title: string;
        description: string;
      },
    },
    CustomSourceSettings: {
      labels: {
        customSource: string;
        sourceDescription: string;
        sourceCustomLabelInput: string;
        listOfAttributes: string;
        listOfAttributesDescription: string;
        attributeColumn: string;
        customLabelColumn: string;
        customLabelInputPlaceHolder: string;
      },
    },
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
      noThresholdSet: string;
      thresholdViolations: string;
      sourceParts: string;
      membershipStatus: string;
      sync: string;
      enabled: string;
      disabled: string;
      pendingReview: string;
      pendingReviewDescription: string;
      pendingReviewInstructions: string;
      approve: string;
      reject: string;
      submissionRejected: string;
      removeGMM: string;
      removeGMMWarning: string;
      removeGMMConfirmation: string;
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
      removeGMMError: string;
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
      pageNumberAriaLabel: string;
      pageSizeAriaLabel: string;
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
            pendingReview: string;
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
    addSyncButton: string;
    bulkAddSyncsButton: string;
    labels: {
      abandonOnboarding: string;
      abandonOnboardingDescription: string;
      alreadyOnboardedWarning: string;
      confirmAbandon: string;
      pageTitle: string;
      step1title: string;
      step1description: string;
      selectDestinationType: string;
      selectDestinationTypePlaceholder: string;
      searchDestination: string;
      searchGroupSuggestedText: string;
      noResultsFound: string;
      appsUsed: string;
      outlookWarning: string;
      appIdNotOwnerWarning: string;
      userNotOwnerWarning: string;
      step2title: string;
      step2description: string;
      advancedQuery: string;
      advancedView: string;
      query: string;
      validQuery: string;
      invalidQuery: string;
      invalidGroups: string;
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
      sourcePart: string;
      noThresholdSet: string;
      savingSyncJob: string;
      updatingSyncJob: string;
      group: string;
      destinationPickerSuggestionsHeaderText: string;
      expandCollapse: string;
      sourceType: string;
      addSourcePart: string;
      excludeSourcePart: string;
      deleteLastSourcePartWarning: string;
      errorOnSchema: string;
      searchGroupName: string;
      HR: string;
      groupMembership: string;
      groupOwnership: string;
      placeMembership: string;
      clickHere: string;
      requestor: string;
    }
  };
  copy: string;
  remove: string;
  delete: string;
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
  or: string;
  and: string;
  privacyPolicy: string;
  hoursAgo: string;
  hoursLeft: string;
  pendingInitialSync: string;
};
