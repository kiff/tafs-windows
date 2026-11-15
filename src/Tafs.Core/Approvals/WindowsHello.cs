using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tafs.Core.Approvals;

/// <summary>
/// Windows Hello passkeys for kiff.dev, through Windows' WebAuthn API (webauthn.dll). Windows shows its own
/// prompt (face, fingerprint or PIN) on every call, and the private key never leaves the TPM. This app passes
/// the exact clientDataJSON it built, so the challenge, and with it the request it approves, is this app's.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHello(Func<IntPtr> window) : IPasskeyAuthenticator
{
    /// <summary>Two minutes for the person to answer Windows Hello.</summary>
    const uint Timeout = 120_000;

    /// <summary>Whether this PC has Windows' WebAuthn API and a platform authenticator that verifies the person.</summary>
    public static bool IsAvailable()
    {
        try
        {
            return Native.WebAuthNGetApiVersionNumber() >= 1
                && Native.WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable(out var available) == 0 && available;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>
    /// Makes this PC's passkey for kiff.dev: ES256 only, on this PC's platform authenticator, the person verified,
    /// no attestation. Returns what KIFF registers, read and checked by <see cref="PasskeyRegistration"/>.
    /// </summary>
    /// <param name="userId">The account's subject, as bytes, when it's known (the contract's user.id); otherwise random bytes.</param>
    /// <param name="userName">The person's email when it's known (user.name); otherwise this PC's name.</param>
    /// <param name="displayName">What Windows' passkey list shows: this PC's name.</param>
    /// <param name="challenge">For a pairing, <see cref="Pairing.PairingClaim.PasskeyChallenge"/>; otherwise a random one.</param>
    public Task<NewPasskey> CreateAsync(byte[] userId, string userName, string displayName, string? challenge = null,
        CancellationToken cancel = default) => Task.Run(() =>
    {
        using var mem = new Memory();
        var clientData = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["type"] = "webauthn.create",
            ["challenge"] = challenge ?? Kiff.KiffSigning.Base64Url(RandomNumberGenerator.GetBytes(32)),
            ["origin"] = PasskeyApprover.Origin,
            ["crossOrigin"] = false,
        });
        var rp = new RpInfo { Version = 1, Id = mem.Text(PasskeyApprover.RpId), Name = mem.Text("KIFF") };
        var user = new UserInfo { Version = 1, IdLength = (uint)userId.Length, Id = mem.Bytes(userId), Name = mem.Text(userName), DisplayName = mem.Text(displayName) };
        var es256 = new CoseParameter { Version = 1, Type = mem.Text("public-key"), Alg = -7 };
        var parameters = new CoseParameters { Count = 1, Items = mem.Struct(es256) };
        var data = ClientData(mem, clientData);
        var options = new MakeCredentialOptions
        {
            Version = 1, TimeoutMs = Timeout, Attachment = PlatformAttachment,
            UserVerification = VerificationRequired, Attestation = AttestationNone,
        };

        var hr = Native.WebAuthNAuthenticatorMakeCredential(window(), ref rp, ref user, ref parameters, ref data, ref options, out var result);
        Check(hr, cancel);
        try
        {
            var attestation = Marshal.PtrToStructure<CredentialAttestation>(result);
            return PasskeyRegistration.Read(Copy(attestation.AuthenticatorData, attestation.AuthenticatorDataLength)) with
            {
                Proof = new PasskeyCreationProof(clientData, Copy(attestation.AttestationObject, attestation.AttestationObjectLength)),
            };
        }
        finally { Native.WebAuthNFreeCredentialAttestation(result); }
    }, cancel);

    /// <summary>One assertion with this PC's passkey, over exactly <paramref name="clientDataJson"/>.</summary>
    public Task<PasskeyAssertion> GetAssertionAsync(string rpId, byte[] credentialId, byte[] clientDataJson, CancellationToken cancel) => Task.Run(() =>
    {
        using var mem = new Memory();
        var credential = new Credential { Version = 1, IdLength = (uint)credentialId.Length, Id = mem.Bytes(credentialId), Type = mem.Text("public-key") };
        var data = ClientData(mem, clientDataJson);
        var options = new GetAssertionOptions
        {
            Version = 1, TimeoutMs = Timeout, Attachment = PlatformAttachment, UserVerification = VerificationRequired,
            Credentials = new Credentials { Count = 1, Items = mem.Struct(credential) },
        };

        var hr = Native.WebAuthNAuthenticatorGetAssertion(window(), rpId, ref data, ref options, out var result);
        Check(hr, cancel);
        try
        {
            var assertion = Marshal.PtrToStructure<Assertion>(result);
            return new PasskeyAssertion(
                Copy(assertion.AuthenticatorData, assertion.AuthenticatorDataLength),
                Copy(assertion.Signature, assertion.SignatureLength));
        }
        finally { Native.WebAuthNFreeAssertion(result); }
    }, cancel);

    const uint PlatformAttachment = 1;      // WEBAUTHN_AUTHENTICATOR_ATTACHMENT_PLATFORM
    const uint VerificationRequired = 1;    // WEBAUTHN_USER_VERIFICATION_REQUIREMENT_REQUIRED
    const uint AttestationNone = 1;         // WEBAUTHN_ATTESTATION_CONVEYANCE_PREFERENCE_NONE
    const int Cancelled = unchecked((int)0x800704C7);   // HRESULT_FROM_WIN32(ERROR_CANCELLED)
    const int UserCancelled = unchecked((int)0x80090036); // NTE_USER_CANCELLED

    static ClientDataStruct ClientData(Memory mem, byte[] json) =>
        new() { Version = 1, Length = (uint)json.Length, Json = mem.Bytes(json), HashAlg = mem.Text("SHA-256") };

    /// <summary>The person closed Windows Hello: a cancel, said as one. Anything else: Windows' own name for the error.</summary>
    static void Check(int hr, CancellationToken cancel)
    {
        if (hr == 0) return;
        if (hr is Cancelled or UserCancelled) throw new OperationCanceledException("Windows Hello was closed", cancel);
        var name = Marshal.PtrToStringUni(Native.WebAuthNGetErrorName(hr)) ?? "";
        throw new PasskeyRefusedException($"Windows Hello didn't finish: {name} (0x{hr:X8})");
    }

    static byte[] Copy(IntPtr data, uint length)
    {
        var bytes = new byte[length];
        Marshal.Copy(data, bytes, 0, (int)length);
        return bytes;
    }

    /// <summary>Native memory for one call's inputs, freed when the call is over.</summary>
    sealed class Memory : IDisposable
    {
        readonly List<IntPtr> blocks = [];
        public IntPtr Text(string text) { var p = Marshal.StringToHGlobalUni(text); blocks.Add(p); return p; }
        public IntPtr Bytes(byte[] bytes) { var p = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, p, bytes.Length); blocks.Add(p); return p; }
        public IntPtr Struct<T>(T value) where T : struct { var p = Marshal.AllocHGlobal(Marshal.SizeOf<T>()); Marshal.StructureToPtr(value, p, false); blocks.Add(p); return p; }
        public void Dispose() { foreach (var p in blocks) Marshal.FreeHGlobal(p); }
    }

    // webauthn.h's structures, version 1 of each: Windows reads only the fields of the version given.
    // Their x64 offsets are pinned by WindowsHelloLayoutTests.

    [StructLayout(LayoutKind.Sequential)] internal struct RpInfo { public uint Version; public IntPtr Id, Name, Icon; }
    [StructLayout(LayoutKind.Sequential)] internal struct UserInfo { public uint Version, IdLength; public IntPtr Id, Name, Icon, DisplayName; }
    [StructLayout(LayoutKind.Sequential)] internal struct CoseParameter { public uint Version; public IntPtr Type; public int Alg; }
    [StructLayout(LayoutKind.Sequential)] internal struct CoseParameters { public uint Count; public IntPtr Items; }
    [StructLayout(LayoutKind.Sequential)] internal struct ClientDataStruct { public uint Version, Length; public IntPtr Json, HashAlg; }
    [StructLayout(LayoutKind.Sequential)] internal struct Credential { public uint Version, IdLength; public IntPtr Id, Type; }
    [StructLayout(LayoutKind.Sequential)] internal struct Credentials { public uint Count; public IntPtr Items; }
    [StructLayout(LayoutKind.Sequential)] internal struct Extensions { public uint Count; public IntPtr Items; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MakeCredentialOptions
    {
        public uint Version, TimeoutMs;
        public Credentials Excluded;
        public Extensions Extensions;
        public uint Attachment, RequireResidentKey, UserVerification, Attestation, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GetAssertionOptions
    {
        public uint Version, TimeoutMs;
        public Credentials Credentials;
        public Extensions Extensions;
        public uint Attachment, UserVerification, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CredentialAttestation
    {
        public uint Version;
        public IntPtr FormatType;
        public uint AuthenticatorDataLength;
        public IntPtr AuthenticatorData;
        public uint AttestationLength;
        public IntPtr Attestation;
        public uint DecodeType;
        public IntPtr Decode;
        public uint AttestationObjectLength;
        public IntPtr AttestationObject;
        public uint CredentialIdLength;
        public IntPtr CredentialId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Assertion
    {
        public uint Version, AuthenticatorDataLength;
        public IntPtr AuthenticatorData;
        public uint SignatureLength;
        public IntPtr Signature;
        public Credential Credential;
        public uint UserIdLength;
        public IntPtr UserId;
    }

    static class Native
    {
        const string Dll = "webauthn.dll";

        [DllImport(Dll)] public static extern uint WebAuthNGetApiVersionNumber();
        [DllImport(Dll)] public static extern int WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable([MarshalAs(UnmanagedType.Bool)] out bool available);
        [DllImport(Dll)] public static extern IntPtr WebAuthNGetErrorName(int hr);

        [DllImport(Dll)]
        public static extern int WebAuthNAuthenticatorMakeCredential(IntPtr window, ref RpInfo rp, ref UserInfo user,
            ref CoseParameters parameters, ref ClientDataStruct clientData, ref MakeCredentialOptions options, out IntPtr attestation);

        [DllImport(Dll)] public static extern void WebAuthNFreeCredentialAttestation(IntPtr attestation);

        [DllImport(Dll, CharSet = CharSet.Unicode)]
        public static extern int WebAuthNAuthenticatorGetAssertion(IntPtr window, string rpId, ref ClientDataStruct clientData,
            ref GetAssertionOptions options, out IntPtr assertion);

        [DllImport(Dll)] public static extern void WebAuthNFreeAssertion(IntPtr assertion);
    }
}
