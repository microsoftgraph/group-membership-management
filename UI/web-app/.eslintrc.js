module.exports = {
    root: true,
    env: {
      browser: true,
      es2021: true,
      node: true
    },
    extends: [
      "eslint:recommended",
      "plugin:@typescript-eslint/recommended",
      "plugin:react/recommended",
      "plugin:react-hooks/recommended"
    ],
    parser: "@typescript-eslint/parser",
    parserOptions: {
      ecmaFeatures: {
        jsx: true
      },
      ecmaVersion: "latest",
      sourceType: "module"
    },
    plugins: [
      "react",
      "@typescript-eslint",
      "react-hooks"
    ],
    settings: {
      react: {
        version: "detect"
      }
    },
    rules: {
      "react/react-in-jsx-scope": "off",
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "warn"
    },
    ignorePatterns: [
      "node_modules/**",
      "build/**",
      "dist/**",
      "coverage/**",
      ".git/**",
      "public/**"
    ]
  };