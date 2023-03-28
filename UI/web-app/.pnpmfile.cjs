"use strict";

module.exports = {
  hooks: {
    readPackage
  }
};

function readPackage(packageJson, context) {

  const dependencyChanges = [
    new PackageUpgradeStrategy('nth-check', ['^1.0.2'], '2.0.1'),
    new PackageUpgradeStrategy('webpack', ['^5.64.4'], '5.76.0'),
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
    const dependencies = this.packageJson.dependencies;

    if (dependencies && dependencies[name]) {
      const currentVersion = dependencies[name];
      if (targetVersions.some(version => currentVersion.startsWith(`${version}`))) {
        this.logger.logOnce(`[${brightGreen(projectName)}]: ${brightCyan(name)}@${brightMagenta(currentVersion)} => ${brightYellow(newVersion)}`);
        this.packageJson.dependencies[name] = newVersion;
        return true;
      }
      this.logger.logOnce(gray(`[${projectName}]: ${name}@${currentVersion} was found, `
        + `but does not satisfy targetVersions: '${targetVersions.join('\', \'')}'`));
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