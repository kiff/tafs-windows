using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Account;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// The AI-data consent, as the iPad's <c>loadAIConsent</c> and <c>consentRequired</c>: read with each session, its screen put
/// up when it isn't given, unless Not now was answered this session; and put up again, whatever was put off, by any turn
/// tafs cloud refuses for want of it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Not now was answered this session: a read doesn't put the screen up again, only a refused turn does.</summary>
    bool consentPutOff;

    /// <summary>The screen is up: one at a time.</summary>
    bool consentShown;

    /// <summary>Read once a session is used. Not given, its screen; a tafs cloud without the route asks nothing.</summary>
    async Task ReadConsentAsync(Func<string, Task<CloudRead>> read, int reading)
    {
        var answer = (await read(AiConsent.Path)).Answer;
        if (reading != sessions || consentPutOff || answer is not { } json || AiConsent.Read(json) is not { Given: false, Drawable: true } consent) return;
        await AskConsentAsync(consent);
    }

    /// <summary>
    /// The screen, as the iPad's <c>AIConsentView</c>: Allow sends its version; an older screen's yes is refused with the new
    /// words, which show in its place; any other refusal says why. Not now puts it off for this session.
    /// </summary>
    async Task AskConsentAsync(AiConsent consent)
    {
        if (consentShown || cloudSend is not { } send || Content?.XamlRoot is not { } root) return;
        consentShown = true;
        var reading = sessions;
        try
        {
            for (AiConsent? asked = consent; asked is not null;)
            {
                if (!await ConsentDialog.AskAsync(root, asked))
                {
                    consentPutOff = true;
                    return;
                }
                var given = await send(AiConsent.Path, asked.AllowBody());
                if (reading != sessions || given.Answer is not null) return;
                asked = given.Status == 409 ? given.Consent : null;
                if (asked is null)
                    await new ContentDialog { XamlRoot = root, Content = given.Why ?? App.Words.Get("error.ours"), CloseButtonText = App.Words.Get("common.ok") }.ShowAsync();
            }
        }
        // Another dialog is up: this one waits for the next turn or session that asks.
        catch (System.Runtime.InteropServices.COMException) { }
        finally { consentShown = false; }
    }
}
