# Csag.WorkloadIdentity

[![NuGet](https://img.shields.io/nuget/v/Csag.WorkloadIdentity.svg)](https://www.nuget.org/packages/Csag.WorkloadIdentity)
[![License: MIT](https://img.shields.io/badge/License-MIT-lightgray.svg)](https://github.com/neolution-ch/Csag.WorkloadIdentity/blob/main/LICENSE)

Passwordless access to Microsoft Entra ID for a .NET application, with the identity the application already runs as: the **managed identity** of the Azure resource it runs on (`ManagedIdentity`), or its **Google identity** through workload identity federation (`Google`). The library has two uses:

- **Access tokens for Azure SQL and Azure Blob Storage.** Each resource has its own configuration section that selects the identity provider. The library holds one access token per resource, refreshes it ahead of expiry, and gives it to your code as a string for `SqlConnection.AccessToken` or as a `TokenCredential` for Azure SDK clients such as `BlobServiceClient`.
- **A client assertion for a Microsoft Entra sign-in.** The `Entra` entry names the identity, and the application sends a token of that identity in place of the client secret of the sign-in app registration.

No database password, storage key, client secret or service account key is stored anywhere.

How a resource token is obtained, per provider:

- **`ManagedIdentity`.** The library requests the token from the managed identity endpoint of the Azure resource the application runs on (App Service, Container Apps, Functions, a virtual machine, AKS and so on), as the system-assigned identity of that resource or as a user-assigned identity selected by its client ID.
- **`Google`.** Using Application Default Credentials, the library asks the IAM Service Account Credentials API for a Google-signed ID token for the configured service account with the audience `api://AzureADTokenExchange`, and presents that ID token to Microsoft Entra ID as the client assertion of the identity that holds a federated credential trusting the service account: an app registration or a user-assigned managed identity.

Either way the result is a Microsoft Entra ID access token for the resource's scope, `https://database.windows.net/.default` for Azure SQL and `https://storage.azure.com/.default` for Blob Storage.

For the sign-in, the `Entra` entry names a Google service account or a managed identity, and `IEntraClientAssertionProvider` returns a token of that identity. The application sends this token to Microsoft Entra ID as the client assertion when it exchanges the sign-in code. See [Client assertion for a Microsoft Entra sign-in](#client-assertion-for-a-microsoft-entra-sign-in).

- Repository: <https://github.com/neolution-ch/Csag.WorkloadIdentity>
- Cloud and identity setup guide (managed identities, Google Cloud to Microsoft Entra ID federation, Azure SQL users, Blob Storage roles, deployment): <https://github.com/neolution-ch/Csag.WorkloadIdentity/blob/main/docs/cloud-identity-setup.md>

## Features

- Access tokens for **Azure SQL** (`IAzureSqlTokenProvider`) and **Azure Blob Storage** (`IBlobStorageTokenProvider`). Each resource is configured on its own, so the two can use different identities or different providers.
- Two identity providers with no secrets to store or rotate: the Azure managed identity the application runs as, or its Google identity through workload identity federation.
- A client assertion for a **Microsoft Entra sign-in** (`IEntraClientAssertionProvider`): a token of the Google service account or of the managed identity that replaces the client secret of the sign-in app registration.
- `WorkloadIdentityTokenCredential` presents any of the providers to Azure SDK clients as a `TokenCredential`, for example to a `BlobServiceClient`.
- Holds the current access token of each resource in memory and hands it out until it enters the configured refresh-ahead window. Callers that find no usable token share a single token request instead of each running their own.
- Background refresh (on by default): a hosted service refreshes each resource's token ahead of its expiry, so requests are served from a valid token without waiting for a token request. Failed requests are retried with exponential backoff.
- Options are validated when the host starts, so a missing or invalid value fails fast with a message that names it.
- The public services, `IAzureSqlTokenProvider`, `IBlobStorageTokenProvider`, `IEntraClientAssertionProvider` and `IGoogleIdTokenProvider`, are registered with `TryAdd`, so you can replace any of them by registering your own implementation first. The background refresh hosted service is always added; turn it off with `EnableBackgroundRefresh` instead.
- Targets `net8.0` and `net10.0`.

## Prerequisites

- An application on .NET 8 or .NET 10 that uses `Microsoft.Extensions.DependencyInjection` (ASP.NET Core, a worker service or the generic host). Microsoft's support for .NET 8 ends on 10 November 2026.
- **With `ManagedIdentity`:** the application runs on an Azure resource that has a system-assigned managed identity enabled or a user-assigned managed identity attached. That identity has a database user in Azure SQL (`CREATE USER [...] FROM EXTERNAL PROVIDER`) and/or a Blob data role (`Storage Blob Data Reader` or `Storage Blob Data Contributor`) on the storage account.
- **With `Google`:** a Google service account, with the IAM Service Account Credentials API enabled in its project (`gcloud services enable iamcredentials.googleapis.com`) and the identity the application runs as granted **Service Account OpenID Connect Identity Token Creator** (`roles/iam.serviceAccountOpenIdTokenCreator`) on it; a Microsoft Entra ID app registration or user-assigned managed identity with a federated credential that trusts the service account (issuer `https://accounts.google.com`, subject = the service account's unique ID, audience `api://AzureADTokenExchange`), holding the same database user and/or Blob role; and Application Default Credentials at runtime (on Cloud Run, GKE or Compute Engine the attached service account; on a workstation `gcloud auth application-default login`).

The setup guide linked above walks through each of these step by step.

## Quickstart A: Azure SQL from Google Cloud Run

An application on Cloud Run runs as a Google service account, so its Azure SQL token comes from the `Google` provider.

### 1. Install

```shell
dotnet add package Csag.WorkloadIdentity
```

The library returns a token string and does not depend on `Microsoft.Data.SqlClient`. Add that package, or an EF Core provider such as `Microsoft.EntityFrameworkCore.SqlServer`, to the project that opens the connections.

### 2. Configure

The options bind from the `Csag.WorkloadIdentity` section of the configuration. The `AzureSql` section selects the provider and carries that provider's settings:

```json
{
  "Csag.WorkloadIdentity": {
    "AzureSql": {
      "Provider": "Google",
      "Google": {
        "TenantId": "<entra-tenant-id>",
        "ClientId": "<client-id-of-the-federated-credential-holder>",
        "ServiceAccountEmail": "<name>@<project-id>.iam.gserviceaccount.com"
      }
    }
  },
  "ConnectionStrings": {
    "AzureSql": "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;Authentication=Active Directory Default"
  }
}
```

`ClientId` is the application (client) ID of the app registration, or the client ID of the user-assigned managed identity, that holds the federated credential. On Cloud Run supply the same keys as environment variables, with `__` as the section separator: `Csag.WorkloadIdentity__AzureSql__Provider`, `Csag.WorkloadIdentity__AzureSql__Google__TenantId`, `Csag.WorkloadIdentity__AzureSql__Google__ClientId`, `Csag.WorkloadIdentity__AzureSql__Google__ServiceAccountEmail` and `ConnectionStrings__AzureSql`. The connection string deliberately carries no credentials; see the notes below.

### 3. Register

```csharp
using Csag.WorkloadIdentity;

var builder = WebApplication.CreateBuilder(args);

// Binds the "Csag.WorkloadIdentity" section of the host configuration.
builder.Services.AddWorkloadIdentity();

var app = builder.Build();
app.Run();
```

The options reference below shows the other two overloads: binding a given `IConfiguration`, and configuring the options in code.

### 4. Use the token

`SqlClient` can obtain the token in two ways: a `SqlAuthenticationProvider` serves every connection the application opens, including those that EF Core or other libraries open, and `SqlConnection.AccessToken` serves a single connection you open yourself. Either way, asking `IAzureSqlTokenProvider` for the token is cheap: until the held token enters the refresh-ahead window the call returns it without any network round trip; inside the window (reachable only when background refresh is off or has been failing) the first caller requests a new token and concurrent callers wait for that one request.

#### With a `SqlAuthenticationProvider`

Install a provider for `ActiveDirectoryDefault` at startup. `SqlClient` then asks it for the token whenever it opens a connection whose connection string says `Authentication=Active Directory Default`, so EF Core contexts and connections that other libraries open, such as a SQL Server distributed cache, need no further code. The repository's Demo project uses this approach.

```csharp
using Csag.WorkloadIdentity.Abstractions;
using Microsoft.Data.SqlClient;

public sealed class FederatedSqlAuthenticationProvider(IAzureSqlTokenProvider tokenProvider) : SqlAuthenticationProvider
{
    public override bool IsSupported(SqlAuthenticationMethod authenticationMethod)
        => authenticationMethod == SqlAuthenticationMethod.ActiveDirectoryDefault;

    public override async Task<SqlAuthenticationToken> AcquireTokenAsync(SqlAuthenticationParameters parameters)
    {
        // ConnectionTimeout bounds the token request; 0 means no limit.
        using var timeout = new CancellationTokenSource();
        if (parameters.ConnectionTimeout > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(parameters.ConnectionTimeout));
        }

        var accessToken = await tokenProvider.GetAccessTokenAsync(timeout.Token);
        return new SqlAuthenticationToken(accessToken.Token, accessToken.ExpiresOn);
    }
}
```

Install the provider once the host is built, before anything opens a connection. With EF Core, register a pooled context factory plus a scoped context, and inject `AppDbContext` (your `DbContext`) where you need it, or `IDbContextFactory<AppDbContext>` where you manage the context's lifetime yourself:

```csharp
using Csag.WorkloadIdentity;
using Csag.WorkloadIdentity.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWorkloadIdentity();
builder.Services.AddPooledDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AzureSql")));
builder.Services.AddScoped(serviceProvider => serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

var app = builder.Build();

if (!SqlAuthenticationProvider.SetProvider(
        SqlAuthenticationMethod.ActiveDirectoryDefault,
        new FederatedSqlAuthenticationProvider(app.Services.GetRequiredService<IAzureSqlTokenProvider>())))
{
    throw new InvalidOperationException("Could not install the federated SQL authentication provider.");
}

app.Run();
```

#### With `SqlConnection.AccessToken`

To authenticate a connection you open yourself, resolve `IAzureSqlTokenProvider` (namespace `Csag.WorkloadIdentity.Abstractions`), call `GetAzureSqlAccessTokenAsync` and assign the result to `SqlConnection.AccessToken` before opening the connection. Do this for every new connection. The connection string must not contain an `Authentication` keyword, because `SqlClient` throws an `InvalidOperationException` when you set `AccessToken` on such a connection. The `AzureSql` connection string from step 2 has `Authentication=Active Directory Default` for the `SqlAuthenticationProvider`, so configure a second connection string without that keyword for this approach. The sample below reads it as `ConnectionStrings:AzureSqlAccessToken` (environment variable `ConnectionStrings__AzureSqlAccessToken`):

```json
{
  "ConnectionStrings": {
    "AzureSqlAccessToken": "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True"
  }
}
```

```csharp
using Csag.WorkloadIdentity.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

public sealed class OrderRepository(IConfiguration configuration, IAzureSqlTokenProvider tokenProvider)
{
    private readonly string connectionString = configuration.GetConnectionString("AzureSqlAccessToken")
        ?? throw new InvalidOperationException("Connection string 'AzureSqlAccessToken' is not configured.");

    public async Task<int> CountOrdersAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        connection.AccessToken = await tokenProvider.GetAzureSqlAccessTokenAsync(cancellationToken);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Orders", connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }
}
```

Register the class as usual, for example `builder.Services.AddScoped<OrderRepository>();`.

## Quickstart B: Azure SQL and Blob Storage from Azure App Service

An application on App Service (or Container Apps, Functions, a virtual machine, AKS) has a managed identity, so both tokens come from the `ManagedIdentity` provider. Blob Storage is reached through the Azure SDK, with `WorkloadIdentityTokenCredential` adapting the library's provider to the `TokenCredential` the SDK expects.

### 1. Install

```shell
dotnet add package Csag.WorkloadIdentity
dotnet add package Azure.Storage.Blobs
```

Add `Microsoft.Data.SqlClient` or an EF Core provider for Azure SQL as in quickstart A.

### 2. Configure

With the App Service's system-assigned identity for both resources:

```json
{
  "Csag.WorkloadIdentity": {
    "AzureSql": {
      "Provider": "ManagedIdentity",
      "ManagedIdentity": { "UseSystemAssignedIdentity": true }
    },
    "BlobStorage": {
      "Provider": "ManagedIdentity",
      "ManagedIdentity": { "UseSystemAssignedIdentity": true }
    }
  },
  "ConnectionStrings": {
    "AzureSql": "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;Authentication=Active Directory Default"
  },
  "BlobStorage": {
    "ServiceUri": "https://<storage-account>.blob.core.windows.net"
  }
}
```

For a user-assigned managed identity, name it by its client ID instead:

```json
"ManagedIdentity": { "ClientId": "<user-assigned-managed-identity-client-id>" }
```

`Provider` defaults to `ManagedIdentity`, so it could be omitted here; it is written out for clarity. The two resources are independent: one can use the system-assigned identity and the other a user-assigned one, or the `Google` provider. In App Service, supply the values as application settings with the same `__` separator, for example `Csag.WorkloadIdentity__AzureSql__ManagedIdentity__UseSystemAssignedIdentity` = `true`. `BlobStorage:ServiceUri` is the application's own setting, read below.

### 3. Register

```csharp
using Azure.Storage.Blobs;
using Csag.WorkloadIdentity;
using Csag.WorkloadIdentity.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWorkloadIdentity();

// One BlobServiceClient for the application. The adapter serves every token request of the client from the
// library's held Blob Storage token, so the client needs neither a key nor a connection string.
builder.Services.AddSingleton(serviceProvider => new BlobServiceClient(
    new Uri(builder.Configuration["BlobStorage:ServiceUri"]
        ?? throw new InvalidOperationException("Setting 'BlobStorage:ServiceUri' is not configured.")),
    new WorkloadIdentityTokenCredential(serviceProvider.GetRequiredService<IBlobStorageTokenProvider>())));

var app = builder.Build();
app.Run();
```

Azure SDK clients are thread-safe and meant to be shared, so a singleton is the right lifetime for the `BlobServiceClient`.

### 4. Use the tokens

Azure SQL works exactly as in quickstart A: install the `SqlAuthenticationProvider`, or assign `SqlConnection.AccessToken` to connections you open yourself. For Blob Storage inject the `BlobServiceClient`; the SDK obtains a token through the credential as needed (its bearer-token policy caches the token and asks again only when it is missing or near expiry), and the adapter serves each of those requests from the provider's held token:

```csharp
using Azure.Storage.Blobs;

public sealed class DocumentStore(BlobServiceClient blobServiceClient)
{
    public async Task<string> ReadTextAsync(string containerName, string blobName, CancellationToken cancellationToken)
    {
        var blobClient = blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        var download = await blobClient.DownloadContentAsync(cancellationToken);
        return download.Value.Content.ToString();
    }
}
```

`IBlobStorageTokenProvider.GetBlobStorageAccessTokenAsync` returns the raw token string for code that calls the Blob REST API directly and sets the `Authorization: Bearer` header itself. `WorkloadIdentityTokenCredential` accepts any of the library's providers (`IAccessTokenProvider`), but each provider is bound to one resource and the adapter ignores the scopes an SDK client asks for: wrap the provider that matches the client, `IBlobStorageTokenProvider` for `BlobServiceClient`, never the Azure SQL provider, whose token carries the SQL audience.

## Client assertion for a Microsoft Entra sign-in

An application that signs users in with Microsoft Entra ID through OpenID Connect receives an authorization code. It exchanges the code at the token endpoint of Microsoft Entra ID. In this token request, the application must authenticate as the client. Usually, a client secret of the sign-in app registration does this. With the `Entra` entry, the application sends a client assertion in place of the secret. The client assertion (the OAuth 2.0 parameter `client_assertion`) is a token of the identity of the application: a Google ID token of its service account, or a token of its managed identity on Azure. A federated credential on the sign-in app registration trusts that identity. No secret exists, so nobody creates, stores or renews one.

### Configure

On Google Cloud, name the service account:

```json
{
  "Csag.WorkloadIdentity": {
    "Entra": {
      "Provider": "Google",
      "Google": {
        "ServiceAccountEmail": "<name>@<project-id>.iam.gserviceaccount.com"
      }
    }
  }
}
```

On Azure, name the managed identity as for a resource: a user-assigned identity by its client ID, or the system-assigned identity with `"UseSystemAssignedIdentity": true`:

```json
{
  "Csag.WorkloadIdentity": {
    "Entra": {
      "Provider": "ManagedIdentity",
      "ManagedIdentity": {
        "ClientId": "<user-assigned-managed-identity-client-id>"
      }
    }
  }
}
```

The entry holds no tenant ID and no client ID of the sign-in, because the sign-in configuration of the application already has them. `Provider` defaults to `Google`. All Microsoft Entra sign-ins of one application use the same identity.

Prefer a user-assigned identity. A system-assigned identity gets a new object ID when you delete the Azure resource and create it again, and the federated credential must then name the new ID. [Certificateless authentication with Microsoft.Identity.Web](https://learn.microsoft.com/entra/msidweb/authentication/certificateless) documents both kinds of identity for this setup. [Configure an application to trust a managed identity](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity) says "You can only use User-Assigned Managed Identities as a credential" in its portal instructions. The Azure CLI command `az ad app federated-credential create` creates the federated credential for a system-assigned identity too.

On the sign-in app registration, add a federated credential with the audience `api://AzureADTokenExchange` and these values:

| Provider | Issuer | Subject |
|---|---|---|
| `Google` | `https://accounts.google.com` | The unique ID of the service account |
| `ManagedIdentity` | `https://login.microsoftonline.com/<tenant-id>/v2.0` | The object (principal) ID of the managed identity |

With `Google`, the Google side is the same as for the `Google` provider of a resource. The IAM Service Account Credentials API is enabled, and the identity the application runs as has `roles/iam.serviceAccountOpenIdTokenCreator` on the service account. With `ManagedIdentity`, the identity is attached to the Azure resource the application runs on, and it is in the same tenant as the sign-in app registration. The [setup guide](https://github.com/neolution-ch/Csag.WorkloadIdentity/blob/main/docs/cloud-identity-setup.md#f-microsoft-entra-sign-in-without-a-client-secret) gives the steps.

### Use the client assertion

The code is the same for both providers; only the configuration differs. Register the library with `AddWorkloadIdentity()`. In the OpenID Connect handler, set the assertion on the token request when the handler receives the authorization code:

```csharp
using Csag.WorkloadIdentity.Abstractions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddAuthentication()
    .AddOpenIdConnect("microsoft", options =>
    {
        // Authority, ClientId, ResponseType = "code" and the other settings of the sign-in; no ClientSecret.
        options.Events.OnAuthorizationCodeReceived = async context =>
        {
            var assertionProvider = context.HttpContext.RequestServices.GetRequiredService<IEntraClientAssertionProvider>();
            var tokenRequest = context.TokenEndpointRequest!;
            tokenRequest.ClientSecret = null;
            tokenRequest.ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
            tokenRequest.ClientAssertion = await assertionProvider.GetClientAssertionAsync(context.HttpContext.RequestAborted);
        };
    });
```

With `Google`, each call of `GetClientAssertionAsync` gets a new ID token from Google, so each sign-in makes one call to Google. With `ManagedIdentity`, the credential returns its cached token until the token is close to expiry. The library does not hold the token itself, and the background refresh does not include the `Entra` entry. Resolving `IEntraClientAssertionProvider` without the `Entra` entry throws an `InvalidOperationException` that names the entry.

## Options reference

All keys live under the `Csag.WorkloadIdentity` section. `<resource>` stands for `AzureSql` or `BlobStorage`; the two sections have the same shape and are independent of each other. The `Entra` entry has fewer keys than a resource: the library does not send the token request of the sign-in, so the entry needs no tenant ID and no client ID.

| Key | Required | Default | Description |
|---|---|---|---|
| `Entra` | no | – | Configures the client assertion for a Microsoft Entra sign-in. Without it, resolving `IEntraClientAssertionProvider` throws. |
| `Entra:Provider` | no | `Google` | `Google` or `ManagedIdentity`. Selects which of the two provider sections is read; the other is ignored. |
| `Entra:Google:ServiceAccountEmail` | with `Google` | – | The Google service account the client assertion is minted for. The federated credential of the sign-in app registration names this account's unique ID as its subject. |
| `Entra:ManagedIdentity:UseSystemAssignedIdentity` | no | `false` | `true` uses the system-assigned identity of the hosting resource; `ClientId` is then ignored. A user-assigned identity is preferred, because its object ID does not change. |
| `Entra:ManagedIdentity:ClientId` | with `ManagedIdentity`, unless `UseSystemAssignedIdentity` is `true` | – | Client ID of the user-assigned managed identity. The federated credential of the sign-in app registration names the object ID of the identity as its subject. |
| `AzureSql` | no | – | Configures how Azure SQL tokens are obtained. Without it, resolving `IAzureSqlTokenProvider` throws. |
| `BlobStorage` | no | – | Configures how Blob Storage tokens are obtained. Without it, resolving `IBlobStorageTokenProvider` throws. |
| `<resource>:Provider` | no | `ManagedIdentity` | `ManagedIdentity` or `Google`. Selects which of the two provider sections is read; the other is ignored. |
| `<resource>:ManagedIdentity:UseSystemAssignedIdentity` | no | `false` | `true` uses the system-assigned identity of the hosting resource; `ClientId` is then ignored. |
| `<resource>:ManagedIdentity:ClientId` | with `ManagedIdentity`, unless `UseSystemAssignedIdentity` is `true` | – | Client ID of the user-assigned managed identity. |
| `<resource>:Google:TenantId` | with `Google` | – | Directory (tenant) ID of the Microsoft Entra tenant that issues the access token. |
| `<resource>:Google:ClientId` | with `Google` | – | Application (client) ID of the app registration, or client ID of the user-assigned managed identity, that holds the federated credential. |
| `<resource>:Google:ServiceAccountEmail` | with `Google` | – | The Google service account the ID token is minted for. The federated credential names this account's unique ID as its subject. |
| `RefreshAheadWindow` | no | `00:05:00` | How long before an access token expires it is treated as due for refresh. Applies to every resource. Must be positive and at most five minutes: the credentials return their cached token until five minutes before expiry, so a longer window cannot refresh earlier. |
| `EnableBackgroundRefresh` | no | `true` | Whether a hosted service keeps the token of every configured resource refreshed ahead of its expiry. |

Environment variables follow the usual .NET mapping, with `__` as the section separator and the `.` of the section name kept as is: `Csag.WorkloadIdentity__AzureSql__Provider`, `Csag.WorkloadIdentity__BlobStorage__ManagedIdentity__ClientId`, `Csag.WorkloadIdentity__RefreshAheadWindow`, and so on.

### Registering with a given configuration or in code

Besides `AddWorkloadIdentity()`, which binds the section from the `IConfiguration` registered in the container, two overloads exist. Given an `IServiceCollection services` and an `IConfiguration configuration`, either bind the section of a given configuration:

```csharp
using Csag.WorkloadIdentity;

// Binds the "Csag.WorkloadIdentity" section of the given configuration.
services.AddWorkloadIdentity(configuration);
```

Or set the options in code:

```csharp
using Csag.WorkloadIdentity;
using Csag.WorkloadIdentity.Options;

// The section name is available as WorkloadIdentityOptions.ConfigurationSectionName.
services.AddWorkloadIdentity(options =>
{
    options.AzureSql = new WorkloadIdentityResourceOptions
    {
        Provider = WorkloadIdentityProvider.Google,
        Google = new GoogleOptions
        {
            TenantId = "<entra-tenant-id>",
            ClientId = "<client-id-of-the-federated-credential-holder>",
            ServiceAccountEmail = "<name>@<project-id>.iam.gserviceaccount.com",
        },
    };
    options.BlobStorage = new WorkloadIdentityResourceOptions
    {
        Provider = WorkloadIdentityProvider.ManagedIdentity,
        ManagedIdentity = new ManagedIdentityOptions { UseSystemAssignedIdentity = true },
    };
    options.RefreshAheadWindow = TimeSpan.FromMinutes(3);
    options.EnableBackgroundRefresh = true;
});
```

`WorkloadIdentityOptions`, `WorkloadIdentityResourceOptions`, `EntraClientAssertionOptions`, `WorkloadIdentityProvider`, `GoogleOptions`, `GoogleServiceAccountOptions` and `ManagedIdentityOptions` live in `Csag.WorkloadIdentity.Options`; the provider interfaces and `IAccessTokenProvider` in `Csag.WorkloadIdentity.Abstractions`; `AddWorkloadIdentity` and `WorkloadIdentityTokenCredential` in `Csag.WorkloadIdentity`.

Every section is optional: without any section the host starts, and the services do nothing until a section is configured. The options are validated when the host starts: a missing value in the provider section of a configured resource or of the `Entra` entry, or a `RefreshAheadWindow` that is not positive or exceeds five minutes, throws an `OptionsValidationException` that names every offending value by its path, for example `AzureSql:Google:ServiceAccountEmail must be provided.` A `Provider` value other than `ManagedIdentity` or `Google` fails earlier, when the configuration is bound. Calling `AddWorkloadIdentity` more than once does not register the services twice. Both token providers and the client assertion provider are always registered; resolving one whose section is absent throws an `InvalidOperationException` that names the missing section.

## Migrating from Csag.AzureSqlFederatedIdentity / Neolution.AzureSqlFederatedIdentity

`Csag.WorkloadIdentity` continues the `Neolution.AzureSqlFederatedIdentity` package (briefly renamed `Csag.AzureSqlFederatedIdentity`, without a release under that name). Azure SQL over Google federation works as before and needs no change on the Google or Microsoft side; what changes is the naming and the shape of the configuration.

1. **Package and namespaces.** Replace the package reference with `Csag.WorkloadIdentity`, and the `Neolution.AzureSqlFederatedIdentity` (or `Csag.AzureSqlFederatedIdentity`) namespace prefix with `Csag.WorkloadIdentity` in `using` directives: `Csag.WorkloadIdentity.Abstractions` for the provider interfaces, `Csag.WorkloadIdentity.Options` for the options types. `Csag.WorkloadIdentity` does not reference `Microsoft.Data.SqlClient`. If your project has no direct reference to it or to an EF Core SQL Server provider, add one.
2. **Registration.** `AddAzureSqlFederatedIdentity` becomes `AddWorkloadIdentity`, with the same three overloads (host configuration, `IConfiguration`, configure in code). `AzureSqlFederatedIdentityOptions` becomes `WorkloadIdentityOptions`, whose `ConfigurationSectionName` is `Csag.WorkloadIdentity`.
3. **Configuration.** The section is renamed and becomes resource-first: `TenantId` and `ClientId` move into the `Google` section, and that section moves under the resource it serves, `AzureSql`, next to `"Provider": "Google"`.

   Before:

   ```json
   {
     "Neolution.AzureSqlFederatedIdentity": {
       "TenantId": "<entra-tenant-id>",
       "ClientId": "<app-registration-client-id>",
       "Google": {
         "ServiceAccountEmail": "<name>@<project-id>.iam.gserviceaccount.com"
       }
     }
   }
   ```

   After:

   ```json
   {
     "Csag.WorkloadIdentity": {
       "AzureSql": {
         "Provider": "Google",
         "Google": {
           "TenantId": "<entra-tenant-id>",
           "ClientId": "<app-registration-client-id>",
           "ServiceAccountEmail": "<name>@<project-id>.iam.gserviceaccount.com"
         }
       }
     }
   }
   ```

   Environment variables move the same way: `Neolution.AzureSqlFederatedIdentity__TenantId` becomes `Csag.WorkloadIdentity__AzureSql__Google__TenantId`, and so on.
4. **Unchanged.** `IAzureSqlTokenProvider.GetAzureSqlAccessTokenAsync` and assigning the token to `SqlConnection.AccessToken`; the token is held in memory, and a hosted service keeps it refreshed. Quickstart A also shows a `SqlAuthenticationProvider`, which serves EF Core and every other connection without a factory.
5. **New or removed.** `RefreshAheadWindow` and `EnableBackgroundRefresh` are optional root keys; keep their defaults unless you have a reason not to, and keep `RefreshAheadWindow` at five minutes or less. `IGoogleIdTokenProvider.GetIdTokenAsync` now takes the service account email, so resources can federate through different service accounts. `IAzureSqlTokenExchanger` no longer exists; the exchange is performed by internal per-provider services, and `IAzureSqlTokenProvider` remains the seam to substitute. The `AzureSqlTokenProvider` class is internal; depend on `IAzureSqlTokenProvider`. `AddWorkloadIdentity` does not register `IMemoryCache`. If your application uses `IMemoryCache`, call `AddMemoryCache()` yourself.

## Notes

- **Connection string.** The access token is the credential, so the connection string must not contain `User ID`/`Password` or `Integrated Security`. With a `SqlAuthenticationProvider` it must say `Authentication=Active Directory Default`; with `SqlConnection.AccessToken` it must not contain an `Authentication` keyword, because `SqlClient` throws an `InvalidOperationException` when `AccessToken` is combined with conflicting authentication settings. Keep `Encrypt=True` so the token and your data travel over TLS.
- **Google runtime identity.** The Google ID token is requested through Application Default Credentials (ADC). Whatever identity ADC resolves to (the service account attached to the Cloud Run, GKE or Compute Engine resource, or your own account after `gcloud auth application-default login`) must hold `roles/iam.serviceAccountOpenIdTokenCreator` on the configured `ServiceAccountEmail`. The intended deployment is the simplest one: the application runs *as* that service account, with the role granted to the account on itself. Two resources may name different service accounts; each is exchanged through its own credential.
- **Managed identity at runtime.** The token comes from the identity endpoint of the Azure resource the application runs on, so a system-assigned identity must be enabled on that resource and a user-assigned one attached to it. A workstation has no such endpoint; `ManagedIdentity` only works on Azure. A `ClientId` next to `"UseSystemAssignedIdentity": true` is ignored.
- **Token handling.** The provider holds the token; do not cache or persist it yourself. When you assign `SqlConnection.AccessToken`, the token is part of the `SqlClient` connection pool key, so a refreshed token starts a new pool, which is expected. If your connection string sets `Min Pool Size` above zero, call `SqlConnection.ClearPool` with a connection that carries the old token once it has expired; otherwise the pool keeps that token's physical connections open indefinitely (see the [`AccessToken` remarks](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection.accesstoken)). Alternatively, set `SqlConnection.AccessTokenCallback` (Microsoft.Data.SqlClient 5.2 or later) to a delegate that returns `new SqlAuthenticationToken(token.Token, token.ExpiresOn)` from `IAzureSqlTokenProvider.GetAccessTokenAsync`. The callback is part of the pool key, and `SqlClient` compares it by its method and its target. To keep a single pool across token refreshes, create the delegate once, in a static field or a singleton, and assign that same delegate to every connection. A lambda that captures a local variable is a new delegate for each connection and gets a pool of its own; a lambda that captures a scoped service gets one pool per scope.
- **Background refresh.** When enabled, the hosted service runs one loop per configured resource, so a slow or failing resource does not delay the other. Each loop requests a token as soon as the host starts and again when the held token enters the refresh-ahead window, or at half the token's remaining lifetime when that is shorter than the window (the wait is kept between 10 seconds and 1 day). A failed request is logged at `Error` and retried, waiting 5 seconds and doubling up to 5 minutes between attempts. When disabled, the first caller to find the token due for refresh performs the request while concurrent callers wait for its result. If you register your own `IAzureSqlTokenProvider` or `IBlobStorageTokenProvider`, the hosted service leaves it alone.
- **Logging.** All categories start with `Csag.WorkloadIdentity`. Token requests and refreshes log at `Debug`; per-call reuse of the held token logs at `Trace`.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| The host fails to start with `OptionsValidationException` | A required key missing in a configured section, or a `RefreshAheadWindow` that is not positive or exceeds five minutes; the message names every offending value. |
| The host fails to start with `InvalidOperationException: Failed to convert configuration value … 'Provider'` | The `Provider` value is not `ManagedIdentity` or `Google`. |
| `InvalidOperationException`: "The AzureSql resource is not configured" (or `BlobStorage`) | `IAzureSqlTokenProvider` or `IBlobStorageTokenProvider` was resolved, but the configuration has no section for that resource. |
| `InvalidOperationException`: "The Entra entry is not configured" | `IEntraClientAssertionProvider` was resolved, but the configuration has no `Entra` entry. |
| `CredentialUnavailableException`: no managed identity endpoint found | `ManagedIdentity` is selected but the application is not running on an Azure resource with a managed identity, for example on a workstation. |
| The managed identity endpoint reports that the identity was not found | The user-assigned identity is not attached to the hosting resource, or `ManagedIdentity:ClientId` is wrong. |
| `RpcException` with status `PermissionDenied` in the log; the caller receives it as the inner exception of an `AuthenticationFailedException` | The runtime identity lacks `roles/iam.serviceAccountOpenIdTokenCreator` on the service account, or the IAM Service Account Credentials API is not enabled in the project. |
| "Failed to create IAMCredentialsClient" in the log | No Application Default Credentials were found; set the runtime service account, or run `gcloud auth application-default login` on a workstation. |
| Microsoft Entra ID rejects the assertion because no matching federated identity credential was found | The federated credential's issuer, subject (the service account's unique ID) or audience does not match the ID token, or `Google:ClientId` names a different identity than the one holding the credential. |
| Azure SQL reports "Login failed for user" | No database user exists for the identity in the target database, or the server's network rules block the connection. |
| Blob Storage returns `403` with `AuthorizationPermissionMismatch` | The identity has no Blob data role on the storage account or container, or the role assignment has not propagated yet (it can take up to 10 minutes). |

## License

MIT. See <https://github.com/neolution-ch/Csag.WorkloadIdentity/blob/main/LICENSE>.

## Contributing and security

Issues and pull requests are welcome at <https://github.com/neolution-ch/Csag.WorkloadIdentity>; the repository's `CONTRIBUTING.md` explains the build, the tests and the release process. Please report vulnerabilities privately through GitHub's private vulnerability reporting: <https://github.com/neolution-ch/Csag.WorkloadIdentity/security/advisories/new>.
