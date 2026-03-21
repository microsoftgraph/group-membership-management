"use strict";

module.exports = {
  hooks: {
    readPackage
  }
};

function readPackage(packageJson, context) {

  const dependencyChanges = [
    new PackageUpgradeStrategy('underscore', ['1.13.6', '^1.13.6', '~1.13.6'], '1.13.8'),
    new PackageUpgradeStrategy('serialize-javascript', ['4.0.0', '^4.0.0', '~4.0.0', '6.0.2', '^6.0.2', '~6.0.2', '6.0.1', '^6.0.1', '~6.0.1', '6.0.0', '^6.0.0', '~6.0.0'], '7.0.3'),
    new PackageUpgradeStrategy('cookie', ['0.4.2', '^0.4.2', '~0.4.2'], '0.7.2'),
    new PackageUpgradeStrategy('js-yaml', ['3.14.1', '^3.14.1', '~3.14.1'], '4.1.1'),
    new PackageUpgradeStrategy('js-yaml', ['4.1.0', '^4.1.0', '~4.1.0'], '4.1.1'),
    new PackageUpgradeStrategy('postcss', ['7.0.39', '^7.0.39', '~7.0.39'], '8.4.47'),
    new PackageUpgradeStrategy('qs', ['6.13.0', '^6.13.0', '~6.13.0', '6.13.1', '^6.13.1', '~6.13.1'], '6.14.1'),
    new PackageUpgradeStrategy('@remix-run/router', ['1.23.0', '^1.23.0', '~1.23.0', '1.23.1', '^1.23.1', '~1.23.1'], '1.23.2'),
    new PackageUpgradeStrategy('nth-check', ['^1.0.2'], '2.0.1'),
    new PackageUpgradeStrategy('webpack', ['^5.64.4'], '5.97.1'),
    new PackageUpgradeStrategy('postcss', ['^8.4.24'], '8.4.31'),
    new PackageUpgradeStrategy('webpack-dev-middleware', ['<=5.3.3'], '5.3.4'),
    new PackageUpgradeStrategy('webpack-dev-server', ['4.15.2', '^4.15.2', '~4.15.2'], '4.15.3'),
    new PackageUpgradeStrategy('braces', ['<3.0.3'], '3.0.3'),
    new PackageUpgradeStrategy('ws', ['>=8.0.0 <8.17.1', '>=7.0.0 <7.5.10'], '8.17.1'),
    new PackageUpgradeStrategy('semver', ['>=7.0.0 <7.5.2', '<5.7.2'], '7.5.2'),
    new PackageUpgradeStrategy('body-parser', ['<1.20.3'], '1.20.3'),
    new PackageUpgradeStrategy('path-to-regexp', ['<0.1.10'], '0.1.10'),
    new PackageUpgradeStrategy('rollup', ['<4.28.1'], '4.28.1'),
    new PackageUpgradeStrategy('http-proxy-middleware', ['<2.0.7'], '2.0.7'),
    new PackageUpgradeStrategy('form-data', ['<4.0.2'], '4.0.4'),
    new PackageUpgradeStrategy('glob', ['^10.3.10'], '10.5.0'),
    new PackageUpgradeStrategy('jws', ['^3.2.2'], '3.2.3'),
    new PackageUpgradeStrategy('node-forge', ['^1'], '1.3.2'),
    new PackageUpgradeStrategy('flatted', ['3.3.3', '^3.3.3', '~3.3.3'], '3.4.2'),
    new PackageUpgradeStrategy('jsonpath', ['1.1.1', '^1.1.1', '~1.1.1'], '1.2.1'),
    new PackageUpgradeStrategy('axios', ['1.8.2', '1.13.2', '^1.13.2', '~1.13.2'], '1.13.5'),
    new PackageUpgradeStrategy('ajv', ['8.17.1', '^8.17.1', '~8.17.1'], '8.18.0'),
    new PackageUpgradeStrategy('lodash', ['4.17.21', '^4.17.21', '~4.17.21'], '4.17.23'),
    new PackageUpgradeStrategy('esbuild', ['0.21.5', '^0.21.5', '~0.21.5'], '0.25.0'),
    new PackageUpgradeStrategy('minimatch', [
      '3.1.2', '^3.1.2', '~3.1.2',
      '3.1.0', '^3.1.0', '~3.1.0',
      '3.0.0', '^3.0.0', '~3.0.0',
      '3', '^3', '~3',
      '3.1.3', '^3.1.3', '~3.1.3',
      '3.1.4', '^3.1.4', '~3.1.4'
    ], '3.1.5'),
    new PackageUpgradeStrategy('minimatch', [
      '5.1.6', '^5.1.6', '~5.1.6',
      '5.1.0', '^5.1.0', '~5.1.0',
      '5.0.0', '^5.0.0', '~5.0.0',
      '5', '^5', '~5',
      '5.1.7', '^5.1.7', '~5.1.7',
      '5.1.8', '^5.1.8', '~5.1.8'
    ], '5.1.9'),
    new PackageUpgradeStrategy('minimatch', [
      '9.0.5', '^9.0.5', '~9.0.5',
      '9.0.0', '^9.0.0', '~9.0.0',
      '9', '^9', '~9',
      '9.0.6', '^9.0.6', '~9.0.6',
      '9.0.7', '^9.0.7', '~9.0.7',
      '9.0.8', '^9.0.8', '~9.0.8'
    ], '9.0.9'),
  ];

  const logger = new Logger(context);
  const upgrader = new PackageUpgrader(packageJson, logger);

  dependencyChanges.forEach(({ name, targetVersions, newVersion }) =>
    upgrader.tryUpgradeDependency(name, targetVersions, newVersion));

  return packageJson;
}

