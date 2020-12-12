## Steps to locate the logs in log analytics for a particular sync
This document will guide you through the steps required in log analytics for finding the logs for a particular sync in GMMv2.

* If you already have the objectId for the destination group you are trying to find the logs for then skip ahead to the next step. If not, navigate to [AAD](https://ms.portal.azure.com/#blade/Microsoft_AAD_IAM/GroupsManagementMenuBlade/AllGroups) on the azure portal and find the objectId of the destination group provided. If you don't have the details available then open the *syncJobs* table and search based on the target security group id

* Once you have the objectId for the destination group, navigate to the syncJobs table and run a query to find the latest runId for the respective destination group. The query editor on table storage will let you find the latest runId based on the target security group id. Set this value to the objectId from the earlier step and run the query.

* Once you have the runId for the destination group, navigate to the log analytics workspace for your production environment. It start start with `gmm-data-`. Navigate to logs on the left panel and run the following query:

      ApplicationLog_CL 
      | where runId_g == '<destination group RunId>'
      | order by TimeGenerated

* The *Results* will contain the logs with a message as well as the location of the message

* You can additionally check for the logs based on a particular sync type as well such as `SecurityGroup`, like this:

      ApplicationLog_CL 
      | where runId_g == '<destination group RunId>'
      | order by TimeGenerated
      | where location_s == 'SecurityGroup'
      

