# Csag.WorkloadIdentity

[![Build Status](https://github.com/neolution-ch/Csag.WorkloadIdentity/actions/workflows/ci.yml/badge.svg)](https://github.com/neolution-ch/Csag.WorkloadIdentity/actions)
[![NuGet](https://img.shields.io/nuget/v/Csag.WorkloadIdentity.svg)](https://www.nuget.org/packages/Csag.WorkloadIdentity)
[![License: MIT](https://img.shields.io/badge/License-MIT-lightgray.svg)](./LICENSE)

A .NET library that gives an application passwordless access tokens for Azure SQL and Azure Blob Storage from the identity it already runs as. Each resource is configured on its own and obtains its token either from the **managed identity** of the Azure resource the application runs on, or from the application's **Google identity**, exchanged for a Microsoft Entra ID access token through workload identity federation. You attach the token to the `SqlConnection`, or hand it to Azure SDK clients such as `BlobServiceClient` through the library's `TokenCredential` adapter. No password, key or secret is stored anywhere.

## Projects

| Project | Description |
|---|---|
| [`Csag.WorkloadIdentity`](./Csag.WorkloadIdentity) | The library, published as the [Csag.WorkloadIdentity](https://www.nuget.org/packages/Csag.WorkloadIdentity) NuGet package for `net8.0` and `net10.0`. Its [README](./Csag.WorkloadIdentity/README.md) is the package documentation; the [CHANGELOG](./Csag.WorkloadIdentity/CHANGELOG.md) is generated from changesets. |
| [`Csag.WorkloadIdentity.Demo`](./Csag.WorkloadIdentity.Demo) | An ASP.NET Core application that reads from Azure SQL and, optionally, Blob Storage through the library, on Cloud Run or an Azure host. Its [README](./Csag.WorkloadIdentity.Demo/README.md) explains how to configure it and run it from source, in Docker and in the cloud. |
| [`Csag.WorkloadIdentity.UnitTests`](./Csag.WorkloadIdentity.UnitTests) | xunit tests for the library, run on both target frameworks. |

## Quick start

1. Set up the cloud side once. Choose the identity configuration that matches where the application runs, an Azure managed identity or Google Cloud to Azure federation, then grant that identity access to Azure SQL and Blob Storage. Every step is in [docs/cloud-identity-setup.md](./docs/cloud-identity-setup.md).
2. Install the package and wire it into your application. The [package README](./Csag.WorkloadIdentity/README.md) has two complete quickstarts, Azure SQL from Google Cloud Run and Azure SQL plus Blob Storage from Azure App Service, the reference of every configuration key, and a migration section for users of `Neolution.AzureSqlFederatedIdentity`.

   ```shell
   dotnet add package Csag.WorkloadIdentity
   ```

3. To see it end to end, run the [Demo](./Csag.WorkloadIdentity.Demo/README.md) against your own resources.

## Preview packages

Each pull request to `main` or to a `release/**` branch publishes the package to a public preview feed on feedz.io. A draft PR, a PR from a fork and a PR from Dependabot publish no preview. The version is one patch above the current release, for example `0.1.2-pr.42.183` (PR 42, workflow run 183). The **"chore: version packages"** PR publishes a release candidate of the upcoming version, for example `0.2.0-pr.73.190`. Thus you can test all the changes of the next release before the release. A comment on the PR gives the version of its latest push. The feed removes previews after 30 days. Releases go to nuget.org only.

To use a preview in an application, add the feed to the `nuget.config` of the application. The feed needs no credentials:

```xml
<packageSources>
  <clear />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  <add key="csag-workloadidentity-previews" value="https://f.feedz.io/collana-solutions/csag-workloadidentity/nuget/index.json" />
</packageSources>
<packageSourceMapping>
  <packageSource key="nuget.org">
    <package pattern="*" />
    <package pattern="Csag.WorkloadIdentity" />
  </packageSource>
  <packageSource key="csag-workloadidentity-previews">
    <package pattern="Csag.WorkloadIdentity" />
  </packageSource>
</packageSourceMapping>
```

The `Csag.WorkloadIdentity` pattern is also necessary under nuget.org. NuGet looks for a package only in the sources of its most specific pattern, so the `*` pattern does not apply to `Csag.WorkloadIdentity`. If nuget.org does not have the `Csag.WorkloadIdentity` pattern, the released versions do not restore.

To test a preview:

1. Set `Csag.WorkloadIdentity` to the preview version.
2. If the application restores in locked mode, update its `packages.lock.json` files.
3. Add the feed only on the branch that tests the preview. The main branch of the application then does not need feedz.io to restore.

## Release process

This repository follows the [neolution-ch release playbook](https://github.com/neolution-ch/release-playbook) (Changesets, NuGet variant):

1. Every PR that changes the library needs a changeset: run `npx changeset`, choose the bump type and describe the change. CI blocks PRs without one (`npx changeset --empty` for changes that do not affect the package).
2. On merge to `main`, the Release workflow maintains a **"chore: version packages"** PR that accumulates pending changesets; `scripts/sync-versions.mjs` keeps the `.csproj` version in step.
3. Merging that PR creates the git tag and GitHub Release; the **NuGet Publish** workflow then packs and pushes the package to nuget.org.

The package is on a `0.x` version: breaking changes are declared as **minor** changesets, fixes as **patch**. Dependabot PRs get their changesets generated automatically.

## Contributing

Issues and pull requests are welcome. [CONTRIBUTING.md](./CONTRIBUTING.md) describes the prerequisites, how to build and test on both target frameworks, the code conventions and what a pull request needs. Please report vulnerabilities privately as described in [SECURITY.md](./SECURITY.md).

## License

This project is licensed under the MIT License. See the [LICENSE](./LICENSE) file for details.
