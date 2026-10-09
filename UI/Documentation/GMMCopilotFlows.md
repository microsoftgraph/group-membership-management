# GMM Copilot UI flows

This guide describes the membership-building experience in the Copilot panel. It covers both onboarding a new membership and editing an existing membership.

## Key concepts

- **Working rules** are the membership rules currently in the editor. They may include unsaved changes.
- **Proposed membership** is a change Copilot has suggested but the user has not yet applied to the editor.
- **Current membership description** describes the working rules, not necessarily the last saved version of the membership.
- **Saved description** is a description retained in the current UI session, together with a snapshot of the rules it describes. It can come from an applied
  proposal or a description request. It does not persist across page reloads.

**Apply & Review is not Save.** Applying updates the working editor; the user must still review and save through the normal membership flow.

## 1. Open Copilot

### Session boundaries

Each entry into **Manage Membership** starts a fresh Copilot session. This includes clicking **Add** on the main page, opening an editing deep link such as
`/ManageMembership/<jobId>`, switching between jobs, and returning to the same job through a new navigation. Previous messages, model context, pending
proposals, draft input, and cached descriptions are discarded; the panel starts closed. Late responses from the previous session cannot repopulate the new chat.

Closing and reopening only the Copilot panel within the same membership session still retains its conversation and normal resume behavior. Navigation does not
apply or save a Copilot proposal; the normal editor loading and save flows remain unchanged.

The header starts at the top of the panel. Short transcripts are bottom-aligned as one continuous stack above the composer; returning Welcome back cards do not
add empty space between earlier messages. Longer transcripts remain scrollable.

### New membership

When no rules exist, eligible users can choose **Let Copilot build it for you** or the manual **Add a rule** path.

Opening a fresh Copilot chat shows:

- The introductory welcome and three-step explanation.
- The review disclaimer.
- Suggested prompts configured by the administrator.
- The text composer.

Selecting a suggestion or sending a typed message starts the conversation. Suggestions disappear after that first message.

### Existing membership

Opening Copilot for an existing membership shows an inline **Welcome back** message with **Continue / New chat**.

On the first opening in a fresh editing session, if working rules exist, Copilot automatically requests their current-membership description. The Welcome back
card shows **Getting current membership description**, then the description and **Continue / New chat**. The request uses the same read-only flow as New chat,
with current working rules and empty model history, without a visible user question or an Apply & Review action. Working rules remain unchanged.

The card shows one concise description in bold throughout (also in New chat), with headings on separate lines and short guidance: **Continue refining, or start
a new chat.** Automatic description requests ask for no greeting, repeated definition, proposal heading, or button instructions. If a response still includes
the standard **Proposed Membership:** paragraph, the UI uses that definition alone, just as it does when retaining an applied proposal. Descriptions start
directly with **Includes...** rather than repeating the UI's **Your current membership:** heading. The UI also normalizes the known "Your current membership
includes..." prefix without changing criteria or exclusions.

Reopening with unchanged rules reuses the description. With no rules, no automatic request is made. If the request fails, an error is shown; **New chat** can
retry.

Copilot availability depends on the feature setting and the user's AI role. Membership-editing entry actions require write permission. Users without Copilot
access retain the manual path.

## 2. Build or refine membership

1. The user selects a suggestion or types a request.
2. Copilot receives the request, conversation history, and current working query. Each request treats those current rules as authoritative, so earlier proposals
   do not define the starting membership after manual edits. This applies to short confirmations such as "yes" as well as requests to add or change rules. The
   visible user bubble keeps only the user's original text.
3. The panel shows a loading state while waiting.
4. Copilot may ask a clarification question, describe the existing rules, or propose a membership change.
5. For a change, the response describes the complete proposed membership. The WebApi prompt requests a bold **Proposed Membership:** label and a bold
   definition, followed by guidance to use **Apply & Review**.

A description or clarification alone does not offer **Apply & Review**. An applicable change shows a full-width blue **Apply & Review** button inside the
assistant's gray response card.

### Current rules on every request

Every call to `/api/v1/Copilot/chat` carries the current editor rules in both:

- The structured `workingQuery` field, which the server places in the model's context.
- A short note in the latest message that the working query is authoritative.

The rules are sent once, in `workingQuery`, and are not repeated as JSON in the message text. This applies to **all turns**, not just automatic membership
descriptions. For HR rules, the outgoing context omits the display title and its summarized criteria text. The actual HR filter, current leader and depth,
inclusion/exclusion, and identity types define the rule. Group-membership names remain in the context. This does not change local rule-card titles or remove
earlier conversation text. Ordinary requests and follow-up confirmations use this message format:

```text
The current working query (sent with this request, including any manual edits) is authoritative. Earlier messages may describe outdated membership; preserve rules the user has not asked to change.

User request:
<the user's original message, including a short confirmation such as "yes">
```

The rule context is rebuilt for each request. This does not overwrite the editor rules, rewrite historical messages, or automatically apply a proposal. The UI
continues to display only the user's original text in their message bubble. Model history since the latest successful description regeneration still provides
context for questions and confirmations. Messages from before that regeneration remain visible but are not sent back to the model.

For example:

