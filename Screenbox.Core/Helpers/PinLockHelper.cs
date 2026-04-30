#nullable enable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Screenbox.Core.Helpers;

public static class PinLockHelper
{
    public static bool IsValidPin(string? pin)
    {
        return pin is { Length: 4 } && pin.All(char.IsDigit);
    }

    public static string CreateSalt()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static string HashPin(string pin, string salt)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes($"{salt}:{pin}");
        return Convert.ToBase64String(sha256.ComputeHash(bytes));
    }

    public static bool VerifyPin(string pin, string salt, string expectedHash)
    {
        return IsValidPin(pin) &&
               !string.IsNullOrWhiteSpace(salt) &&
               !string.IsNullOrWhiteSpace(expectedHash) &&
               HashPin(pin, salt) == expectedHash;
    }
}