/**
 * Describes whether and how a package should be upgraded.
 */
class PackageUpgradeStrategy {
  /**
   * Creates an instance of upgrade strategy describing the package to be upgraded,
   * the target versions to upgrade, and the new version to use.
   * @param {string} name The name of the package to be upgraded
   * @param {Array<string>} targetVersions Versions to upgrade
   * @param {string} newVersion The new version to use
   */
  constructor(name, targetVersions, newVersion) {
    this.name = name;
    this.targetVersions = targetVersions;
    this.newVersion = newVersion;
  }
}

/**
 * Encapsulates package upgrade functionality.
 */
class PackageUpgrader {
  constructor(packageJson, logger) {
    this.packageJson = packageJson;
    this.logger = logger;
  }

  /**
   * Upgrades a dependency version for the specified name with a version whose string value
   * begins with one of the supplied targetVersions.
   * @param {string} name The name of the dependency
   * @param {Array} targetVersions An array of versions eligible for upgrade
   * @param {string} newVersion The new version to use
   * @returns true, if the package was upgraded; otherwise false
   */
  tryUpgradeDependency(name, targetVersions, newVersion) {

    const projectName = this.packageJson.name;
    const dependencySections = ['dependencies', 'devDependencies', 'optionalDependencies', 'peerDependencies'];

    for (const section of dependencySections) {
      const dependencies = this.packageJson[section];
      if (!dependencies || !dependencies[name]) continue;

      const currentVersion = dependencies[name];
      if (targetVersions.some(version => currentVersion.startsWith(`${version}`))) {
        this.logger.logOnce(`[${brightGreen(projectName)} ${section}]: ${brightCyan(name)}@${brightMagenta(currentVersion)} => ${brightYellow(newVersion)}`);
        dependencies[name] = newVersion;
        return true;
      }

      this.logger.logOnce(gray(`[${projectName} ${section}]: ${name}@${currentVersion} was found, but does not satisfy targetVersions: '${targetVersions.join("', '")}'`));
    }
    return false;
  }
}

/**
 * Injectable logger that provides the ability to log a message only once.
 */
class Logger {
  constructor(context) {
    Logger.messages = Logger.messages || [];
    this.log = context.log;
  }
  /**
   * Logs the specified message to the context logger
   * @param {string} message The message to log
   */
  log(message) {
    Logger.messages.push(message);
    this.log(message);
  }

  /**
   * Logs the specified message to the context logger only once, ignoring subsequent identical messages.
   * @param {string} message The message to log
   */
  logOnce(message) {
    if (Logger.messages.includes(message)) return;
    Logger.messages.push(message);
    this.log(message);
  }
}

// https://github.com/Marak/colors.js/blob/master/lib/styles.js
const brightGreen = (text) => formatColor(text, 92, 39);
const brightYellow = (text) => formatColor(text, 93, 39);
const brightMagenta = (text) => formatColor(text, 95, 39);
const brightCyan = (text) => formatColor(text, 96, 39);
const gray = (text) => formatColor(text, 90, 39);

const formatColor = (text, open, close) => {
  return `\u001b[${open}m${text}\u001b[${close}m`
}