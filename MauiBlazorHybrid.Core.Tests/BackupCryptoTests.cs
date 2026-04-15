using System.Security.Cryptography;
using System.Text;
using MauiBlazorHybrid.Core;

namespace MauiBlazorHybrid.Core.Tests;

public class BackupCryptoTests
{
    #region DeriveSalt

    /// <summary>
    /// Test: Derive salt from a given account id.
    /// Assumptions: Same account id is passed twice.
    /// Expectation: Same salt bytes are returned — salt derivation is deterministic.
    /// </summary>
    [Fact]
    public void DeriveSalt_SameAccountId_ProducesSameSalt()
    {
        var a = BackupCrypto.DeriveSalt("abc123");
        var b = BackupCrypto.DeriveSalt("abc123");

        Assert.Equal(a, b);
    }

    /// <summary>
    /// Test: Derive salt from two different account ids.
    /// Assumptions: Different account ids are passed.
    /// Expectation: Different salt bytes are returned — salt is unique per account.
    /// </summary>
    [Fact]
    public void DeriveSalt_DifferentAccountIds_ProduceDifferentSalts()
    {
        var a = BackupCrypto.DeriveSalt("abc123");
        var b = BackupCrypto.DeriveSalt("def456");

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Test: Derive salt from an empty account id.
    /// Assumptions: Empty string is a valid input (defensive behaviour).
    /// Expectation: Salt is still 32 bytes (SHA256 output length).
    /// </summary>
    [Fact]
    public void DeriveSalt_EmptyAccountId_ReturnsFixedLengthSalt()
    {
        var salt = BackupCrypto.DeriveSalt("");

        Assert.Equal(32, salt.Length);
    }

    #endregion

    #region DeriveEncryptionKey

    /// <summary>
    /// Test: Derive encryption key from password + account id.
    /// Assumptions: Same password and account id passed twice.
    /// Expectation: Same 32-byte key is returned — derivation is deterministic.
    /// </summary>
    [Fact]
    public void DeriveEncryptionKey_SamePasswordAndAccount_ProducesSameKey()
    {
        var a = BackupCrypto.DeriveEncryptionKey("password123", "account-1");
        var b = BackupCrypto.DeriveEncryptionKey("password123", "account-1");

        Assert.Equal(a, b);
        Assert.Equal(32, a.Length);
    }

    /// <summary>
    /// Test: Derive encryption key with different passwords but same account id.
    /// Assumptions: Password matters for key derivation.
    /// Expectation: Different keys are returned.
    /// </summary>
    [Fact]
    public void DeriveEncryptionKey_DifferentPasswords_ProduceDifferentKeys()
    {
        var a = BackupCrypto.DeriveEncryptionKey("passwordA", "account-1");
        var b = BackupCrypto.DeriveEncryptionKey("passwordB", "account-1");

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Test: Derive encryption key with same password but different account ids.
    /// Assumptions: Account id contributes to the salt, so keys must differ.
    /// Expectation: Different keys are returned.
    /// </summary>
    [Fact]
    public void DeriveEncryptionKey_DifferentAccounts_ProduceDifferentKeys()
    {
        var a = BackupCrypto.DeriveEncryptionKey("password123", "account-A");
        var b = BackupCrypto.DeriveEncryptionKey("password123", "account-B");

        Assert.NotEqual(a, b);
    }

    #endregion

    #region Encrypt / Decrypt round-trip

    /// <summary>
    /// Test: Encrypt then decrypt a small plaintext with the same key.
    /// Assumptions: Key is 32 bytes, data is arbitrary bytes.
    /// Expectation: Decrypt returns the original plaintext exactly.
    /// </summary>
    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        var key = BackupCrypto.DeriveEncryptionKey("pw", "acct");
        var plaintext = Encoding.UTF8.GetBytes("hello world, this is a backup payload");

        var encrypted = BackupCrypto.Encrypt(plaintext, key);
        var decrypted = BackupCrypto.Decrypt(encrypted, key);

        Assert.Equal(plaintext, decrypted);
    }

    /// <summary>
    /// Test: Encrypt then decrypt a larger JSON-like payload.
    /// Assumptions: The function must handle realistic backup-size blobs (not just tiny).
    /// Expectation: Round-trip is exact regardless of length.
    /// </summary>
    [Fact]
    public void EncryptDecrypt_LargePayload_RoundTripsExactly()
    {
        var key = BackupCrypto.DeriveEncryptionKey("pw", "acct");
        var plaintext = new byte[64 * 1024];
        new Random(42).NextBytes(plaintext);

        var encrypted = BackupCrypto.Encrypt(plaintext, key);
        var decrypted = BackupCrypto.Decrypt(encrypted, key);

        Assert.Equal(plaintext, decrypted);
    }

    /// <summary>
    /// Test: Encrypt an empty payload.
    /// Assumptions: Empty plaintext is valid input.
    /// Expectation: Round-trip returns an empty byte array, ciphertext is still IV+tag length.
    /// </summary>
    [Fact]
    public void EncryptDecrypt_EmptyPayload_RoundTrips()
    {
        var key = BackupCrypto.DeriveEncryptionKey("pw", "acct");
        var plaintext = Array.Empty<byte>();

        var encrypted = BackupCrypto.Encrypt(plaintext, key);
        var decrypted = BackupCrypto.Decrypt(encrypted, key);

        Assert.Equal(plaintext, decrypted);
        Assert.Equal(12 + 16, encrypted.Length); // IV + Tag + 0 ciphertext
    }

    /// <summary>
    /// Test: Two successive encryptions of the same plaintext with the same key.
    /// Assumptions: IV is randomly generated each call.
    /// Expectation: Ciphertexts differ (non-deterministic output), but both decrypt to same plaintext.
    /// </summary>
    [Fact]
    public void Encrypt_SamePlaintextTwice_ProducesDifferentCiphertexts()
    {
        var key = BackupCrypto.DeriveEncryptionKey("pw", "acct");
        var plaintext = Encoding.UTF8.GetBytes("same data");

        var encrypted1 = BackupCrypto.Encrypt(plaintext, key);
        var encrypted2 = BackupCrypto.Encrypt(plaintext, key);

        Assert.NotEqual(encrypted1, encrypted2);
        Assert.Equal(plaintext, BackupCrypto.Decrypt(encrypted1, key));
        Assert.Equal(plaintext, BackupCrypto.Decrypt(encrypted2, key));
    }

    /// <summary>
    /// Test: Encrypted output layout is [12 byte IV][16 byte tag][ciphertext].
    /// Assumptions: Implementation documents this format.
    /// Expectation: encrypted.Length == 12 + 16 + plaintext.Length.
    /// </summary>
    [Fact]
    public void Encrypt_OutputLength_EqualsIvPlusTagPlusPlaintext()
    {
        var key = BackupCrypto.DeriveEncryptionKey("pw", "acct");
        var plaintext = new byte[100];

        var encrypted = BackupCrypto.Encrypt(plaintext, key);

        Assert.Equal(12 + 16 + plaintext.Length, encrypted.Length);
    }

    #endregion

    #region Decrypt failure cases

    /// <summary>
    /// Test: Decrypt with a different key than was used to encrypt.
    /// Assumptions: AES-GCM authentication catches the wrong key.
    /// Expectation: CryptographicException is thrown.
    /// </summary>
    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var keyA = BackupCrypto.DeriveEncryptionKey("passwordA", "acct");
        var keyB = BackupCrypto.DeriveEncryptionKey("passwordB", "acct");
        var encrypted = BackupCrypto.Encrypt(Encoding.UTF8.GetBytes("secret"), keyA);

        Assert.Throws<AuthenticationTagMismatchException>(() => BackupCrypto.Decrypt(encrypted, keyB));
    }

