using System.Security.Cryptography;
using System.Text;

namespace MauiBlazorHybrid.Core;

/// <summary>
/// Client-side AES-256-GCM encryption helpers for backup payloads.
/// Keys are derived from the user's password with PBKDF2 (100k iterations)
/// using a deterministic salt derived from the account id, so a new device
/// can recover the key from just password + account id without any server round-trip.
/// </summary>
public static class BackupCrypto
{
    /// <summary>PBKDF2 iteration count. Tuned to be slow enough to make brute force expensive.</summary>
    private const int Iterations = 100_000;

    /// <summary>AES key size in bytes (256 bits).</summary>
    private const int KeySize = 32;

    /// <summary>AES-GCM nonce (IV) size in bytes. GCM standard is 12 bytes.</summary>
    private const int IvSize = 12;

    /// <summary>AES-GCM authentication tag size in bytes.</summary>
    private const int TagSize = 16;

    /// <summary>
    /// Derives a deterministic salt from the account ID.
    /// </summary>
    public static byte[] DeriveSalt(string accountId)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes("PillerinPyörittäjä" + accountId));
    }

    /// <summary>
    /// Derives the encryption key from password + account ID.
    /// </summary>
    public static byte[] DeriveEncryptionKey(string password, string accountId)
    {
        var salt = DeriveSalt(accountId);
        var context = Combine(salt, "encrypt"u8);
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), context, Iterations, HashAlgorithmName.SHA256, KeySize);
    }

    /// <summary>
    /// Encrypts plaintext bytes with AES-256-GCM using the encryption key.
    /// Output format: [12 bytes IV][16 bytes tag][ciphertext]
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] encryptionKey)
    {
        var iv = new byte[IvSize];
        RandomNumberGenerator.Fill(iv);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(encryptionKey, TagSize);
        aes.Encrypt(iv, plaintext, ciphertext, tag);

        // [IV][Tag][Ciphertext]
        var result = new byte[IvSize + TagSize + ciphertext.Length];
        iv.CopyTo(result, 0);
        tag.CopyTo(result, IvSize);
        ciphertext.CopyTo(result, IvSize + TagSize);
        return result;
    }

    /// <summary>
    /// Decrypts data encrypted by Encrypt().
    /// </summary>
    public static byte[] Decrypt(byte[] encryptedData, byte[] encryptionKey)
    {
        if (encryptedData.Length < IvSize + TagSize)
            throw new CryptographicException("Invalid encrypted data");

        var iv = encryptedData.AsSpan(0, IvSize);
        var tag = encryptedData.AsSpan(IvSize, TagSize);
        var ciphertext = encryptedData.AsSpan(IvSize + TagSize);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(encryptionKey, TagSize);
        aes.Decrypt(iv, ciphertext, tag, plaintext);
        return plaintext;
    }

    /// <summary>
    /// Concatenates a byte array and a byte span into a new byte array.
    /// Used to append the context label ("encrypt") to the salt before PBKDF2.
    /// </summary>
    private static byte[] Combine(byte[] a, ReadOnlySpan<byte> b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result.AsSpan(a.Length));
        return result;
    }
}
