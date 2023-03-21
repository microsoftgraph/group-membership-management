# Notifier Setup

The GMM Notifier uses adaptive cards to send Outlook Actionable Emails (OAM). OAM requires that a special id, called a `provider id`, be included in each adaptive card payload in order to validate the sender, content, and recipients of the message.

If an actionable message is sent from an email address that is not part of the approved senders list, the adaptive card will not be rendered.

These are the steps to procure a `provider id` for an environment.

## Create a Provider

1. Navigate to the [Outlook Actionable Messages Developer Dashboard].
1. Click the 'New Provider' button.
1. Fill out the required fields:
    Field Name | Description
    -|-
    Friendly Name | A name that represents the environment associated with the Originator Id. *Example: `<SolutionAbbreviation>` Notifier `<EnvironmentAbbreviation>`.*
    Sender Email Address | The email address from which actionable messages will be sent.
    Target URLs | The endpoints where the actional messages will send responses.
    Scope of Submission | This should be set to `Organization` scope.

After the submission is sent, an email will be sent to the Exchange admins asking them to review the request. The email contains an actionable message with a button that directs them to the [Outlook Actionable Messages Admin Dashboard] where they can approve the request.


## Update parameters.env.json file with your new notifierId

1. Find your *parameters.`<EnvironmentAbbreviation>`.json* file in the [Infrastructure/data/parameters directory](../Infrastructure/data/parameters/)
1. Update the "notifierProviderId" parameter with the value of the Provider Id (Originator) from the provider that you created
1. Add and commit the change to the repository so that it gets written to the data keyvault

<!-- Link References -->
[Outlook Actionable Messages Developer Dashboard]: https://aka.ms/publishoam
[Outlook Actionable Messages Admin Dashboard]: https://outlook.office.com/connectors/oam/Admin