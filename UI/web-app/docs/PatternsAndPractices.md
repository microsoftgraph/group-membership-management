# Patterns and Practices

## Typing

Any object whose purpose is to contain data, like a model, should be defined as a TypeScript `type` because these types of objects may be used to compose other types or be subject to comparison.

```TypeScript
type User = {
    id: string;
    name: string;
}
```

`Interfaces` should be used for defining classes that may have multiple implementations, like a `Service` or `Api`.  We will generally take a dependency on an interface rather than the implementation.

```TypeScript
interface IAuthenticationService {
    login: Promise<void>;
}

class MSALAuthenticationService implements IAuthenticationService {
    public async login(): Promise<void> {
        // login with msal client
    }
}

class OfflineAuthenticationService implements IAuthenticationService {
    public async login(): Promise<void> {
        // do nothing, because we're testing offline (probably with other services stubs)
    }
}
```

## Enums

We should declare `enums` as `const`` because it can improve performance and reduce the bundle size.
By default, enums are created as JavaScript objects that are then referenced throughout the code.

```TypeScript
// regular enum
enum Color {
    Red = 'red',
    Blue = 'blue',
    Green = 'green'
}
const myColor = Color.Red;

// compiles to:
"use strict";
var Color;
(function (Color) {
    Color["Red"] = "red";
    Color["Blue"] = "blue";
    Color["Green"] = "green";
})(Color || (Color = {}));
const color = Color.Red;
```

Const enums are inlined at compile time.

```TypeScript
// const enum
const enum Color {
    Red = 'red',
    Blue = 'blue',
    Green = 'green'
}
const color = ConstColor.Red;

// compiles to
const color = "red";
```

## Differentiating between Entities and Models

Models are the application-specific representation of a piece or collection of data.  They do not necessarily match the shape of the data returned by an API or other 3rd party dependency, but often times they do.  All Apis in the `Apis` folder should communicate with their respective Api / Endpoints using entities.  The Api Interfaces should not contain references to the entities.  The Api Interfaces should accept and return models and primitive types.

## Creating and using Apis

Apis are the classes we use to communicate with 3rd party dependencies.  They use the 'axios' library for communication and are designed to use the Authentication Service to automatically authenticate requests.  All API method signatures should be described by `types` located in the src/models folder or primitive types.  Their job is to communicate with Api endpoints, which also requires them to transform models to entities and entities to models.

## Exporting

When you create a folder of related objects, it is good practice to create an `index.ts` file that exports the objects and types that you want to be consumed by other modules.  It also allows us to selectively export objects and types and 'hide' objects or types that we consider to be 'internal' to that folder / module.

For example, for our components, the `index.ts` file typically looks like this:

```TypeScript
export * from './ComponentName';
export * from './ComponentName.types';
```

This allows us to export the `<ComponentName/>` component and all of the component-related types without exporting the `<ComponentNameBase/>` or the `getStyles()` function, which should only be accessed internally by that component.

In the future, we may further divide the responsibiltiies of the `ComponentName.base.tsx` file by creating a `ComponentName.view.tsx` file with its own `ComponentNameViewProps` in the `ComponentName.types.ts` file.  When that happens, we will have to change our `index.ts` file to be more specific about which types we export:

```TypeScript
export * from './ComponentName';
export type { ComponentNameStyles, ComponentNameTypeProps, ComponentNameProps } from './ComponentName.types';
```

This will ensure that `ComponentNameViewProps` is not exported.

Exporting in this way also allows us to define a contract.  We can change the file names, folder structure, and even the object and type declarations without impacting the rest of the code.  It's not good practice to export objects and types that do not match their source name, but it becomes useful when we decide to make a design change and don't want to prevent new work while we refactor older code.  We can use the `index.ts` file to export an object or type with two names as follows:

```TypeScript
export { ComponentName, ComponentName as OldComponentName } from './ComponentName';
```

## App Architecture

index.tsx

- Initializes the Theme Provider for the app
- Initializes the redux store
- Initializes the router
  - Defines the base route as the `<App />` component
  - Defines the navigation routes

### App Component

- Initiates the automatic login flow
- Initiates the call for user info from graph
- Displays a loading indicator while logging in.
- Defines the general page structure and router outlet where the router will render the page components associated with each route.

### Pages

Pages are where GMM's business logic should live.  Pages should be composed of a `base` component, which acts like a controller or view model, and a `view` component where the UI is rendered and user interaction is handled.  A brief breakdown of responsibilities is as follows:

- Page.base
  - Contains the business logic.
  - Knows how to communicate with redux to retrieve and store the data used by the view.
  - Handles updates in the application state (redux store)
  - Prepares the data for the view by performing any special date formatting, timezone conversions, etc.
  - Renders the Page.view with the data.
  - Handles events from the view and updates the view with new data.

- Page.view
  - Contains the UI components
  - Knows how to render and update itself using data from the controller.
  - Handles user interaction.
  - Communicates with the Page.base via events or callbacks.
  - Knows how to manage states related specifically to the UI.
    - Current value of a TextBox
    - Selected item of a ComboxBox.
    - Whether a component should be disabled.

## Components

Components in the `/components` folder should be reusable UI components.  They should not know anything about redux and should not contain business logic.  They are very much like the `Page.view`, meaning they know how to render data, handler user interaction, and communicate with a higher order component using events or callbacks.  We can think about our components folder as an extension of the @fluent/* component library.

In some instances a UI component should know about the constraints it has on the shape of its data, but not why it has those constraints.  These constraints can be used to perform validation before passing the data up to a higher order component.  For example, the `<Link />`, component whose job is to collect a URL from a user, should verify that the user input is a valid URI.  When we build data constraints or validation into our UI components, we need to be thoughtful about the rules.  The way a UI component reasons about its data should be the default behavior we want every time that component is used.  If we find ourselves frequently needing to override the default validation behavior for a component, it might be better to remove the default validation altogether in favor of an `onValidate: (value: T) => boolean;` property.

### Store (redux)

- store.ts - This is where the redux store is configured.  It is where service and api implementations are created before being added to the store's middleware for dependency injection.
- *.slice.ts - A part of the application state.  Defines basic actions and reducers to manipulate the state. Also contains extra reducers to handle the various states of a thunk action (pending, fulfilled, rejected).
- *.api.ts - Contains async Thunk actions that are responsible for using Apis to retrieve and store data.

### Services

Services are responsible for wrapping 3rd party libraries used by GMM. We currently have two services: The MSALAuthenticationService and the LocalizationService.  Services should implement an interface that can be used to implement a mock for testing or offline development.  The interface should be agnostic of the 3rd party library.  For example, the IMSALAuthenticationService interface has generic methods like `login` and `getTokenAsync`.

### Apis

The GMM WebApi and MS Graph Api should be treated like 3rd party Apis.  This means that all data sent to and received from these Api should be contained in `entities`.
The method signatures of the Apis therefore should only contain primitives and types located with the `/models` folder.  The Api implementations will be responsible for transforming models to entities and entities to models.  The entities should be stored in the `/api/entities` folder.

## Strings

The `IStrings` type should define all of the strings used by GMM.  Each `Page` component should its own set of strings that are not reused by another `Page`.  This allows us to confidently change any string in a view without fear of impacting another `Page`.
