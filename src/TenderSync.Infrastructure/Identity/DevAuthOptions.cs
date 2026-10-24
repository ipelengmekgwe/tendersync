namespace TenderSync.Infrastructure.Identity;

/// <summary>
/// Development-only sign-in shortcut, bound from "DevAuth". With AllowAnyCredentials on, any email and password sign in:
/// an unknown email gets an Admin account created on the spot and the password is not checked. The API host forces the
/// flag off outside the Development environment (Program.cs), so a stray setting can never open a deployed instance.
/// </summary>
public sealed class DevAuthOptions
{
    public const string SectionName = "DevAuth";

    public bool AllowAnyCredentials { get; set; }
}
