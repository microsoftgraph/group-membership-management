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
    provideOrgLeader: string;
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
    attributeDescription: string;
    attributeNameHeader: string;
    attributeDescriptionHeader: string;
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
    valueComboBoxOptionCodeLabel: string;
    attributeDisabledErrorMessage: string;
    supportPlaceHolder: string;
    all: string;
    level: string;
    levelsPlural: string;
    down: string;
    up: string;
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
    Errors:{
      forbidden: string;
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
    Operations: {
      labels: {
        operations: string;
        description: string;
        title: string;
      };
      buttons: {
        stop: string;
        stopping: string;
        reset: string;
        resetting: string;
        start: string;
        starting: string;
      };
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
        valuesColumn: string;
        valuesDropdownSpinnerLabel: string;
        valuesDropdownPlaceholder: string;
        valuesDropdownTitle: string;
        descriptionColumn: string;
        enabledColumn: string;
        enabledToggleTitle: string;
        descriptionPlaceHolder: string;
      },
    },
    GeneralSettings: {
      labels: {
        general: string;
        reviewOwnSubmissionTitle: string;
        reviewOwnSubmissionDescription: string;
        createGroupTitle: string;
        createGroupDescription: string;
        businessJustificationTitle: string;
        businessJustificationDescription: string;
      }
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
      requestedBy: string;
      requestedOnBehalfOf: string;
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
      businessJustification: string;
      approve: string;
      reject: string;
      submissionRejected: string;
      removeGMM: string;
      removeGMMWarning: string;
      removeGMMConfirmation: string;
    };
    descriptions: {
      requestedBy: string;
      requestedOnBehalfOf: string;
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
      forbidden: string;
      internalError: string;
      removeGMMError: string;
    };
    Panel: {
      dismissButtonAriaLabel: string;
      changeTimeColumnLabel: string;
      changedByColumnLabel: string;
      changeReasonColumnLabel: string;
      changeDetailsColumnLabel: string;
      viewDetails: string;
      history: string;
      configurationPivotHeader: string;
      onboardingRequest: string;
      statusUpdate: string;
      update: string;
      submissionApproved: string;
      submissionRejected: string;
      businessJustification: string;
    };
    notFound: string;
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
        email: string;
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
            developerPaused: string;
            membershipDataNotFound: string;
            destinationGroupNotFound: string;
            notOwnerOfDestinationGroup: string;
            securityGroupNotFound: string;
            pendingReview: string;
            submissionRejected: string;
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
      selectOrCreateGroup: string;
      selectDestinationType: string;
      selectDestinationTypePlaceholder: string;
      selectDestination: string;
      createNewGroup: string;
      searchDestination: string;
      searchGroupSuggestedText: string;
      noResultsFound: string;
      appsUsed: string;
      entraSecurityGroup: string;
      outlookWarning: string;
      appIdNotOwnerWarning: string;
      userNotOwnerWarning: string;
      groupDescription: string;
      channelDescription: string;
      step2title: string;
      step2description: string;
      advancedQuery: string;
      advancedView: string;
      query: string;
      validQuery: string;
      invalidQuery: string;
      invalidGroups: string;
      invalidSqlFilters: string;
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
      preventAutomaticSyncInfo: string;
      preventAutomaticSyncWarning: string;
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
      expand: string;
      collapse: string;
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
      requestorInfo: string;
      businessJustificationSubtitle: string;
      businessJustificationPrompt: string;
      businessJustificationPlaceholder: string;
      requestedBy: string;
      requestedOnBehalfOf: string;
    },
    CreateGroup: {
      createNewGroup: string;
      groupName: string;
      groupNamePlaceholder: string;
      groupAlias: string;
      groupAliasPlaceholder: string;
      creating: string;
      created: string;
      createGroup: string;
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
