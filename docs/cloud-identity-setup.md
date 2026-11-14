# Cloud and identity setup

This guide sets up everything outside your code so that an application can reach Azure SQL and Azure Blob Storage through `Csag.WorkloadIdentity` without a password, key or secret: the identity the application presents to Microsoft Entra ID (formerly Azure Active Directory), the trust that identity needs, the grants on each resource, the application's configuration and the deployment. For the code that uses the tokens, see the [package README](../Csag.WorkloadIdentity/README.md); the [Demo](../Csag.WorkloadIdentity.Demo/README.md) is a complete application. [Section F](#f-microsoft-entra-sign-in-without-a-client-secret) sets up a Microsoft Entra sign-in that needs no client secret.

## Choose your identity configuration

Every access token the library obtains is issued by Microsoft Entra ID to one identity. Where the application runs decides which identity that is:

| | A. Azure managed identity | B. Google Cloud to Azure federation |
|---|---|---|
| The application runs on | Azure: App Service, Container Apps, Functions, a virtual machine | Google Cloud: Cloud Run, GKE, Compute Engine; or a workstation with Application Default Credentials |
| The identity | The system-assigned managed identity of the hosting resource, or a user-assigned managed identity attached to it | An app registration or a user-assigned managed identity that holds a federated credential trusting the application's Google service account |
| `Provider` in the configuration | `ManagedIdentity` | `Google` |
| Set up in | [Section A](#a-azure-managed-identity) | [Section B](#b-google-cloud-to-azure-federation) |

The choice is made per resource (`AzureSql`, `BlobStorage`), so one application can, for example, use its system-assigned identity for Azure SQL and a user-assigned identity for Blob Storage. Whatever the identity, the grants in [section C](#c-grant-access-to-the-resources) are the same, [section D](#d-configure-the-application) maps everything onto the configuration keys, and [section E](#e-deploy) covers the deployment.

Values you collect along the way:

| Value | Comes from | Used as |
|---|---|---|
| Identity name: the App Service name (system-assigned), the managed identity's name or the app registration's display name | A.1, A.2 or B.2 | Database user name (C.1); the member of the Blob role assignment (C.2) |
| Client ID of a user-assigned managed identity | A.2 or B.2 | `ManagedIdentity:ClientId` (A) or `Google:ClientId` (B) |
| Application (client) ID of an app registration | B.2 | `Google:ClientId` |
| Directory (tenant) ID | B.2 | `Google:TenantId` |
| Service account email and unique ID | B.1 | `Google:ServiceAccountEmail`; subject of the federated credential (B.2) |

## A. Azure managed identity

A [managed identity](https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/overview) is an identity in Microsoft Entra ID whose credentials Azure creates and rotates; the application obtains tokens from the hosting resource's local identity endpoint and never sees a secret. Nothing is needed at runtime beyond the identity being enabled on, or attached to, the resource the application runs on. The steps below use App Service; Container Apps, Functions and virtual machines expose the same **Identity** setting.

### A.1 System-assigned identity

A system-assigned identity is created with the resource, shares its lifecycle and cannot be attached to anything else. It is the simplest choice for a single application.

1. In the Azure portal open the App Service and go to **Settings** > **Identity**.
2. On the **System assigned** tab switch **Status** to **On** and select **Save**.

Or with the Azure CLI:

```shell
az webapp identity assign --resource-group <resource-group> --name <app-name>
```

The name of a system-assigned identity is always the name of the App Service; that is the `<identity-name>` the database user in C.1 is created with. Configure the resource with `"UseSystemAssignedIdentity": true`; no client ID is needed.

### A.2 User-assigned identity

A user-assigned identity is a standalone Azure resource that can be attached to several resources and outlives any of them, so its grants can be made before the application exists and survive its re-creation.

1. In the Azure portal search for **Managed Identities**, select **Create**, choose the subscription, resource group, region and a name, and create it.
2. On the identity's **Overview** page note the **Client ID**.
3. Open the App Service, go to **Settings** > **Identity** > **User assigned**, select **Add**, pick the identity and confirm.

Or with the Azure CLI:

```shell
az identity create --resource-group <resource-group> --name <identity-name>
az identity show --resource-group <resource-group> --name <identity-name> --query clientId --output tsv
az webapp identity assign --resource-group <resource-group> --name <app-name> --identities <identity-resource-id>
```

`<identity-resource-id>` is the identity's full resource ID (`az identity show ... --query id --output tsv`). The identity's name is the `<identity-name>` for C.1; its client ID goes into `ManagedIdentity:ClientId`.

### A.3 Local development

A workstation has no managed identity endpoint, so `"Provider": "ManagedIdentity"` only works on Azure. To run the same code locally, either configure the resource with the `Google` provider and Application Default Credentials (section B), or register your own `IAzureSqlTokenProvider` or `IBlobStorageTokenProvider` before calling `AddWorkloadIdentity` (every registration is a `TryAdd`, so yours wins), for example one built on `Azure.Identity`'s `AzureCliCredential`. Your provider ignores the resource section, so leave the section out. `AddWorkloadIdentity` validates only the sections that are present.

## B. Google Cloud to Azure federation

With [workload identity federation](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation), Microsoft Entra ID accepts a token from an external identity provider as the credential of one of its own identities. Here the external token is a Google-signed ID token for the application's service account, and the Microsoft Entra identity that trusts it is either an app registration or a user-assigned managed identity:

```text
Your application, running as a Google service account (Application Default Credentials)
   │
   │  1. generateIdToken(audience = "api://AzureADTokenExchange")      IAM Service Account Credentials API
   ▼
Google-signed ID token   iss = https://accounts.google.com, sub = <service account unique ID>
   │
   │  2. client assertion for <ClientId> in tenant <TenantId>      Microsoft Entra ID
   ▼
Access token for https://database.windows.net/.default or https://storage.azure.com/.default
   │
   │  3. SqlAuthenticationProvider or SqlConnection.AccessToken, or TokenCredential for BlobServiceClient
   ▼
Azure SQL or Blob Storage, as the identity that holds the federated credential
```

### B.1 Google Cloud

#### B.1.1 Enable the IAM Service Account Credentials API

The library mints the ID token by calling `generateIdToken` on this API. It must be enabled in the project that owns the service account, otherwise every call fails with `PERMISSION_DENIED`:

```shell
gcloud services enable iamcredentials.googleapis.com --project <project-id>
```

#### B.1.2 Create the service account

```shell
gcloud iam service-accounts create <name> --project <project-id> --display-name "<display name>"
```

The service account's email is `<name>@<project-id>.iam.gserviceaccount.com`. Also note its numeric **unique ID**, which the federated credential in B.2 uses as the subject:

```shell
gcloud iam service-accounts describe <name>@<project-id>.iam.gserviceaccount.com --format 'value(uniqueId)'
```

The Google Cloud console shows both values on the service account's details page under **IAM & Admin** > **Service Accounts**.

#### B.1.3 Grant the least privilege needed

The only IAM operation the library performs is `generateIdToken`, which requires the single permission `iam.serviceAccounts.getOpenIdToken` on the service account. The predefined role that contains exactly that permission is **Service Account OpenID Connect Identity Token Creator** (`roles/iam.serviceAccountOpenIdTokenCreator`). Grant it to the identity the application runs as, **on the service account resource itself**:

```shell
gcloud iam service-accounts add-iam-policy-binding <name>@<project-id>.iam.gserviceaccount.com \
  --member "serviceAccount:<name>@<project-id>.iam.gserviceaccount.com" \
  --role roles/iam.serviceAccountOpenIdTokenCreator
```

The member here is the service account itself, which is the intended deployment: the Cloud Run service runs as this account (E.2) and mints ID tokens for it. Google's Service Account Credentials API allows this; its [self-impersonation](https://cloud.google.com/iam/docs/service-account-creds) restriction covers generating access tokens and signing, not ID tokens. A developer who runs the application on a workstation needs the same role on the service account for their own account:

```shell
gcloud iam service-accounts add-iam-policy-binding <name>@<project-id>.iam.gserviceaccount.com \
  --member "user:<developer>@<domain>" \
  --role roles/iam.serviceAccountOpenIdTokenCreator
```

Why this binding and not a broader one:

- **Bind on the service account, not on the project.** A project-level binding (`gcloud projects add-iam-policy-binding`) applies to every service account in the project, including ones created later, so the member could mint ID tokens for all of them. A binding on the service account limits the grant to that one account, which is what Google recommends for [direct short-lived credentials](https://cloud.google.com/iam/docs/create-short-lived-credentials-direct).
- **Prefer the OpenID Token Creator role to Service Account Token Creator.** `roles/iam.serviceAccountTokenCreator` additionally grants `iam.serviceAccounts.getAccessToken`, `signBlob`, `signJwt` and `implicitDelegation`, that is, full impersonation of the account. The library needs none of those.

#### B.1.4 Application Default Credentials

The library never reads a key file of its own. It uses [Application Default Credentials](https://cloud.google.com/docs/authentication/application-default-credentials) (ADC), which the Google client library resolves from the `GOOGLE_APPLICATION_CREDENTIALS` environment variable, then from the credential file written by `gcloud auth application-default login`, and finally from the service account attached to the Cloud Run, GKE or Compute Engine resource.

- On Cloud Run nothing needs configuring beyond the runtime service account (E.2).
- On a workstation run `gcloud auth application-default login` once. The application then acts as your user account, which needs the role from B.1.3.
- Do not create a service account key; nothing in this setup requires one.

### B.2 Microsoft Entra ID: the holder of the federated credential

The federated credential is attached to a Microsoft Entra identity, and that identity is what Azure SQL and Blob Storage see. The library accepts either kind of holder in `Google:ClientId`; pick one:

| | Option 1: app registration | Option 2: user-assigned managed identity |
|---|---|---|
| Created in | Microsoft Entra ID (**App registrations**); requires permission to register applications in the tenant | An Azure subscription (**Managed Identities**); requires Azure RBAC rights on a resource group and no Microsoft Entra role |
| `Google:ClientId` | The registration's **Application (client) ID** | The identity's **Client ID** |
| `<identity-name>` for the grants in section C | The registration's display name | The identity's name |

Both options need the **Directory (tenant) ID** of the tenant, shown on the tenant's **Microsoft Entra ID** > **Overview** page, as `Google:TenantId`.

#### B.2.1 Option 1: register an application

1. In the Azure portal go to **Microsoft Entra ID** > **App registrations** > **New registration**.
2. Enter a name. "Accounts in this organizational directory only" is sufficient, and no redirect URI is needed.
3. On the registration's **Overview** page copy the **Application (client) ID** and the **Directory (tenant) ID**, and note the display name.

Do not create a client secret or certificate: the federated credential in B.2.3 replaces them.

#### B.2.2 Option 2: create a user-assigned managed identity

Create the identity as in A.2 (portal or `az identity create`) and note its **Client ID** and name. It is only the holder of the federated credential, so it does not need to be attached to any Azure resource. Microsoft documents this scenario under [Configure a user-assigned managed identity to trust an external identity provider](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust-user-assigned-managed-identity).

#### B.2.3 Add the federated credential

Where the credential is added depends on the holder:

- **App registration:** under the registration go to **Certificates & secrets** > **Federated credentials** > **Add credential** and choose the **Other issuer** scenario.
- **User-assigned managed identity:** under the identity go to **Settings** > **Federated credentials** > **Add Credential** and choose the **Other issuer** scenario.

Fill in the same values in both cases:

| Field | Value |
|---|---|
| Issuer | `https://accounts.google.com` |
| Subject identifier | The service account's **unique ID** from B.1.2 (the numeric ID, not the email) |
| Audience | `api://AzureADTokenExchange` (the default) |
| Name | Any name, for example the service account's name |

The audience is fixed: the library requests every Google ID token with exactly `api://AzureADTokenExchange`, which is the value Microsoft Entra ID recommends for workload identity federation. It is neither the holder's client ID nor the Azure resource. Microsoft Entra ID fetches Google's signing keys through the issuer, verifies the ID token's signature, and accepts it as a client assertion only if its `sub` and `aud` claims match this credential.

## C. Grant access to the resources

The identity from section A or B needs a grant on each resource the application is configured for. The grant is the same whichever way the identity was set up. `<identity-name>` below is the name from the table at the top: the App Service name for a system-assigned identity, the managed identity's name, or the app registration's display name.

### C.1 Azure SQL

#### C.1.1 Set a Microsoft Entra admin for the server

In the Azure portal open the logical SQL server, select **Microsoft Entra ID** under **Settings**, then **Set admin**, if no admin is set. Only a Microsoft Entra identity can create database users from an external provider, so connect to the database as that admin (for example with SQL Server Management Studio, Azure Data Studio or `sqlcmd` using Microsoft Entra authentication) to run the statements below. If the admin is itself a service principal or managed identity rather than a user, the server additionally needs a server identity with permission to read Microsoft Graph before it can create such users; Microsoft describes that setup under [service principals with Azure SQL](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-service-principal). A user admin needs nothing extra.

#### C.1.2 Create a database user for the identity

In the target database (not in `master`), create a contained user for the identity. With Google federation, the user represents the holder of the federated credential; the Google service account never appears in Azure SQL:

```sql
CREATE USER [<identity-name>] FROM EXTERNAL PROVIDER;
```

When a person runs this statement, as the Microsoft Entra admin from C.1.1 does here, Azure SQL looks the identity up in Microsoft Graph with that person's delegated permissions, and nothing more is needed. Only when a service principal runs it, for example a deployment pipeline that signs in as an app registration or a managed identity, does Azure SQL query Microsoft Graph with its *server identity*, the managed identity assigned to the logical server, because an application cannot act on behalf of another. In that case assign the server an identity (in the portal under the server's **Identity** page, or `az sql server update --resource-group <resource-group> --name <server> --assign-identity`) and grant that identity permission to read the directory: add it to the Microsoft Entra **Directory Readers** role, or grant it the Microsoft Graph application permissions `User.Read.All`, `GroupMember.Read.All` and `Application.Read.All`. Without this, the statement fails with error 33134, "Principal '<identity-name>' could not be resolved", and the message says that the server identity is not configured. See the troubleshooting section of [Microsoft Entra service principals with Azure SQL](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-service-principal#troubleshoot).

Then grant permissions. The built-in roles are a convenient starting point:

```sql
ALTER ROLE db_datareader ADD MEMBER [<identity-name>];
ALTER ROLE db_datawriter ADD MEMBER [<identity-name>];
```

`db_datareader` and `db_datawriter` allow reading and writing every table in the database. Narrow that to what the application actually needs: a read-only service needs only `db_datareader`, and explicit grants scope access to particular objects, for example:

```sql
GRANT SELECT, INSERT ON dbo.Orders TO [<identity-name>];
```

Neither built-in role allows schema changes; run migrations with a separate, more privileged identity rather than widening this one.

#### C.1.3 Allow the network path

The server's [network access controls](https://learn.microsoft.com/en-us/azure/azure-sql/database/network-access-controls-overview) must admit connections from where the application runs.

- **From Azure:** the server's **Allow Azure services and resources to access this server** setting admits every resource inside Azure, including other customers', which is more permissive than most deployments want. Prefer an IP firewall rule for the App Service's outbound addresses, or private connectivity through virtual network integration and a private endpoint.
- **From Cloud Run:** its outbound IP addresses are not fixed by default, so to use IP firewall rules give the service a [static outbound IP address](https://cloud.google.com/run/docs/configuring/static-outbound-ip), or use private connectivity instead.

### C.2 Azure Blob Storage

Blob access is granted with Azure role-based access control on the storage account or, more narrowly, on a single container. Two [built-in roles](https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/storage) cover most applications:

| Role | Grants |
|---|---|
| **Storage Blob Data Reader** | Read and list containers and blobs |
| **Storage Blob Data Contributor** | Read, write and delete containers and blobs |

1. In the Azure portal open the storage account (or the container) and go to **Access control (IAM)** > **Add** > **Add role assignment**.
2. Select the role. On the **Members** tab choose **Managed identity** and pick the identity (a system-assigned identity is listed under its resource type, for example App Service), or choose **User, group, or service principal** and search for the app registration by name.
3. Select **Review + assign**.

Or with the Azure CLI, given the object (principal) ID of the identity:

```shell
az role assignment create \
  --role "Storage Blob Data Reader" \
  --assignee-object-id <principal-object-id> \
  --assignee-principal-type ServicePrincipal \
  --scope /subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.Storage/storageAccounts/<storage-account>
```

The object ID is `az identity show ... --query principalId --output tsv` for a user-assigned identity, `az webapp identity show --resource-group <resource-group> --name <app-name> --query principalId --output tsv` for a system-assigned one, and `az ad sp show --id <application-client-id> --query id --output tsv` for an app registration's service principal. Append `/blobServices/default/containers/<container>` to the scope to restrict the grant to one container. A new [role assignment](https://learn.microsoft.com/en-us/azure/storage/blobs/assign-azure-role-data-access) can take up to 10 minutes to take effect.

The application then needs only the account's blob endpoint, `https://<storage-account>.blob.core.windows.net`. Because nothing uses the account keys any more, you can [disallow Shared Key authorization](https://learn.microsoft.com/en-us/azure/storage/common/shared-key-authorization-prevent) on the account (**Settings** > **Configuration** > **Allow storage account key access** set to **Disabled**), so that a leaked key cannot be used.

## D. Configure the application

The library binds its options from the `Csag.WorkloadIdentity` configuration section. Each resource has its own section that selects the `Provider` and carries that provider's settings. These are the exact keys, with the environment variable form that .NET maps to the same keys (`__` stands for the `:` separator; the `.` in the section name is part of the variable name):

| Configuration key | Environment variable | Value |
|---|---|---|
| `Csag.WorkloadIdentity:Entra:Provider` | `Csag.WorkloadIdentity__Entra__Provider` | `Google` (the default) or `ManagedIdentity` (F) |
| `Csag.WorkloadIdentity:Entra:Google:ServiceAccountEmail` | `Csag.WorkloadIdentity__Entra__Google__ServiceAccountEmail` | Service account email for the client assertion of the sign-in (F.1) |
| `Csag.WorkloadIdentity:Entra:ManagedIdentity:ClientId` | `Csag.WorkloadIdentity__Entra__ManagedIdentity__ClientId` | Client ID of the user-assigned identity for the client assertion of the sign-in (F.4); or set `Csag.WorkloadIdentity:Entra:ManagedIdentity:UseSystemAssignedIdentity` to `true` for the system-assigned identity |
| `Csag.WorkloadIdentity:AzureSql:Provider` | `Csag.WorkloadIdentity__AzureSql__Provider` | `ManagedIdentity` (the default) or `Google` |
| `Csag.WorkloadIdentity:AzureSql:ManagedIdentity:UseSystemAssignedIdentity` | `Csag.WorkloadIdentity__AzureSql__ManagedIdentity__UseSystemAssignedIdentity` | `true` for a system-assigned identity (A.1) |
| `Csag.WorkloadIdentity:AzureSql:ManagedIdentity:ClientId` | `Csag.WorkloadIdentity__AzureSql__ManagedIdentity__ClientId` | Client ID of the user-assigned identity (A.2); required unless the previous key is `true` |
| `Csag.WorkloadIdentity:AzureSql:Google:TenantId` | `Csag.WorkloadIdentity__AzureSql__Google__TenantId` | Directory (tenant) ID (B.2) |
| `Csag.WorkloadIdentity:AzureSql:Google:ClientId` | `Csag.WorkloadIdentity__AzureSql__Google__ClientId` | Application (client) ID of the app registration, or client ID of the user-assigned identity, holding the federated credential (B.2) |
| `Csag.WorkloadIdentity:AzureSql:Google:ServiceAccountEmail` | `Csag.WorkloadIdentity__AzureSql__Google__ServiceAccountEmail` | Service account email (B.1.2) |
| `Csag.WorkloadIdentity:BlobStorage:...` | `Csag.WorkloadIdentity__BlobStorage__...` | The same keys, for Blob Storage |
| `Csag.WorkloadIdentity:RefreshAheadWindow` | `Csag.WorkloadIdentity__RefreshAheadWindow` | Optional; how long before expiry a token is refreshed. Default `00:05:00`; must be positive and at most five minutes |
| `Csag.WorkloadIdentity:EnableBackgroundRefresh` | `Csag.WorkloadIdentity__EnableBackgroundRefresh` | Optional; default `true` |

Every section is optional, but a configured section must carry the complete settings of the provider it selects; the application refuses to start otherwise and names the missing value. For the identity configuration A, using the system-assigned identity for both resources:

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
    "DefaultConnection": "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;Authentication=Active Directory Default"
  }
}
```

For a user-assigned identity replace the `ManagedIdentity` section with `{ "ClientId": "<client-id>" }`. For the identity configuration B, with Azure SQL only:

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
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;Authentication=Active Directory Default"
  }
}
```

The connection string and the storage account's blob endpoint are your own application's settings (the Demo reads `ConnectionStrings:DefaultConnection`). The connection string names the server and database and carries no credentials such as `User ID`/`Password` or `Integrated Security`: the access token is the credential, and `Encrypt=True` keeps the token and the data on TLS. `Authentication=Active Directory Default` makes `SqlClient` ask the `SqlAuthenticationProvider` that the application installs for the token, as the Demo does; an application that assigns `SqlConnection.AccessToken` itself must leave the `Authentication` keyword out. The [package README](../Csag.WorkloadIdentity/README.md) shows how to register the library, both ways of handing the token to `SqlClient`, and how to build a `BlobServiceClient` on the library's `TokenCredential` adapter.

## E. Deploy

### E.1 Azure App Service

The identity from section A is already attached to the App Service, so only the settings remain. Supply them as application settings, which App Service exposes to the process as environment variables: in the portal under **Settings** > **Environment variables** > **App settings**, or with the Azure CLI:

```shell
az webapp config appsettings set --resource-group <resource-group> --name <app-name> --settings \
  Csag.WorkloadIdentity__AzureSql__Provider=ManagedIdentity \
  Csag.WorkloadIdentity__AzureSql__ManagedIdentity__UseSystemAssignedIdentity=true \
  Csag.WorkloadIdentity__BlobStorage__Provider=ManagedIdentity \
  Csag.WorkloadIdentity__BlobStorage__ManagedIdentity__UseSystemAssignedIdentity=true \
  "ConnectionStrings__DefaultConnection=Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;Authentication=Active Directory Default"
```

The same keys work for Container Apps, Functions and virtual machines, in whatever way each supplies environment variables.

### E.2 Google Cloud Run

Deploy the application with the service account from B.1 as its runtime identity, so that ADC resolves to that account, and supply the settings as environment variables. A YAML file keeps the connection string's commas out of the command line:

```yaml
# env.yaml
Csag.WorkloadIdentity__AzureSql__Provider: "Google"
Csag.WorkloadIdentity__AzureSql__Google__TenantId: "<tenant-id>"
Csag.WorkloadIdentity__AzureSql__Google__ClientId: "<client-id>"
Csag.WorkloadIdentity__AzureSql__Google__ServiceAccountEmail: "<name>@<project-id>.iam.gserviceaccount.com"
ConnectionStrings__DefaultConnection: "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;Authentication=Active Directory Default"
```

```shell
gcloud run deploy <service> \
  --project <project-id> \
  --region <region> \
  --image <region>-docker.pkg.dev/<project-id>/<repository>/<image>:<tag> \
  --service-account <name>@<project-id>.iam.gserviceaccount.com \
  --env-vars-file env.yaml
```

To change only the identity of an existing service, use `gcloud run services update <service> --service-account <name>@<project-id>.iam.gserviceaccount.com`. In the console, the runtime service account is under the service's **Security** tab and the variables under **Variables & Secrets**.

At startup the application validates its configuration. The first token request for each resource happens right after startup with background refresh on (the default), otherwise on the resource's first use. What that request does depends on the resource's provider: with `Google`, the application requests an ID token for the configured service account through ADC (as the runtime service account) and exchanges it at Microsoft Entra ID; with `ManagedIdentity`, it obtains the access token from the host's managed identity endpoint and no Google configuration is involved. If something fails, the application log names the failing step; the troubleshooting table in the [package README](../Csag.WorkloadIdentity/README.md) maps the usual messages to their cause.

## F. Microsoft Entra sign-in without a client secret

An application that signs users in with Microsoft Entra ID exchanges the authorization code at the token endpoint of Microsoft Entra ID. In that request, the application must authenticate as the client. With the `Entra` entry, the application sends a client assertion: a Google ID token of the service account of the application, or on Azure a token of its managed identity. The sign-in app registration has no client secret. The [package README](../Csag.WorkloadIdentity/README.md#client-assertion-for-a-microsoft-entra-sign-in) shows how the application puts the assertion into the token request. F.1 to F.3 set up the Google service account; F.4 sets up the managed identity.

With the Google service account, the setup is that of section B, with these differences:

- The holder of the federated credential is the **sign-in app registration**: the app registration whose client ID the sign-in configuration of the application uses, with the redirect URIs of the sign-in. It is not the identity of section B.2, which has no redirect URIs. A user-assigned managed identity cannot be the holder, because a sign-in needs an app registration.
- The application needs no tenant ID and no client ID in the `Entra` entry. The sign-in configuration of the application has them.
- Section C does not apply: the sign-in gets no access to a resource.

### F.1 Google Cloud

Use a service account as in B.1: the IAM Service Account Credentials API is enabled in its project (B.1.1), the identity the application runs as has `roles/iam.serviceAccountOpenIdTokenCreator` on the service account (B.1.3), and Application Default Credentials are available at runtime (B.1.4). The service account of the Azure resources can also serve the sign-in. Note its email and its unique ID (B.1.2).

### F.2 Add the federated credential to the sign-in app registration

1. In the Azure portal go to **Microsoft Entra ID** > **App registrations** and open the sign-in app registration.
2. Go to **Certificates & secrets** > **Federated credentials** > **Add credential** and choose the **Other issuer** scenario.
3. Fill in the values below and select **Add**.

| Field | Value |
|---|---|
| Issuer | `https://accounts.google.com` |
| Subject identifier | The service account's **unique ID** from B.1.2 (the numeric ID, not the email) |
| Audience | `api://AzureADTokenExchange` (the default) |
| Name | Any name, for example the service account's name |

Or with the Azure CLI:

```shell
az ad app federated-credential create --id <sign-in-application-client-id> --parameters '{
  "name": "<credential-name>",
  "issuer": "https://accounts.google.com",
  "subject": "<service-account-unique-id>",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

When the sign-in works with the assertion, delete the client secrets of the sign-in app registration under **Certificates & secrets** > **Client secrets**, and remove them from your secret store.

### F.3 Configure the application

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

On Cloud Run, supply the keys as environment variables, for example `Csag.WorkloadIdentity__Entra__Provider` = `Google` and `Csag.WorkloadIdentity__Entra__Google__ServiceAccountEmail`. Each sign-in asks Google for a new ID token. When a step of the setup is missing, the sign-in fails at the code exchange, and the application log names the cause:

| Symptom | Likely cause |
|---|---|
| `RpcException` with status `PermissionDenied` | The identity the application runs as does not have `roles/iam.serviceAccountOpenIdTokenCreator` on the service account, or the IAM Service Account Credentials API is not enabled (F.1). |
| Microsoft Entra ID answers `AADSTS70021`: no matching federated identity record | The sign-in app registration has no federated credential with this issuer, subject and audience, or the subject is the email and not the unique ID (F.2). |

### F.4 On Azure: a managed identity

An application on Azure, for example on App Service, can send a token of its managed identity in place of the Google ID token. The identity must be in the same tenant as the sign-in app registration. Microsoft describes the setup under [Configure an application to trust a managed identity](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity) and [Certificateless authentication with Microsoft.Identity.Web](https://learn.microsoft.com/entra/msidweb/authentication/certificateless).

Prefer a user-assigned identity. A system-assigned identity also works, but it gets a new object ID when you delete the App Service and create it again, and you must then update the federated credential. The Microsoft page linked above says "You can only use User-Assigned Managed Identities as a credential" in its portal instructions. The Azure CLI command in step 2 creates the federated credential for a system-assigned identity too.

1. Create a user-assigned managed identity and attach it to the App Service, as in A.2. Note its **client ID** and its **object (principal) ID**: `az identity show --resource-group <resource-group> --name <identity-name> --query "{clientId:clientId, objectId:principalId}"`. For the system-assigned identity, enable it as in A.1 and note its object ID: `az webapp identity show --resource-group <resource-group> --name <app-name> --query principalId --output tsv`.
2. Add a federated credential to the sign-in app registration that trusts the identity:

   ```shell
   az ad app federated-credential create --id <sign-in-application-client-id> --parameters '{
     "name": "<credential-name>",
     "issuer": "https://login.microsoftonline.com/<tenant-id>/v2.0",
     "subject": "<managed-identity-object-id>",
     "audiences": ["api://AzureADTokenExchange"]
   }'
   ```

3. Configure the application with the client ID of the identity:

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

   For the system-assigned identity, replace `"ClientId"` with `"UseSystemAssignedIdentity": true`. In App Service, supply the keys as application settings: `Csag.WorkloadIdentity__Entra__Provider` = `ManagedIdentity` and `Csag.WorkloadIdentity__Entra__ManagedIdentity__ClientId` (or `Csag.WorkloadIdentity__Entra__ManagedIdentity__UseSystemAssignedIdentity` = `true`).

When the sign-in app registration has no federated credential at all, Microsoft Entra ID answers `AADSTS70025`.

## Security best practice: disable SQL authentication

Once every application and every person reaches the database with a Microsoft Entra identity, password-based access is only an attack surface. Remove it in two steps.

1. **Audit and remove password-based users.** Connected as the Microsoft Entra admin, list the contained users that carry their own password (`authentication_type_desc = 'DATABASE'`) and drop the ones no longer needed:

   ```sql
   SELECT name, type_desc, authentication_type_desc
   FROM sys.database_principals
   WHERE authentication_type_desc = 'DATABASE';

   DROP USER [<username>];
   ```

   Users with `authentication_type_desc = 'INSTANCE'` are mapped to server logins, which may be SQL logins with a password or Microsoft Entra logins; check the login behind each one in `master` (`SELECT name, type_desc FROM sys.server_principals`, where `SQL_LOGIN` is password-based and `EXTERNAL_LOGIN` is Microsoft Entra) before dropping anything. Do this only after confirming that everything that used those users has moved to the passwordless connection.

2. **Enable Microsoft Entra-only authentication on the server.** This turns off SQL authentication for the whole logical server, including the server admin login; existing SQL logins are kept but can no longer connect. In the portal open the server's **Microsoft Entra ID** page under **Settings** and check **Support only Microsoft Entra authentication for this server**, or run:

   ```shell
   az sql server ad-only-auth enable --resource-group <resource-group> --name <server>
   ```

   A Microsoft Entra admin must be set first (C.1.1), and the person enabling the feature needs a highly privileged role on the server such as Owner, Contributor or SQL Security Manager. Microsoft describes the feature and its consequences under [Microsoft Entra-only authentication](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication).

The counterpart for Blob Storage is disallowing Shared Key authorization on the storage account, described at the end of C.2.

## References

- Google Cloud: [Create short-lived credentials for a service account](https://cloud.google.com/iam/docs/create-short-lived-credentials-direct), [Service account credentials and self-impersonation](https://cloud.google.com/iam/docs/service-account-creds), [IAM roles and permissions](https://cloud.google.com/iam/docs/roles-permissions/iam), [`generateIdToken`](https://cloud.google.com/iam/docs/reference/credentials/rest/v1/projects.serviceAccounts/generateIdToken), [Application Default Credentials](https://cloud.google.com/docs/authentication/application-default-credentials), [Cloud Run service identity](https://cloud.google.com/run/docs/configuring/services/service-identity), [Cloud Run environment variables](https://cloud.google.com/run/docs/configuring/services/environment-variables)
- Microsoft Entra ID: [Managed identities for Azure resources](https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/overview), [Manage user-assigned managed identities](https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/how-manage-user-assigned-managed-identities), [Workload identity federation](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation), [Configure an app to trust an external identity provider](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust), [Configure a user-assigned managed identity to trust an external identity provider](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust-user-assigned-managed-identity)
- Azure: [Managed identities in App Service](https://learn.microsoft.com/en-us/azure/app-service/overview-managed-identity), [Connect App Service to Azure SQL without secrets](https://learn.microsoft.com/en-us/azure/app-service/tutorial-connect-msi-sql-database), [App Service settings](https://learn.microsoft.com/en-us/azure/app-service/configure-common), [Microsoft Entra authentication for Azure SQL](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview), [Azure SQL with a service principal](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-service-principal), [Microsoft Entra-only authentication](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication), [Database-level roles](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/database-level-roles), [Authorize Blob access with Microsoft Entra ID](https://learn.microsoft.com/en-us/azure/storage/blobs/authorize-access-azure-active-directory), [Assign an Azure role for blob data](https://learn.microsoft.com/en-us/azure/storage/blobs/assign-azure-role-data-access), [Built-in roles for Storage](https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/storage), [`SqlConnection.AccessToken`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection.accesstoken), [`BlobServiceClient`](https://learn.microsoft.com/en-us/dotnet/api/azure.storage.blobs.blobserviceclient)
