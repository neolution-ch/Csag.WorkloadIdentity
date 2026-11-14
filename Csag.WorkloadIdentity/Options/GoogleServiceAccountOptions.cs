namespace Csag.WorkloadIdentity.Options
{
    /// <summary>
    /// Names the Google service account a client assertion is minted for.
    /// </summary>
    /// <remarks>
    /// The <see cref="EntraClientAssertionOptions.Google"/> section uses this type and not <see cref="GoogleOptions"/>.
    /// The sign-in configuration already has the tenant ID and the client ID, so the section must not require them.
    /// </remarks>
    public class GoogleServiceAccountOptions
    {
        /// <summary>
        /// Gets or sets the email of the Google service account the ID token is minted for. The federated credential
        /// names the unique ID of this account as its subject.
        /// </summary>
        public string? ServiceAccountEmail { get; set; }
    }
}
