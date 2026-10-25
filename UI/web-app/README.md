# Getting Started with Vite

This project uses [Vite](https://vite.dev/) for development and builds.

## Development Tools

### Node Version Switcher (NVS) - Cross Platform

NVS helps you do development on projects with multiple versions of Node. It provides an easy method for installing node and managing the current shell version. NVS can be configured _per shell_, allowing for multiple repos needing different node versions to be worked on simultaneously.

#### Installation

Detailed instructions can be found [in the repo](https://github.com/jasongin/nvs).
The easiest way is to install it using [chocolatey](https://chocolatey.org/), and then running `choco install nvs`.

#### Using NVS with `.node-version` file

Running the `nvs auto on` command will ensure your Node version aligns with the repo whenever your shell enters a directory with a `.node-version` file.

If you don't want to turn this feature on, you can still take advantage of the file by running `nvs use auto` manually from within our repo will set your Node version to the repo's, from the `.node-version` file.
_This repo has a `.node-version` file with the specified version set to `v14.19.0`._

### Node.js through NVS

Easy repo instructions:

1. Open your shell
1. At the shell prompt type:

   ```bash
   cd <repo directory>
   nvs use auto
   ```

Set default Node version:

1. Open your shell
1. Open browser to check the current [NodeJS LTS version](https://nodejs.org/)
1. At the shell prompt type:

   ```bash
   nvs add <version>
   # Example: nvs add 23.1.0
   nvs link <version>
   # Example: nvs link 23.1.0
   ```

### PNPM

Performant Node Package Manager, or pnpm is what we use to manage dependencies. You can get it [here](https://pnpm.io/installation).
We are using pnpm v7, which is compatible with this node version.

## Available Scripts

In this project directory, you can run:

### `pnpm start`

Runs the app in development mode using Vite.\
Open [http://localhost:3000](http://localhost:3000) to view it in the browser.

The page will reload if you make edits.\
You will also see any lint errors in the console.

### `pnpm test`

Launches the unit test runner in watch mode using Vitest.\

### `pnpm test:run`

Runs unit tests once (non-watch mode) using Vitest.

#### Unit test commands (Vitest)

From `UI/web-app`, use these commands:

- Run tests in watch mode (local development):

  ```bash
  pnpm test
  ```

- Run all tests once (non-interactive / CI-friendly):

  ```bash
  pnpm run test:run
  ```

- Run a single test file:

  ```bash
  pnpm run test:run src/components/Page/Page.test.tsx
  ```

- Run tests with coverage output:

  ```bash
  pnpm run test:run --coverage
  ```

Vitest is configured in `vite.config.ts`. Test files are discovered with:

- `src/**/__tests__/**/*.{js,jsx,ts,tsx}`
- `src/**/*.{spec,test}.{js,jsx,ts,tsx}`

Coverage thresholds are also enforced through `vite.config.ts` when running with `--coverage`.

### `pnpm run build`

Builds the app for production to the `build` folder.\
It bundles React in production mode and optimizes output for performance.

The build is minified and the filenames include the hashes.\
Your app is ready to be deployed!

### `pnpm preview`

Serves the built app locally for verification.

### `npx eslint .`

Runs ESLint on all files in the project according to the configuration in `eslint.config.mjs`.

## Playwright UI-only tests (no WebApi/auth)

Run these when you only want to validate UI behavior and avoid dependencies on WebApi, sign-in, or 2FA.

### Default behavior

- `playwright.config.ts` now defaults to mock mode (`PLAYWRIGHT_USE_MOCK_API=true` unless explicitly set to `false`).
- In mock mode, Playwright:
  - Skips `tests/auth/auth-setup.ts`
  - Starts the web app with `REACT_APP_PLAYWRIGHT_MOCK_MODE=true`
  - Uses mocked network handlers from `tests/mocks/mockApi.ts`
  - Runs the UI mock project targeting `tests/home/home.spec.ts`

### Run in PowerShell

```powershell
Set-Location "<path-to-repo>\UI\web-app"
pnpm exec playwright test
```

### Force real integration mode (existing sign-in flow)

```powershell
Set-Location "<path-to-repo>\UI\web-app"
$env:PLAYWRIGHT_USE_MOCK_API = 'false'
pnpm exec playwright test
```

## Admin Center Health Dashboard

The **Health Dashboard** tab requires the `Dashboard.Read.All` role. It displays
current pending-review, in-progress, and anomalous-status aggregates. The three
headline metric cards are not a total of all active jobs. Anomalous and
in-progress counts can overlap.
Card info icons use a help cursor and show descriptions on hover or keyboard
focus; they are not click actions.

The header's activity range changes both charts; each chart also has its own
range menu: **7 days**, **14 days**, **1 month**, or **3 months**. The month options
retain the existing 30- and 90-day windows. These filters do not change the current-health tables.
**Individual ranges** means the two charts currently have different ranges selected.
**Refresh** reloads the health snapshot and both charts. Each chart includes an
expandable **Reviewer breakdown**. Requests On Behalf Of covers onboarding and
update requests filed by reviewers on behalf of a group owner.

**Automated** includes onboarding and update auto-approvals
(`OnboardingAutoApproved` and `UpdateAutoApproved`). Manual approvals and
rejections remain separate and attributed to their reviewers.

The dashboard follows the selected light or dark theme, including refresh metadata
and status surfaces. Table values use neutral text rather than severity colors.
Status-card borders are red for errors, blue for pending automatic approval,
and gray for totals; they are not derived from staleness. Long count labels
truncate with an ellipsis and show their full original text on hover.
**Last refreshed** sits beside **Refresh** and identifies the last successfully
loaded health snapshot. It does not change for chart-only range changes or failed
refreshes. This timestamp and table timestamps use the browser's local time zone;
activity charts remain bucketed by UTC day.

## Learn More

- [Vite Documentation](https://vite.dev/guide/)
- [React Documentation](https://react.dev/)
