# Notifier Setup

The GMM Notifier uses adaptive cards to send Outlook Actionable Emails (OAM). OAM requires that an special id, called an `provider id`, be included in each adaptive card payload in order to validate the sender, content, and recipients of the message.

The provider id is not a secret, since it is only one part of a multipart authorization scheme. The provider id is tied directly to a list of authorized senders, from which the email must be sent in order to be accepted.  Regardless, we will be treating it like a secret within GMM.

These are the steps to procure, you must procure a `provider id` for your environment.

## Prerequisites

1. You should already have set up your pre-requisites Key Vault.
2. You need a list of sender email addresses from which actionable emails will originate.

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

<!-- Link References -->
[Outlook Actionable Messages Developer Dashboard]: https://aka.ms/publishoam
[Outlook Actionable Messages Admin Dashboard]: https://outlook.office.com/connectors/oam/Admin