1. The user applies a proposal for User 4's organization.
2. In the editor, they change the first rule to User 6's organization.
3. They return to Copilot and ask to add User 5's organization.
4. Both that request and the later "yes" confirmation carry User 6's current rule.
5. The expected proposal is User 6's organization plus User 5's organization, preserving the current reporting depth of the first rule, not restoring User 4
   from the conversation history.

### Organization leader and depth

The actual organization criteria come from the working editor query:

- `query.source.manager.id` identifies the selected leader.
- `query.source.manager.depth` supplies the selected reporting depth. An omitted depth is sent as `null`, meaning all levels.
- Removing the manager from the query removes org structure from the outgoing rules, even if old Copilot auto-selection hints remain on the UI object.

The UI uses its employee-ID lookup cache to obtain the current leader's display name and object ID. If those details are already cached, no extra leader or
Graph lookup is needed. If they are missing, the existing employee-ID lookup and Graph display-name lookup run before the chat request. Depth needs no lookup.

The original `managerToAutoSelect` and `depthToAutoSelect` hints do not override the current leader or depth. An email hint is retained only when its object ID
matches the current resolved leader. If a leader lookup fails, the request is still sent and that rule falls back to its stored leader hint, if one exists,
rather than blocking the chat.

## 3. Apply & Review

When the user selects **Apply & Review**:

1. The proposed rules are applied to the working membership editor.
2. Once the apply succeeds, the panel closes so the user can review the rules. If the apply fails, the panel stays open with an error and the proposal remains
   so the user can retry.
3. After successful application, a right-aligned **Apply & Review** acknowledgment is added to the transcript.
4. The UI retains the applied description and a snapshot of the resulting rules.

The acknowledgment is UI-only; it is not sent back as model conversation history. The membership is not saved automatically.

The retained description normally comes from the `Proposed Membership:` paragraph in the accepted response. If that paragraph is absent, the original response
is retained as **Last applied response**, rather than treated as a reusable current-membership summary.

## 4. Close and resume

Closing the panel does not reset the conversation or discard working rules. Reopening a chat shows an inline **Welcome back** card with **Continue / New chat**.
There is no separate "Resume your chat?" dialog.

The description shown on resume depends on the saved snapshot:

| Situation | Resume behavior |
|---|---|
| No saved description in this UI session | Show the inline welcome-back message without a description request. |
| Saved description matches the working rules | Reuse it without calling the chat endpoint. |
| Working rules differ from the saved snapshot | Request a fresh description and update the welcome-back card. |

During a refresh, the card shows an indeterminate loading bar labeled **Getting current membership description**. The composer is disabled while the request is
pending. The refreshed description replaces the card's old description; it does not clear history or add a duplicate assistant response to the transcript.
Working rules remain unchanged.

Every description regeneration sends **empty model conversation history**. Its latest message explicitly supplies the current rules as JSON and asks for their
description. Those rules also appear in the structured `workingQuery` field. It uses the automatic description message shown in the New chat section below.

After success, model context starts with the fresh description. Later requests include that description and only subsequent conversation turns, so old context
cannot return on the next follow-up. The visible transcript is preserved on resume, including earlier messages. If regeneration fails, the existing model
context is retained until a successful refresh. Ordinary follow-ups do not reset context, so confirmations such as "yes" still refer to the preceding exchange.

### Continue

**Continue**:

- Hides only the card's buttons.
- Keeps the message and description visible above subsequent conversation turns.
- Focuses the composer.
- Does not send a request, reset history, apply rules, or save membership.

The user can also type a follow-up directly rather than selecting Continue.

## 5. New chat

The header **New chat** button and the inline **New chat** button use the same flow. They clear conversation history and pending proposals, but **preserve
working rules**.

| Working membership state | New chat behavior |
|---|---|
| No rules | Show the fresh-chat welcome/reset introduction and configured suggestions. Do not request a description. |
| Rules exist and a reusable saved description matches them | Display that description immediately. No API call or loading bar. |
| Rules exist but the description is missing, unsuitable, or stale | Request a description of the current working rules. |

When a description request is needed, the latest message asks for a description of the current working query, which is sent in the structured `workingQuery`
field:

```text
What is my current membership? Describe only the current working query rules without changing them.
```

The UI sends this to `/api/v1/Copilot/chat` with the working query and empty conversation history. This automatic question does **not** appear as a user bubble.
While waiting, the panel shows **Getting current membership description** and an indeterminate loading bar.

The resulting gray assistant card contains:

- The current-membership description, with Markdown formatting preserved.
- Guidance for continuing or starting a new chat.
- **Continue / New chat** buttons.

It does **not** include a **Welcome back** heading or an **Apply & Review** button. This is a read-only description, not a membership change.

Continue hides only the buttons. The description remains in the new conversation and provides context for follow-up requests. A valid saved description can be
reused on repeated New chats as long as the working rules still match.

The header New chat control is shown when transcript messages exist. It may disappear while a fresh description is loading and return when the description has
been added to the transcript.

## 6. Errors and recovery

- Failed requests appear as errors in the conversation; they do not apply or save membership changes.
- If a resume refresh fails, the card shows a review notice instead of presenting an outdated description as current. Existing history and working rules remain.
- If a New chat description request fails, the user can select New chat again to retry.
- An automatic description response that proposes operations is rejected rather than offered as a change to apply.
- Responses from requests superseded by a newer chat request are ignored.

## Related documentation

- [UI setup](UISetup.md)
