using Tafs.Core.Approvals;

namespace Tafs.App;

/// <summary>
/// What this PC's passkey is approving now (the owner's ask): set where the act is known, read when
/// Windows Hello opens. Windows' WebAuthn prompt shows only kiff.dev and Windows' own words, so tafs says the act itself
/// while the prompt is up (<c>approval.hello.before</c>). It flows with the call that approves (an <see cref="AsyncLocal{T}"/>),
/// so two acts never name each other.
/// </summary>
static class HelloAct
{
    static readonly AsyncLocal<string?> current = new();

    /// <summary>The act's words, in the person's language: the iPad's own reason for it (its Face ID prompt).</summary>
    public static string? Current => current.Value;

    /// <summary>Names <paramref name="act"/> for the approvals made until the result is disposed.</summary>
    public static IDisposable Naming(string act)
    {
        var before = current.Value;
        current.Value = act;
        return new Restore(() => current.Value = before);
    }

    sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}

/// <summary>
/// Windows Hello as this PC's passkey asks it, saying the act while its prompt is up: "Approving: {act}. Windows Hello asks
/// for your PIN, face or fingerprint." (<see cref="Saying"/>, which the window shows). With no act named, nothing is said.
/// </summary>
sealed class NamedHello : IPasskeyAuthenticator
{
    readonly WindowsHello inner = new(() => App.WindowHandle);

    /// <summary>The line to show while Windows Hello is up; null when it closes. The window draws it.</summary>
    public static event Action<string?>? Saying;

    public async Task<PasskeyAssertion> GetAssertionAsync(string rpId, byte[] credentialId, byte[] clientDataJson, CancellationToken cancel)
    {
        var act = HelloAct.Current;
        if (act is not null) Saying?.Invoke(App.Words.Get("approval.hello.before", ("act", act)));
        try { return await inner.GetAssertionAsync(rpId, credentialId, clientDataJson, cancel); }
        finally { if (act is not null) Saying?.Invoke(null); }
    }
}
