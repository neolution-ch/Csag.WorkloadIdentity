# @neolution-ch/csag-workload-identity

## 0.1.1

### Patch Changes

- [#16](https://github.com/neolution-ch/Csag.WorkloadIdentity/pull/16) [`7e357ae`](https://github.com/neolution-ch/Csag.WorkloadIdentity/commit/7e357ae90f4b0c036b15c571a2304bd01c9c6c03) Thanks [@dependabot](https://github.com/apps/dependabot)! - Bump the nuget-ecosystem group with 6 updates
  
  | Package | From | To | Bump |
  |---------|------|----|------|
  | Azure.Core | 1.53.0 | 1.63.0 | 🟡 minor |
  | Azure.Storage.Blobs | 12.28.0 | 12.30.1 | 🟡 minor |
  | coverlet.collector | 10.0.1 | 10.1.0 | 🟡 minor |
  | Grpc.Core.Api | 2.83.0 | 2.84.0 | 🟡 minor |
  | Microsoft.Data.SqlClient | 7.0.3 | 7.1.1 | 🟡 minor |
  | xunit.runner.visualstudio | 3.1.1 | 4.0.0 | 🔴 major |

- [#20](https://github.com/neolution-ch/Csag.WorkloadIdentity/pull/20) [`75befaf`](https://github.com/neolution-ch/Csag.WorkloadIdentity/commit/75befaf3e8ee7c0c04dd1bc771e2f27dbf23002b) Thanks [@neoscie](https://github.com/neoscie)! - Correct the package README and the cloud setup guide.
  
  - The `SqlConnection.AccessToken` sample now reads its own connection string, `ConnectionStrings:AzureSqlAccessToken`, which has no `Authentication` keyword. `SqlClient` throws an `InvalidOperationException` when you set `AccessToken` on a connection string that has this keyword.
  - `SqlConnection.AccessTokenCallback` keeps a single pool only when all connections get the same delegate. The README now tells you to create the delegate one time.
  - The README now shows the two overloads under "Registering with a given configuration or in code" as alternatives.
  - The migration section now tells you that `Csag.WorkloadIdentity` does not reference `Microsoft.Data.SqlClient` and does not register `IMemoryCache`. `Neolution.AzureSqlFederatedIdentity` did both.
  - The example of object-level grants in the setup guide no longer grants `EXECUTE` on the full `dbo` schema. The setup guide also quotes error 33134, which Azure SQL reports when the server identity is missing.

## 0.1.0

### Minor Changes

- [#5](https://github.com/neolution-ch/Csag.WorkloadIdentity/pull/5) [`0d4d90e`](https://github.com/neolution-ch/Csag.WorkloadIdentity/commit/0d4d90e9630fad5adfe0fb8e8c1175dfd4e0ce80) Thanks [@neotrow](https://github.com/neotrow)! - Target `net8.0` and `net10.0`, and tidy the dependency surface.
  
  - The unused `Microsoft.Data.SqlClient` dependency is gone; attach the token to your own `SqlConnection` exactly as before.
  - `Microsoft.Extensions.*` dependencies move to 10.x (still consumable from net8.0 hosts); `Azure.Identity` 1.21 and `Google.Cloud.Iam.Credentials.V1` 2.5.
  - The debug-only decoding and logging of the Google ID token's claims is removed.
  - Package metadata: `<Version>` is declared, deterministic/SourceLink builds are enabled, and symbols ship as `.snupkg`.

- [#4](https://github.com/neolution-ch/Csag.WorkloadIdentity/pull/4) [`289f540`](https://github.com/neolution-ch/Csag.WorkloadIdentity/commit/289f5406d2810a9beff0598960ff26fb42f59e56) Thanks [@neotrow](https://github.com/neotrow)! - Rename the package to `Csag.WorkloadIdentity` (previously `Neolution.AzureSqlFederatedIdentity`).
  
  The root namespace and the configuration section key change to `Csag.WorkloadIdentity`; update `using` directives and `appsettings.json` accordingly. NuGet metadata now names collana solutions AG.

- [#6](https://github.com/neolution-ch/Csag.WorkloadIdentity/pull/6) [`5a896c2`](https://github.com/neolution-ch/Csag.WorkloadIdentity/commit/5a896c2edab231a3b7d1bcd7b3b636bc844f3408) Thanks [@neotrow](https://github.com/neotrow)! - Harden token acquisition, refresh and registration.
  
  - Concurrent callers that find no usable token share a single token exchange instead of each running their own. A failed exchange is not retained, so the next caller simply retries.
  - The provider holds the current token itself; `IMemoryCache` is no longer used or registered, so hosts that configure a cache `SizeLimit` or compact their cache are unaffected. `AzureSqlTokenProvider` is now sealed and disposable, and its constructor takes a `TimeProvider` instead of an `IMemoryCache`.
  - The background refresh service refreshes ahead of the token's own expiry (`ExpiresOn` minus `RefreshAheadWindow`) rather than on a fixed interval, retries with exponential backoff when an exchange fails, and stops quietly on host shutdown. It can be turned off with `EnableBackgroundRefresh = false`.
  - New options: `RefreshAheadWindow` (default five minutes; must be positive and at most five minutes, because the credential returns its cached token until five minutes before expiry) replaces the hard-coded skew and interval, and `EnableBackgroundRefresh` (default `true`). A token that arrives with less than the window remaining is still used and replaced on the next call.
  - The `Google` configuration section is now required; a missing section fails validation with a clear message instead of an `ArgumentNullException` when the provider is resolved.
  - Options are bound with `BindConfiguration` and validated at host startup (`ValidateOnStart`); a failed validation reports every invalid value at once. `AddAzureSqlFederatedIdentity()` without arguments binds the `Csag.WorkloadIdentity` section from the host configuration; the section name is exposed as `AzureSqlFederatedIdentityOptions.ConfigurationSectionName`. The `Action<AzureSqlFederatedIdentityOptions>` and `IConfiguration` overloads remain.
  - Services are registered with `TryAdd*`, so a consumer-registered `IAzureSqlTokenProvider` (or any other service in the pipeline) takes precedence and repeated registration calls do not duplicate services or the hosted service. A substituted provider is left alone by the background refresh service.
  - The Google IAM Credentials client is created once, and one Azure `ClientAssertionCredential` per tenant and client, and both are reused across token exchanges.

- [#10](https://github.com/neolution-ch/Csag.WorkloadIdentity/pull/10) [`4aa62b1`](https://github.com/neolution-ch/Csag.WorkloadIdentity/commit/4aa62b17ee128fdbca4e39a4d58ca57f80ef7103) Thanks [@neotrow](https://github.com/neotrow)! - Generalise the library into a resource-first workload identity model: access tokens for Azure SQL and Azure Blob Storage, each obtained either with the Azure managed identity the application runs as or with its Google identity through workload identity federation.
  
  - **Configuration is resource-first (breaking).** Each resource has its own section under `Csag.WorkloadIdentity` that selects a `Provider` (`ManagedIdentity` or `Google`) and carries that provider's settings. For the existing Azure SQL over Google federation case:
  
    Before:
  
    ```json
    {
      "Csag.WorkloadIdentity": {
        "TenantId": "<tenant-id>",
        "ClientId": "<client-id>",
        "Google": { "ServiceAccountEmail": "<name>@<project-id>.iam.gserviceaccount.com" }
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
            "TenantId": "<tenant-id>",
            "ClientId": "<client-id>",
            "ServiceAccountEmail": "<name>@<project-id>.iam.gserviceaccount.com"
          }
        }
      }
    }
    ```
  
    `RefreshAheadWindow` and `EnableBackgroundRefresh` stay at the root and apply to every resource; `RefreshAheadWindow` must be positive and at most five minutes. Startup validation requires at least one resource section and reports every missing value by its path, for example `AzureSql:Google:ServiceAccountEmail must be provided.`
  - **Managed identity.** `"Provider": "ManagedIdentity"` with `"ManagedIdentity": { "UseSystemAssignedIdentity": true }` or `"ManagedIdentity": { "ClientId": "<user-assigned-client-id>" }` obtains the token from the managed identity endpoint of the Azure resource the application runs on.
  - **Blob Storage.** A `BlobStorage` section (same shape as `AzureSql`) registers `IBlobStorageTokenProvider` with `GetBlobStorageAccessTokenAsync`, requesting the `https://storage.azure.com/.default` scope.
  - **`AddWorkloadIdentity` replaces `AddAzureSqlFederatedIdentity`** with the same three overloads (host configuration, `Action<WorkloadIdentityOptions>`, `IConfiguration`). `WorkloadIdentityOptions` replaces `AzureSqlFederatedIdentityOptions`; the section name `Csag.WorkloadIdentity` is unchanged and exposed as `WorkloadIdentityOptions.ConfigurationSectionName`. Both providers are always registered; resolving the provider of a resource whose section is absent throws an `InvalidOperationException` naming the missing section.
  - **`TokenCredential` adapter.** Both provider interfaces gain `GetAccessTokenAsync`, which returns the token together with its real expiry, and `WorkloadIdentityTokenCredential` presents a provider to Azure SDK clients, for example `new BlobServiceClient(uri, new WorkloadIdentityTokenCredential(blobStorageTokenProvider))`.
  - `IGoogleIdTokenProvider.GetIdTokenAsync` takes the service account email, so resources can federate through different service accounts. `IAzureSqlTokenExchanger` is replaced by internal per-provider exchangers, and the provider implementations `AzureSqlTokenProvider` and `BlobStorageTokenProvider` are internal: depend on `IAzureSqlTokenProvider` and `IBlobStorageTokenProvider`.
  - Unchanged: `IAzureSqlTokenProvider.GetAzureSqlAccessTokenAsync`; the token is held in memory and concurrent callers share a single request; the background service refreshes ahead of expiry with exponential backoff, now in one loop per configured resource; every service is registered with `TryAdd`, so a consumer-registered provider wins and is left alone by the background refresh.

### Patch Changes

- [#8](https://github.com/neolution-ch/Csag.WorkloadIdentity/pull/8) [`bbc1f9c`](https://github.com/neolution-ch/Csag.WorkloadIdentity/commit/bbc1f9c61f3789ca652d98ec02c46037882c8afd) Thanks [@neotrow](https://github.com/neotrow)! - Rewrite the package README as a self-contained quickstart: every configuration key including `RefreshAheadWindow` and `EnableBackgroundRefresh`, all three `AddAzureSqlFederatedIdentity` overloads, obtaining the token from `IAzureSqlTokenProvider` and handing it to `SqlClient` through a `SqlAuthenticationProvider` (also for EF Core) or `SqlConnection.AccessToken`, the prerequisites on the Google and Microsoft side, and a troubleshooting table. Links are absolute so they work on nuget.org.

- [#17](https://github.com/neolution-ch/Csag.WorkloadIdentity/pull/17) [`56bc35a`](https://github.com/neolution-ch/Csag.WorkloadIdentity/commit/56bc35ac85ebdbcea3dd599e0ebaa5924d9aecac) Thanks [@neotrow](https://github.com/neotrow)! - The package's project and repository links, and the links in its README, point to the renamed GitHub repository `neolution-ch/Csag.WorkloadIdentity`.

- [#12](https://github.com/neolution-ch/Csag.WorkloadIdentity/pull/12) [`85d4f83`](https://github.com/neolution-ch/Csag.WorkloadIdentity/commit/85d4f83da182b4fabf0002d13087147b9b0854d1) Thanks [@neotrow](https://github.com/neotrow)! - Document the resource-first model. The package README explains how each resource obtains its token from a managed identity or through Google workload identity federation, and carries two complete quickstarts (Azure SQL from Google Cloud Run, with the token supplied through a `SqlAuthenticationProvider` or `SqlConnection.AccessToken`; Azure SQL and Blob Storage from Azure App Service with a system- or user-assigned managed identity, including a `BlobServiceClient` built on `WorkloadIdentityTokenCredential`), a reference of every configuration key for both providers, and a migration section from `Neolution.AzureSqlFederatedIdentity` with the before/after configuration and the `AddWorkloadIdentity` rename. The cloud setup guide is restructured around the choice between an Azure managed identity and Google Cloud to Azure federation, describes both holders of the federated credential (app registration or user-assigned managed identity), the Azure SQL and Blob Storage grants, the new configuration keys, and Microsoft Entra-only authentication.

## 0.0.1

Initial release, published as **Neolution.AzureSqlFederatedIdentity** (2025-06-16).

- Federated identity integration for Azure SQL using Google Cloud IAM Credentials and Azure AD.
- Obtain Azure SQL access tokens using Google service accounts.
- Automatic token refresh and caching.
- Easy integration with ASP.NET Core and .NET worker services.
