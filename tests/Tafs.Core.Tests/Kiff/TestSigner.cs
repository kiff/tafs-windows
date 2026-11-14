using System.Security.Cryptography;
using Tafs.Core.Kiff;

namespace Tafs.Core.Tests.Kiff;

/// <summary>A software P-256 key, for tests only. The app's keys live in the TPM and never in memory.</summary>
sealed class TestSigner : IKiffSigner, IDisposable
{
    readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public byte[] PublicKeyDer => key.ExportSubjectPublicKeyInfo();

    public Task<byte[]> SignDerAsync(byte[] message, CancellationToken cancel = default) =>
        Task.FromResult(key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    public static bool Verify(byte[] spki, byte[] message, byte[] derSignature)
    {
        using var k = ECDsa.Create();
        k.ImportSubjectPublicKeyInfo(spki, out _);
        return k.VerifyData(message, derSignature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    public void Dispose() => key.Dispose();
}
