// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IStrings } from '../../../IStrings';

export const strings: IStrings = {
  emptyList: 'There are no GMM managed groups',
  loading: 'Loading',
  addOwner204Message: 'Added Successfully.',
  addOwner400Message: 'GMM is already added as an owner.',
  addOwner403Message: 'You do not have permission to complete this operation.',
  addOwnerErrorMessage:
    'We are having trouble adding GMM as the owner. Please try again later.',
  okButton: 'OK',
  groupIdHeader: 'Enter Group ID',
  groupIdPlaceHolder: 'Group ID',
  addOwnerButton: 'Add GMM as an owner',
  membershipManagement: 'Membership Management',
  learnMembershipManagement:'Learn how Membership Management works in your organization',
  permissionDenied: 'You do not have permission to view this page. Please contact your administrator.',
  HROnboarding: {
    orgLeader: "Provide Org. leader",
    orgLeaderPlaceHolder: "Please enter Org. leader",
    orgLeaderInfo: "Provide Org. leader",
    depth: "Depth",
    depthPlaceHolder: "Please enter Depth",
    depthInfo: "Defines the maximum depth level in the organization hierarchy used to retrieve data. The default value is 0 which means there is no depth limit. If depth 1 is specified, it means only the org leader will be retrieved. If depth 2 is specified, the org leader and everyone directly reporting to the org leader will be retrieved.",
    incrementButtonAriaLabel: "Increase value by 1",
    decrementButtonAriaLabel: "Decrease value by 1",
    filter: "Filter",
    filterPlaceHolder: "Please enter Filter",
    filterInfo: "Provide filter",
    includeOrg: "Do you want this source to be based on organization structure?",
    includeFilter: "Do you want to filter by properties?",
    includeLeader: "Include leader",
    addAttribute: "Add attribute",
    attribute: "Attribute",
    equalityOperator: "Equality operator",
    attributeValue: "Value",
    orAndOperator: "And/Or",
    attributeInfo: "Select the attribute",
    equalityOperatorInfo: "Select the equality operator",
    attributeValueInfo: "Select the attribute value",
    orAndOperatorInfo: "Select And/Or",
    missingAttributeErrorMessage: "Please provide missing info in the previous attribute",
    customOrgLeaderMissingErrorMessage: " doesn't exist in the ",
    source: " source",
    orgLeaderMissingErrorMessage: " doesn't exist in the HR Data source",
    invalidInputErrorMessage: "Invalid input. Please enter only numbers."
  },
  Components: {
    AppHeader: {
      title: 'Group Membership Management',
      settings: 'Settings',
    },
    Banner: {
      bannerMessageStart: "Need help? ",
      clickHere: "Click here",
      bannerMessageEnd: " to learn more about how Membership Management works in your organization.",
      expandBanner: "Expand banner",
    },
    HyperlinkSetting: {
      address: "Address",
      addHyperlink: "Add hyperlink",
      invalidUrl: "Invalid URL",
    },
    GroupQuerySource: {
      searchGroupSuggestedText: "Suggested Groups",
      noResultsFoundText: "No results found",
      loadingText: "Loading",
      selectionAriaLabel: "Selected contacts",
      removeButtonAriaLabel: "Remove",
      validQuery: "Query is valid.",
      invalidQuery: "Failed to parse query. Ensure it is valid JSON.",
      searchGroupName: "Search group name",
    }
  },
  AdminConfig: {
    labels: {
      pageTitle: "Admin Configuration",
      saveButton: "Save",
      saveSuccess: "Saved successfully.",
    },
    HyperlinkSettings: {
      labels: {
        hyperlinks: "Hyperlinks",
        description: "Provide hyperlinks to the following organization specific information so that your users are empowered to leverage XMM to its fullest.",
      },
      dashboardLink: {
        title: "Dashboard",
        description: "This is the link that shows on the top right corner of the dashboard. It takes you to an internal site that has all the details on how to leverage XMM at your organization. This could include FAQs, contact information, SLAs, etc.",
      },
      outlookWarningLink: {
        title: "Destination Instructions",
        description: "This link appears when Outlook is involved in the group selected. It takes you to an internal site to make the proper settings when sending emails to the Outlook group.",
      },
      privacyPolicyLink: {
        title: "Privacy Policy",
        description: "This is the link that shows on the bottom left corner of the dashboard. It takes you to an internal site that has all the details on how XMM handles and stores user data.",
      },
    },
    CustomSourceSettings: {
      labels: {
        customSource: "Custom Source",
        sourceDescription: "Assign a friendly name to the source of your organization's data so that group owners recognize it when defining the source of their membership destinations.",
        sourceCustomLabelInput: "Custom Label",
        listOfAttributes: "List of Attributes",
        listOfAttributesDescription: "Assign a friendly name to each of the attributes in your custom source so that group owners recognize them when defining the source of their membership destinations.",
        attributeColumn: "Attribute",
        customLabelColumn: "Custom Label",
        customLabelInputPlaceHolder: "Enter a custom label",
      },
    },
  },
  Authentication: {
    loginFailed: 'An unexpected error occurred during login.'
  },
  JobDetails: {
    labels: {
      pageTitle: 'Membership Details',
      sectionTitle: 'Membership Details',
      lastModifiedby: 'Last Modified by',
      groupLinks: 'Group Links',
      destination: 'Destination',
      type: 'Type',
      name: 'Name',
      ID: 'ID',
      configuration: 'Configuration',
      startDate: 'Start Date',
      endDate: 'End Date',
      lastRun: 'Last Run',
      nextRun: 'Next Run',
      frequency: 'Frequency',
      frequencyDescription: 'Every {0} hrs',
      requestor: 'Requestor',
      increaseThreshold: 'Increase Threshold',
      decreaseThreshold: 'Decrease Threshold',
      noThresholdSet: 'No threshold configured',
      thresholdViolations: 'Threshold Violations',
      sourceParts: 'Source Parts',
      membershipStatus: 'Membership status',
      sync: 'Sync',
      enabled: 'Enabled',
      disabled: 'Disabled',
      pendingReview: 'Pending Review',
      pendingReviewDescription: 'Your submission is awaiting to be reviewed.',
      pendingReviewInstructions: 'Please review this request and then approve or decline after reviewing the membership configuration.',
      approve: 'Approve',
      reject: 'Reject',
      submissionRejected: 'Submission Rejected',
      removeGMM: 'Remove GMM Management',
      removeGMMWarning: 'Are you sure you want to remove GMM management from this group? You will need to manually remove GMM as a group owner in Microsoft Entra ID.',
      removeGMMConfirmation: 'Confirm (Link opens in new tab)',
    },
    descriptions: {
      lastModifiedby: 'User who made the last change to this job.',
      startDate: 'Date of the onboarding of this job into GMM.',
      endDate: 'Date of the last run of this job.',
      type: 'The destination\'s type. Currently GMM only manages the membership of Group destinations. In the future it will also support Teams Channel membership.',
      id: 'Object ID of the destination group.',
      lastRun: 'Time of the last run of this job.',
      nextRun: 'Next run time for this job.',
      frequency: 'How often this job runs.',
      requestor: 'User who requested this job to be onboarded into GMM.',
      increaseThreshold:
        'If GMM detects large changes in membership, you (the group owner) may want to review these changes.  This setting allows GMM to suspend updates if a threshold you define is exceeded. This value (expressed as a percentage of the current size of the group) limits the number of users that can be added to the target group.  If the number is exceeded, GMM will wait for your approval to continue.',
      decreaseThreshold:
        'If GMM detects large changes in membership, you (the group owner) may want to review these changes.  This setting allows GMM to suspend updates if a threshold you define is exceeded. This value (expressed as a percentage of the current size of the group) limits the number of users that can be removed from the target group.  If the number is exceeded, GMM will wait for your approval to continue.',
      thresholdViolations: 'Number of times a threshold was exceeded.',
    },
    MessageBar: {
      dismissButtonAriaLabel: 'Close',
    },
    Errors:{
      jobInProgress: 'Job is in progress. Please try again later.',
      notGroupOwner: 'You are not an owner of this group.',
      internalError: 'We can\'t process your request at this time. Please try again later',
      removeGMMError: 'Error removing GMM management from this group:',
    },
    openInAzure: 'Open in Azure',
    viewDetails: 'View Details',
    editButton: 'Edit',
  },
  JobsList: {
    listOfMemberships: 'Managed groups',
    ShimmeredDetailsList: {
      toggleSelection: 'Toggle selection',
      toggleAllSelection: 'Toggle selection for all items',
      selectRow: 'select row',
      ariaLabelForShimmer: 'Content is being fetched',
      ariaLabelForGrid: 'Item details',
      columnNames: {
        name: 'Name',
        type: 'Type',
        lastRun: 'Last Run',
        nextRun: 'Next Run',
        status: 'Status',
        actionRequired: 'Action Required',
      },
    },
    MessageBar: {
      dismissButtonAriaLabel: 'Close',
    },
    PagingBar: {
      previousPage: 'Prev',
      nextPage: 'Next',
      page: 'Page',
      of: 'of',
      display: 'Display',
      items: 'Items per page',
      pageNumberAriaLabel: 'Page number',
      pageSizeAriaLabel: 'Items per page',
    },
    JobsListFilter: {
      filters: {
        ID: {
          label: 'ID',
          placeholder: 'Search',
          validationErrorMessage: 'Invalid GUID!',
        },
        status: {
          label: 'Status',
          options: {
            all: 'All',
            enabled: 'Enabled',
            disabled: 'Disabled',
          },
        },
        actionRequired: {
          label: 'Action Required',
          options: {
            all: 'All',
            thresholdExceeded: 'Threshold Exceeded',
            customerPaused: 'Customer Paused',
            membershipDataNotFound: 'No users in the source',
            destinationGroupNotFound: 'Destination Group Not Found',
            notOwnerOfDestinationGroup: 'Not Owner Of Destination Group',
            securityGroupNotFound: 'Security Group Not Found',
            pendingReview: 'Pending Review',
          },
        },
        destinationType: {
          label: 'Destination Type',
          options: {
            all: 'All',
            channel: 'Channel',
            group: 'Group'
          }
        },
        destinationName: {
          label: 'Destination Name',
          placeholder: 'Search',
        },
        ownerPeoplePicker: {
          label: 'Owner',
          suggestionsHeaderText: 'Suggested People',
          noResultsFoundText: 'No results found',
          loadingText: 'Loading',
          selectionAriaLabel: 'Selected contacts',
          removeButtonAriaLabel: 'Remove'
        },
      },
      filterButtonText: 'Filter',
      clearButtonTooltip: 'Clear Filters',
    },
    NoResults: 'No memberships found.',
  },
  ManageMembership: {
    manageMembershipButton: 'Manage Membership',
    addSyncButton: 'Add Sync',
    bulkAddSyncsButton: 'Bulk Add Syncs',
    labels: {
      abandonOnboarding: 'Abandon Onboarding?',
      abandonOnboardingDescription: 'Are you sure you want to abandon the in-progress onboarding and go back?',
      alreadyOnboardedWarning: 'This group is already onboarded.',
      confirmAbandon: 'Yes, go back',
      pageTitle: 'Manage Membership',
      step1title: 'Step 1: Select Destination',
      step1description: 'Please select the destination type and the destination whose membership you want to manage.',
      selectDestinationType: 'Select Destination Type',
      selectDestinationTypePlaceholder: 'Select an option',
      searchDestination: 'Search destination',
      searchGroupSuggestedText: 'Suggested Groups',
      noResultsFound: 'No results found',
      appsUsed: 'This group uses the following apps:',
      outlookWarning: 'There are important settings that should be considered before sending email to this Outlook group. Follow the instructions on your organization.',
      appIdNotOwnerWarning: 'Warning: GMM is not the owner of this group! It will not be able to manage membership for this group until you add it.',
      userNotOwnerWarning: 'Warning: You are not the owner of this group! You can only manage memberships with GMM for groups you own.',
      step2title: 'Step 2: Run Configuration',
      step2description: '',
      advancedQuery: 'Advanced Query',
      advancedView: 'Advanced View',
      query: 'Query',
      validQuery: 'Query is valid.',
      invalidQuery: 'Failed to parse query. Ensure it is valid JSON.',
      invalidGroups: 'Invalid group IDs:',
      step3title: 'Step 3: Membership Configuration',
      step3description: 'Define the source membership for the destination.',
      selectStartDate: 'Select an option to start managing the membership',
      ASAP: 'ASAP',
      requestedDate: 'Requested Date',
      selectRequestedStartDate: 'Select a requested start date',
      from: 'From',
      selectFrequency: 'Select the frequency with which XMM should manage the membership',
      frequency: 'Frequency',
      hrs: 'hrs',
      preventAutomaticSync: 'Prevent automatic synchronization if membership change exceeds increase and/or decrease threshold?',
      increase: 'Increase',
      decrease: 'Decrease',
      step4title: 'Step 4: Confirmation',
      step4description: '',
      objectId: 'Object ID',
      sourceParts: 'Source Parts',
      sourcePart: "Source Part",
      noThresholdSet: 'No threshold set',
      savingSyncJob: 'Saving...',
      updatingSyncJob: 'Updating...',
      group: 'Group',
      destinationPickerSuggestionsHeaderText: 'Suggested destinations',
      expandCollapse: 'Expand/Collapse',
      sourceType: 'Source Type',
      addSourcePart: 'Add Source Part',
      excludeSourcePart: 'Exclude Source Part',
      deleteLastSourcePartWarning: 'Cannot delete the last source part.',
      errorOnSchema: 'Expected {0} but got type {1} at {2}.',
      searchGroupName: 'Search group name',
      HR: 'HR',
      groupMembership: 'Group Membership',
      groupOwnership: 'Group Ownership',
      placeMembership: 'Place Membership',
      clickHere: 'Click here',
      requestor: 'Requestor',
    }
  },
  copy: 'Copy',
  remove: 'Remove',
  delete: 'Delete',
  edit: 'Edit',
  submit: 'Submit',
  needHelp: 'Need help?',
  next: 'Next',
  close: 'Close',
  cancel: 'Cancel',
  learnMore: 'Learn more',
  errorItemNotFound: 'Item not found',
  welcome: 'Welcome',
  back: 'Back',
  backToDashboard: 'Back to dashboard',
  version: 'Version',
  yes: 'Yes',
  no: 'No',
  or: 'Or',
  and: 'And',
  privacyPolicy: 'Privacy Policy',
  hoursAgo: '{0}, {1} hrs ago',
  hoursLeft: '{0}, {1} hrs left',
  pendingInitialSync: 'Pending initial sync',
};
