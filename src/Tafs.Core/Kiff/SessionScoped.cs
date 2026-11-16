namespace Tafs.Core.Kiff;

/// <summary>
/// What a session keeps in memory, by key (each taf's Setup as last read): for that session only. Asked for by another
/// session, it starts empty; forgotten at sign-out, nothing of the last account answers for the next.
/// </summary>
public sealed class SessionScoped<TKey, TValue> where TKey : notnull
{
    readonly Dictionary<TKey, TValue> items = [];
    object? session;

    /// <summary>The value kept for this session and key, made the first time it's asked for.</summary>
    public TValue Get(object session, TKey key, Func<TValue> make)
    {
        if (!ReferenceEquals(this.session, session))
        {
            items.Clear();
            this.session = session;
        }
        if (!items.TryGetValue(key, out var value)) items[key] = value = make();
        return value;
    }

    /// <summary>Signed out: everything kept goes.</summary>
    public void Forget()
    {
        items.Clear();
        session = null;
    }

    public int Count => items.Count;
}
