namespace Tafs.Core.SignIn;

/// <summary>What the window opens on: the app, the signed-out window, or the celebration.</summary>
public enum Opening { App, SignedOut, Connected }

/// <summary>
/// Signed out, the whole window signs in (the owner's approved mockup, 16 Nov, as the iPad's <c>SignInView</c>), never the app
/// with nothing in it; after either way of signing in, the celebration (the iPad's <c>ConnectedView</c>). Neither over the
/// sample.
/// </summary>
public static class SignedOut
{
    /// <summary>
    /// At launch: the screen asked for (<c>--page signed-out</c> or <c>connected</c>, for screenshots); else the signed-out
    /// window with no session, unless it's the sample or a screenshot of another screen; else the app.
    /// </summary>
    public static Opening AtLaunch(string? page, bool screenshot, bool sample, bool hasSession) => page switch
    {
        "signed-out" => Opening.SignedOut,
        "connected" => Opening.Connected,
        null when !screenshot && !sample && !hasSession => Opening.SignedOut,
        _ => Opening.App,
    };

    /// <summary>After signing in, with the device code or the phone: the celebration, except over the sample.</summary>
    public static Opening AfterSignIn(bool sample) => sample ? Opening.App : Opening.Connected;
}
