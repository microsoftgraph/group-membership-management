# Release Notes:

## Release 3/1/2025

- Added UI popup with list of attributes and descriptions.
- Added UI business justification for submitters.
- Updated UI to include Last Modified By in job details.
- Added functionality to create group from UI.
- Bug fixes for functions and UI.

## Release 2/1/2025

- Replaced backend Newtonsoft with System.Text.Json package.
- Updated email to include deeplinking.
- Implemented UI Group and Teams Channel deeplinking with error pages.
- UI accessibility fixes and checks added.
- Added better UI HR source part depth description and controls.
- Added UI descriptions for destination types.

## Release 1/1/2025

- UI enhancements to People Picker search suggestions in HR source part.
- Updated GraphUpdater to support multilane.
- Removed Destination / TargetOfficeGroupId columns in favor of Group / Channel EF entities.

## Release 12/1/2024

- Added backend and UI support for job configuration history tracking.
- Added UI preview of source part details in the job details page.
- Enabled sync job deletion from the UI.
- Added policy to allow/disallow submission reviewers from reviewing their own submissions.
- UI styling and accessibility fixes.
- Display a message if manager id is not found in HR source part.
- Created MessageSpliiter function in preparation for multilane functionality.

## Release 11/1/2024

- Added UI support for setting attribute descriptions and disabling filter attributes from the admin config page.
- Added email notification samples for reference.
- Implemented Deeplinking for UI.
- Implemented Sql validation in the Advanced Query view of UI.
- Added UI support for IS NULL and IS NOT NULL clauses.
- Added new ServiceBus topics / subscriptions in preparation for multilane functionality.

## Release 10/1/2024

- Enabled admins to see attribute values in the custom source admin config page.
- Enabled grouping in HR source parts.
- Updated HR source parts to have searchable values of descriptions with codes in dropdown.
- Updated cache deletion logic.

## Release 9/10/2024

- Added fallback message / html for all actionable message notifications.
- Allowed jobs to be edited for resubmission if their status is SubmissionRejected.
- Added SignalR to update the UI when resetting / stopping GMM.

## Release 8/22/2024

- Updated wording on purging emails for better clarity.
- Added the General tab in the Admin Center along with the General Settings Admin role.
- Added the Operations tab in the Admin Center along with the Operations Setting Admin role.
- Updated to .NET 8.0.
- Updated nuget packages.
- Removed the databse rename step from the database migration script. 