    /// <summary>
    /// Test: Decrypt data whose ciphertext portion has been tampered with.
    /// Assumptions: AES-GCM tag verifies integrity of the ciphertext.
    /// Expectation: CryptographicException is thrown.
    /// </summary>
    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var key = BackupCrypto.DeriveEncryptionKey("pw", "acct");
        var encrypted = BackupCrypto.Encrypt(Encoding.UTF8.GetBytes("important data"), key);

        // Flip a bit in the ciphertext portion (after IV and tag)
        encrypted[12 + 16] ^= 0x01;

        Assert.Throws<AuthenticationTagMismatchException>(() => BackupCrypto.Decrypt(encrypted, key));
    }

    /// <summary>
    /// Test: Decrypt data whose authentication tag has been tampered with.
    /// Assumptions: AES-GCM tag covers ciphertext + associated data.
    /// Expectation: CryptographicException is thrown.
    /// </summary>
    [Fact]
    public void Decrypt_TamperedTag_Throws()
    {
        var key = BackupCrypto.DeriveEncryptionKey("pw", "acct");
        var encrypted = BackupCrypto.Encrypt(Encoding.UTF8.GetBytes("important data"), key);

        // Flip a bit in the tag portion (bytes 12..27)
        encrypted[12] ^= 0x01;

        Assert.Throws<AuthenticationTagMismatchException>(() => BackupCrypto.Decrypt(encrypted, key));
    }

    /// <summary>
    /// Test: Decrypt data that is shorter than IV+tag overhead.
    /// Assumptions: Any blob shorter than 28 bytes cannot possibly be a valid ciphertext.
    /// Expectation: CryptographicException with a clear "invalid" message is thrown.
    /// </summary>
    [Fact]
    public void Decrypt_TruncatedData_Throws()
    {
        var key = BackupCrypto.DeriveEncryptionKey("pw", "acct");

        var tooShort = new byte[10];

        Assert.Throws<CryptographicException>(() => BackupCrypto.Decrypt(tooShort, key));
    }

    #endregion

    #region End-to-end password → decrypt scenarios

    /// <summary>
    /// Test: Full flow — device A derives key from password and encrypts; device B derives key from
    /// the same password and account id and decrypts successfully.
    /// Assumptions: Two devices share password + account id out-of-band but never share the key itself.
    /// Expectation: Device B recovers the original plaintext.
    /// </summary>
    [Fact]
    public void EndToEnd_TwoDevicesSamePasswordAndAccount_CanDecryptEachOther()
    {
        var deviceAKey = BackupCrypto.DeriveEncryptionKey("user-password", "shared-account");
        var plaintext = Encoding.UTF8.GetBytes("{\"products\":[]}");
        var encrypted = BackupCrypto.Encrypt(plaintext, deviceAKey);

        // Device B independently derives its key from the same inputs
        var deviceBKey = BackupCrypto.DeriveEncryptionKey("user-password", "shared-account");
        var decrypted = BackupCrypto.Decrypt(encrypted, deviceBKey);

        Assert.Equal(plaintext, decrypted);
    }

    /// <summary>
    /// Test: Full flow — device B attempts to decrypt with the wrong password.
    /// Assumptions: Typo or wrong password should never silently return garbage.
    /// Expectation: Authentication failure throws; no plaintext is recovered.
    /// </summary>
    [Fact]
    public void EndToEnd_WrongPassword_CannotDecrypt()
    {
        var correctKey = BackupCrypto.DeriveEncryptionKey("correct-password", "account");
        var plaintext = Encoding.UTF8.GetBytes("secret backup");
        var encrypted = BackupCrypto.Encrypt(plaintext, correctKey);

        var wrongKey = BackupCrypto.DeriveEncryptionKey("wrong-password", "account");

        Assert.Throws<AuthenticationTagMismatchException>(() => BackupCrypto.Decrypt(encrypted, wrongKey));
    }

    /// <summary>
    /// Test: Full flow — attempt to decrypt with a matching password but wrong account id.
    /// Assumptions: Account id feeds the salt; a different account means a different key.
    /// Expectation: Authentication failure throws.
    /// </summary>
    [Fact]
    public void EndToEnd_WrongAccountId_CannotDecrypt()
    {
        var keyA = BackupCrypto.DeriveEncryptionKey("pw", "account-A");
        var encrypted = BackupCrypto.Encrypt(Encoding.UTF8.GetBytes("data"), keyA);

        var keyB = BackupCrypto.DeriveEncryptionKey("pw", "account-B");

        Assert.Throws<AuthenticationTagMismatchException>(() => BackupCrypto.Decrypt(encrypted, keyB));
    }

    #endregion
}
