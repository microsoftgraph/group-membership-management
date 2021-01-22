## Using Application Insights to troubleshoot GMM

GMM uses Log Analytics to log most activity within GMM processes, however sometimes logs might not be available due to unexpected errors, in those cases we will need to use Application Insights to find out if an error occured in GMM.

An Application Insights resource was created as part of GMM setup, so it will be available on your Azure Portal.

## Steps to locate exceptions using Application Insights

1. In the Azure Portal navigate to your 'Application Insights' resource. If you don't see it on your screen you can use the top search bar to locate it, the naming convention is `<solutionAbbreviation>`-data-`<environmentAbbreviation>` i.e. gmm-data-dev
2. In the Application Insights screen, locate and click on the 'Logs' blade.
3. In order to find the exceptions you can run any of the queries below in the query window:

    exceptions // get all exceptions, date time filter is set by the UI

    or

    exceptions | where timestamp > ago(30m) // get all exception from the last 30 minutes
