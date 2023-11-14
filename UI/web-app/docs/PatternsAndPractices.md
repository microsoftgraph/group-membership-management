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

## Differentiating between Entities and Models

Models are the application-specific representation of a piece or collection of data.  They do not necessarily match the shape of the data returned by an API or other 3rd party dependency, but often times they do.  All Apis in the `Apis` folder should communicate with their respective Api / Endpoints using entities.  The Api Interfaces should not contain references to the entities.  The Api Interfaces should accept and return models and primitive types.

## Creating and using Apis

Apis are the classes we use to communicate with 3rd party dependencies.  They use the 'axios' library for communication and are designed to use the Authentication Service to automatically authenticate requests.  All API method signatures should be described by `types` located in the src/models folder or primitive types.  Their job is to communicate with Api endpoints, which also requires them to transform models to entities and entities to models.
