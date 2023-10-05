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
  bannerMessage:
    'Need help? Click here to learn more about how Membership Management works in your organization.',
  okButton: 'OK',
  groupIdHeader: 'Enter Group ID',
  groupIdPlaceHolder: 'Group ID',
  addOwnerButton: 'Add GMM as an owner',
  membershipManagement: 'Membership Management',
  learnMembershipManagement:
    'Learn how Membership Management works in your organization',
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
      thresholdViolations: 'Threshold Violations',
      sourceParts: 'Source Parts',
      membershipStatus: 'Membership status',
      sync: 'Sync',
      enabled: 'Enabled',
      disabled: 'Disabled',
    },
    descriptions: {
      lastModifiedby: 'User who made the last change to this job.',
      startDate: 'Date of the onboarding of this job into GMM.',
      endDate: 'Date of the last run of this job.',
      type: 'Sync type.',
      id: 'Object ID of the destination group.',
      lastRun: 'Time of the last run of this job.',
      nextRun: 'Next run time for this job.',
      frequency: 'How often this job runs.',
      requestor: 'User who requested this job to be onboarded into GMM.',
      increaseThreshold:
        'Number of users that can be added from the target group expressed as a percentage of the current size of the group.',
      decreaseThreshold:
        'Number of users that can be removed from the target group expressed as a percentage of the current size of the group.',
      thresholdViolations: 'Number of times a threshold was exceeded.',
    },
    MessageBar: {
      dismissButtonAriaLabel: 'Close',
    },
    openInAzure: 'Open in Azure',
    viewDetails: 'View Details',
    editButton: 'Edit',
  },
  JobsList: {
    listOfMemberships: 'List of memberships',
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
          },
        },
      },
      filterButtonText: 'Filter',
      clearButtonTooltip: 'Clear Filters',
    },
    NoResults: 'No memberships found.',
  },
  ManageMembership: {
    manageMembershipButton: 'Manage Membership',
    labels: {
      pageTitle: 'Manage Membership',
      step1title: 'Step 1: Select Destination',
      step1description: 'Please select the destination type and the destination whose membership you want to manage.',
      selectDestinationType: 'Select Destination Type',
      searchDestination: 'Search destination',
      appsUsed: 'This group uses the following apps:',
      outlookWarning: 'There are important settings that should be considered before sending email to this Outlook group. Follow the instructions on your organization.'
    }
  },
  HelpPanel: {
    specificGuidanceTitle: 'Specific Guidance',
    specificGuidanceDescription: 'If you are looking for more specific guidance we invite you to check out our website to find more details about the XMM processes of your organization.',
    openSite: 'Open site'
  },
  needHelp: 'Need help?',
  next: 'Next',
  close: 'Close',
  learnMore: 'Learn more',
  errorItemNotFound: 'Item not found',
  welcome: 'Welcome',
  back: 'Back to dashboard',
  version: 'Version',
};
