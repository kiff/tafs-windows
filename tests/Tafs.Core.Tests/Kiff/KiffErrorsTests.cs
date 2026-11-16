using Tafs.Core.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Kiff;

/// <summary>A failed KIFF read in the iPad's plain words: KIFFError.errorDescription and PlainError.words(status:).</summary>
[Part("pairing")]
public class KiffErrorsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    [Theory]
    [InlineData(401, "error.signed_out")]
    [InlineData(0, "error.offline")]
    [InlineData(403, "error.not_allowed")]
    [InlineData(409, "error.changed")]
    [InlineData(429, "error.busy")]
    [InlineData(500, "error.down")]
    [InlineData(503, "error.down")]
    [InlineData(599, "error.down")]
    [InlineData(404, "error.ours")]
    [InlineData(400, "error.ours")]
    [InlineData(200, "error.ours")]
    public void Each_status_in_the_app_s_words(int status, string key) => Assert.Equal(En.Get(key), KiffErrors.Words(En, status));

    [Fact]
    public void KIFF_s_reason_never_shows() =>
        Assert.Equal("Signed out of KIFF. Sign in again.", KiffErrors.Words(En, 401));
}
