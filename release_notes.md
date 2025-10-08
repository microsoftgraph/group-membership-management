# Release Notes:

## Release 10/08/2025

Multi‑lane Decommission & Session Enablement
Removed the Medium and Onboarding processing lanes (code, infra templates, UI tiles, unused subscriptions) and introduced/expanded session‑enabled handling for the remaining lanes. Added cleanup + pre‑deployment migration scaffolding and a script to retire obsolete lane resources. Result: simpler routing model, reduced config surface, readiness for session-ordered large workloads.

Messaging, Caching, Batching, Concurrency
Enhanced membership processing throughput by refining batch size limits, surfacing and tuning concurrent write settings, extracting source member IDs earlier, and improving cache file evolution (conversion + metadata). Added safeguards (dispose sender, missing setting checks). Net effect: more controlled write pressure on Graph + safer cache transitions.

Deployment / Infrastructure & Platform Migration
Added PreDeploymentMigrations support, Flex consumption migration scripts, function plan adjustments, bicep/template updates (including session-enabled subs), and isolated worker model adoption for specific functions (e.g., NonProdService, AzureUserReader). Outcome: cleaner deployment pipeline, forward compatibility with Flex, reduced infra drift.

Data / Schema & Persistence Evolution
Introduced SyncJobHistory plus CreatedAt/UpdatedAt auditing fields, and ensured update paths populate history and sync context. This raises observability and auditability for sync lifecycles and supports future analytic/reporting scenarios.

Reliability / Resilience
Strengthened retry logic (general + session-enabled subscription flows), fixed batch sizing edge cases, added defensive checks around settings and sender disposal, and resolved merge/consistency issues. Goal: lower transient failure impact and cleaner degradation behavior.

Logging / Observability
Refined logging logic, adjusted wait behaviors, filtered noisy dashboard messages, and synchronized unit tests with new log semantics. Result: leaner signal and easier issue triage.

Flex Consumption & Performance Tuning
Added targeted scripts plus configuration knobs (concurrent write and batch controls) to safely migrate and optimize workloads under Flex—positioning for higher memory headroom and elastic scaling.

- Rejecting a pending review submission sends out an email with provided feedback.
- Filters in the Jobs List UI are preserved
- After reviewing a submission, the reviewer is redirected back to the Jobs List
- Auto approval of onboarding jobs that contain only group membership source parts (configurable, default is disabled)
- Auto approval of onboarding jobs where query contains only one sql membership source part and the requestor is the org leader (configurable, default is disabled)
- Changed the wording of the include/exclude source part to be more explicit. Removed the include leader option.
- Added a Submission Rejector role which only has permissions to reject jobs, not approve
- Added AI title for source parts (can enable/disable the feature via UI)
- Enabled reordering of the last item in the attribute list
- Addressed Out Of Memory Exceptions in SqlMembershipObtainer
- Removed trailing And/Or from the final query
- Deployment script bug fixes and improvements
- Enabled support for linux containers for the deployment script

## Release 7/28/2025
- Enabled bulk approval via UI.
- Added Maintenance Page to the UI.
- Submission Reviewers can submit feedback on rejection.
- Requested on behalf of is now a group owners dropdown.
- Converted GMO to Flex Consumption.
- Added Reset-GMM.ps1 to help reset GMM when deploying a new version using the deployment script.
- Ordered deployment script parameters and labeled required ones.
- Made the schedule of JobScheduler runs configurable.

## Release 6/30/2025
- Added support for the 'NOT IN' operator in SqlMembership source parts.
- Added dependency requirements check to the deployment script.
- Added a new script to replace 'BETWEEN'/'NOT BETWEEN' with 'IN'/'NOT IN' in SQL filters.
- Added 'Remove GMM Management' button for jobs with the status DestinationGroupNotFound.

## Release 5/21/2025
- Added configurable first visit disclaimer to the UI.
- Fixed transitive/delta calls to address OOME in GMO.
- Added metrics to track P values per lane.
- Enabled UI Bulk Download + added console app to update group settings using exported csv file from bulk download.
- Set default frequency to 24 in UI.
- Org Leader ID/Depth fixes.
- Deployment script fixes and improvements.
- Added equality operator descriptions in the UI.

## Release 4/1/2025
- Added check to verify if submission requestor is still an owner at review time.
- Added optional business justification field for onboardings and updates in the UI.
- Updated Source Parts component in the UI to include an expand/collapse all button and preserve their state.
- Set-up Playwright UI integration tests.
- Removed trace logging from several azure functions.
- Updated PostDeployment.ps1 to grant access to storage accounts.
- Add build number to binaries.
- Added disable and purge dates to the threshold email.

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