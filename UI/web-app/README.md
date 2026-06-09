# Getting Started with Vite

This project uses [Vite](https://vite.dev/) for development and builds.

## Development Tools
### Node Version Switcher (NVS) - Cross Platform
NVS helps you do development on projects with multiple versions of Node.  It provides an easy method for installing node and managing the current shell version. NVS can be configured _per shell_, allowing for multiple repos needing different node versions to be worked on simultaneously.

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

## Learn More

- [Vite Documentation](https://vite.dev/guide/)
- [React Documentation](https://react.dev/)
