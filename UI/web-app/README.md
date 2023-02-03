# Getting Started with Create React App

This project was bootstrapped with [Create React App](https://github.com/facebook/create-react-app).

## Development Tools
### Node Version Switcher (NVS) - Cross Platform
NVS helps you do development on projects with multiple versions of Node.  It provides an easy method for installing node and managing the current shell version. NVS can be configured _per shell_, allowing for multiple repos needing different node versions to be worked on simultaneously.

#### Installation

Download instructions can be found [in the repo](https://github.com/jasongin/nvs).  After installation, running `nvs install` will print instructions to your shell on how to set it up.

#### Using NVS with `.node-version` file

Running the `nvs auto on` command will ensure your Node version aligns with the repo whenever your shell enters a directory with a `.node-version` file.

If you don't want to turn this feature on, you can still take advantage of the file by running `nvs use auto` manually from within our repo will set your Node version to the repo's, from the `.node-version` file.

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

    ``` bash
    nvs add <version>
        Example: nvs add v14.19.0
    nvs link <version>
        Example: nvs add v14.19.0

### PNPM
Performant Node Package Manager, or pnpm is what we use to manage dependencies. You can get it [here](https://pnpm.io/installation).
We are using pnpm v7, which is compatible with node v14.

## Available Scripts

In this project directory, you can run:

### `npm start`

Runs the app in the development mode.\
Open [http://localhost:3000](http://localhost:3000) to view it in the browser.

The page will reload if you make edits.\
You will also see any lint errors in the console.

### `npm test`

Launches the test runner in the interactive watch mode.\
See the section about [running tests](https://facebook.github.io/create-react-app/docs/running-tests) for more information.

### `npm run build`

Builds the app for production to the `build` folder.\
It correctly bundles React in production mode and optimizes the build for the best performance.

The build is minified and the filenames include the hashes.\
Your app is ready to be deployed!

See the section about [deployment](https://facebook.github.io/create-react-app/docs/deployment) for more information.

### `npm run eject`

**Note: this is a one-way operation. Once you `eject`, you can’t go back!**

If you aren’t satisfied with the build tool and configuration choices, you can `eject` at any time. This command will remove the single build dependency from your project.

Instead, it will copy all the configuration files and the transitive dependencies (webpack, Babel, ESLint, etc) right into your project so you have full control over them. All of the commands except `eject` will still work, but they will point to the copied scripts so you can tweak them. At this point you’re on your own.

You don’t have to ever use `eject`. The curated feature set is suitable for small and middle deployments, and you shouldn’t feel obligated to use this feature. However we understand that this tool wouldn’t be useful if you couldn’t customize it when you are ready for it.

## Learn More

You can learn more in the [Create React App documentation](https://facebook.github.io/create-react-app/docs/getting-started).

To learn React, check out the [React documentation](https://reactjs.org/).
