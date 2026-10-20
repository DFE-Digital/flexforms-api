# FlexForms API Client

The .NET HTTP client used by FlexForms to communicate with the FlexForms API (`GovUK.Dfe.FlexForms.Api`).

> **Important:** This package is specific to, and dedicated to, the FlexForms service. It is published publicly only so that FlexForms components can consume it. It is **not** intended for use by any other service, and no support, stability or backwards-compatibility guarantees are offered to consumers outside FlexForms. Breaking changes may be released at any time to suit FlexForms' needs.

## Overview

FlexForms is the DfE service for building and running configurable, multi-tenant application forms. This library contains the typed clients, contracts and authentication plumbing that FlexForms front-end hosts (such as FlexForms Web) use to call the FlexForms API on behalf of signed-in users.

The clients and DTOs are generated with NSwag from the FlexForms API's OpenAPI specification, so they always reflect the shape of the API at the version the package was built from.

## Key Features

- **Typed API clients and contracts** - NSwag-generated clients and DTOs for applications, templates, users, roles, tenant administration, files, notifications, reporting and token exchange.
- **Token exchange** - exchanges a user's DfE Sign-In / Entra identity for a FlexForms API token, with caching and refresh handled by `TokenManagementMiddleware`.
- **Azure bearer tokens** - service-to-service authentication against the API using Azure AD tokens, from either a client secret or the host's managed identity.
- **Header forwarding** - forwards FlexForms context headers (tenant, template and application) from the incoming request to the API.
- **User auto-registration** - optionally registers users with the API on first sign-in.
- **Dependency injection registration** - a single `AddExternalApplicationsApiClient<TInterface, TImplementation>()` call wires up a client and its HTTP handlers.

## Installation

For FlexForms projects only:

```sh
dotnet add package GovUK.Dfe.FlexForms.Api.Client
```

## Service authentication

Service tokens are configured in the `ExternalApplicationsApiClient` section. Use a client secret:

```json
"ExternalApplicationsApiClient": {
  "BaseUrl": "https://<flexforms-api-host>/",
  "Authority": "https://login.microsoftonline.com/<entra-tenant-id>",
  "ClientId": "<client-app-id>",
  "ClientSecret": "<client-secret>",
  "Scope": "api://<flexforms-api-app-id>/.default"
}
```

Or the host's managed identity, in which case `ClientId`, `ClientSecret` and `Authority` are ignored:

```json
"ExternalApplicationsApiClient": {
  "BaseUrl": "https://<flexforms-api-host>/",
  "UseManagedIdentity": true,
  "ManagedIdentityClientId": "<user-assigned-identity-client-id>",
  "Scope": "api://<flexforms-api-app-id>/.default"
}
```

Leave `ManagedIdentityClientId` unset to use the system-assigned identity. Any app roles the API requires must be assigned to the identity's service principal.
