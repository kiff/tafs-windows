using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Tafs.Core.Approvals;

namespace Tafs.Core.Tests.Approvals;

/// <summary>Runs only on 64-bit Windows: CI's Windows job, and the team's PCs.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows Hello's calls are Windows only";
        else if (IntPtr.Size != 8) Skip = "the offsets below are webauthn.h's on x64";
    }
}

/// <summary>
/// The structures handed to webauthn.dll, field by field, at webauthn.h's x64 offsets. A field out of place
/// would hand Windows a wrong length or pointer, so each is pinned here. The Windows Hello prompt itself
/// needs a person: it's checked by hand, as docs/approvals.md says.
/// </summary>
[Part("approvals")]
[SupportedOSPlatform("windows")]
public class WindowsHelloLayoutTests
{
    static int At<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    [WindowsFact]
    public void The_relying_party_and_the_user()
    {
        Assert.Equal((0, 8, 16, 24, 32), (At<WindowsHello.RpInfo>("Version"), At<WindowsHello.RpInfo>("Id"),
            At<WindowsHello.RpInfo>("Name"), At<WindowsHello.RpInfo>("Icon"), Marshal.SizeOf<WindowsHello.RpInfo>()));
        Assert.Equal((4, 8, 16, 24, 32, 40), (At<WindowsHello.UserInfo>("IdLength"), At<WindowsHello.UserInfo>("Id"),
            At<WindowsHello.UserInfo>("Name"), At<WindowsHello.UserInfo>("Icon"), At<WindowsHello.UserInfo>("DisplayName"),
            Marshal.SizeOf<WindowsHello.UserInfo>()));
    }

    [WindowsFact]
    public void ES256_client_data_and_a_credential()
    {
        Assert.Equal((8, 16, 24), (At<WindowsHello.CoseParameter>("Type"), At<WindowsHello.CoseParameter>("Alg"), Marshal.SizeOf<WindowsHello.CoseParameter>()));
        Assert.Equal((8, 16), (At<WindowsHello.CoseParameters>("Items"), Marshal.SizeOf<WindowsHello.CoseParameters>()));
        Assert.Equal((4, 8, 16, 24), (At<WindowsHello.ClientDataStruct>("Length"), At<WindowsHello.ClientDataStruct>("Json"),
            At<WindowsHello.ClientDataStruct>("HashAlg"), Marshal.SizeOf<WindowsHello.ClientDataStruct>()));
        Assert.Equal((4, 8, 16, 24), (At<WindowsHello.Credential>("IdLength"), At<WindowsHello.Credential>("Id"),
            At<WindowsHello.Credential>("Type"), Marshal.SizeOf<WindowsHello.Credential>()));
        Assert.Equal((8, 16), (At<WindowsHello.Credentials>("Items"), Marshal.SizeOf<WindowsHello.Credentials>()));
    }

    [WindowsFact]
    public void The_options_version_1()
    {
        Assert.Equal((8, 24, 40, 44, 48, 52, 56), (At<WindowsHello.MakeCredentialOptions>("Excluded"), At<WindowsHello.MakeCredentialOptions>("Extensions"),
            At<WindowsHello.MakeCredentialOptions>("Attachment"), At<WindowsHello.MakeCredentialOptions>("RequireResidentKey"),
            At<WindowsHello.MakeCredentialOptions>("UserVerification"), At<WindowsHello.MakeCredentialOptions>("Attestation"),
            At<WindowsHello.MakeCredentialOptions>("Flags")));
        Assert.Equal((8, 24, 40, 44, 48), (At<WindowsHello.GetAssertionOptions>("Credentials"), At<WindowsHello.GetAssertionOptions>("Extensions"),
            At<WindowsHello.GetAssertionOptions>("Attachment"), At<WindowsHello.GetAssertionOptions>("UserVerification"),
            At<WindowsHello.GetAssertionOptions>("Flags")));
    }

    [WindowsFact]
    public void What_Windows_answers()
    {
        Assert.Equal((16, 24, 80, 88), (At<WindowsHello.CredentialAttestation>("AuthenticatorDataLength"), At<WindowsHello.CredentialAttestation>("AuthenticatorData"),
            At<WindowsHello.CredentialAttestation>("CredentialIdLength"), At<WindowsHello.CredentialAttestation>("CredentialId")));
        Assert.Equal((4, 8, 16, 24, 32, 56, 64), (At<WindowsHello.Assertion>("AuthenticatorDataLength"), At<WindowsHello.Assertion>("AuthenticatorData"),
            At<WindowsHello.Assertion>("SignatureLength"), At<WindowsHello.Assertion>("Signature"), At<WindowsHello.Assertion>("Credential"),
            At<WindowsHello.Assertion>("UserIdLength"), At<WindowsHello.Assertion>("UserId")));
    }

    /// <summary>Asking whether Windows Hello is there never shows it, and never throws, with or without it.</summary>
    [WindowsFact]
    public void Asking_whether_Windows_Hello_is_there_never_throws() => _ = WindowsHello.IsAvailable();
}
