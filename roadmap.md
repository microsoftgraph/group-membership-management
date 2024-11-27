# GMM Roadmap

This document outlines features we are considering for future releases.

| Feature Name | Description |
| --- | --- |
| Configuration History | Enable group owners to see when changes to the job definition occurred and by whom. |
| Run History | Enable users to see the history of every sync, including when it occurred, how long it took, and who was added or removed. |
| Installation Simplification | Enable admins to install GMM by providing all required input upfront and running a single script. |
| Write Scalability Improvements | Increase GMM's ability to write changes to groups by creating multiple, self-regulated lanes of writing based on pending changes. This will decrease the runtime of small jobs that get stuck behind large ones. |
| Read Scalability Improvements | Increase GMM's ability to read membership from groups by hashing the results of previous runs and comparing those results to pending syncs. If no pending change is detected, skip reading from the destination group. This saves read capacity for other groups. |
| Outlook Group Creation | Facilitate the creation of Outlook Groups directly in the GMM user interface. |
| Group Configuration | Facilitate common configuration changes required to onboard an existing group. |
| Approval Workflow Improvements | Do not require approval workflow for requests from managers requesting groups whose membership is based on their organization. |