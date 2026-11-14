---
"@neolution-ch/csag-workload-identity": minor
---

Add `IEntraClientAssertionProvider` and the `Entra` entry. A Microsoft Entra sign-in can now send a client assertion in place of a client secret: a Google ID token of the service account (`Provider` `Google`, the default) or a token of a managed identity (`Provider` `ManagedIdentity`, configured as for a resource). A user-assigned managed identity is preferred, because the object ID of a system-assigned one changes when the Azure resource is deleted and created again. The background refresh does not include the entry.

Every section is now optional. A host without any `AzureSql`, `BlobStorage` or `Entra` section starts, and the services do nothing; it no longer fails with "At least one resource section (AzureSql or BlobStorage) must be configured." An application that calls `AddWorkloadIdentity` only when its section has a value can now call it always.
