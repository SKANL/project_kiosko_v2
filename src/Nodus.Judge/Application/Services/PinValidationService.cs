using System.Text.RegularExpressions;
using Nodus.Judge.Domain.Common;

namespace Nodus.Judge.Application.Services;

/// <summary>
/// PIN validation and management service.
/// Enforces security rules: length, allowed characters, format for BLE transmission.
/// </summary>
public sealed class PinValidationService
{
    public const int MinPinLength = 4;
    public const int MaxPinLength = 8;
    private static readonly Regex AllowedCharacters = new Regex(@"^[0-9]+$", RegexOptions.Compiled);

    /// <summary>
    /// Validates that a PIN meets all requirements for secure storage and BLE transmission.
    /// </summary>
    /// <returns>
    /// Ok if PIN is valid.
    /// Fail if PIN doesn't meet requirements.
    /// </returns>
    public Result<string> ValidatePin(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
            return Result<string>.Fail("PIN cannot be empty");

        pin = pin.Trim();

        if (pin.Length < MinPinLength)
            return Result<string>.Fail($"PIN must be at least {MinPinLength} characters");

        if (pin.Length > MaxPinLength)
            return Result<string>.Fail($"PIN must be no more than {MaxPinLength} characters");

        if (!AllowedCharacters.IsMatch(pin))
            return Result<string>.Fail("PIN must contain only digits (0-9)");

        return Result<string>.Ok(pin);
    }

    /// <summary>
    /// Formats a PIN as a masked display string (e.g., "••••" for 4 chars).
    /// </summary>
    public string MaskPin(string pin)
        => new string('•', pin?.Length ?? 0);

    /// <summary>
    /// Generates a random PIN of the specified length.
    /// </summary>
    public string GenerateRandomPin(int length = MinPinLength)
    {
        if (length < MinPinLength || length > MaxPinLength)
            length = MinPinLength;

        var random = new Random();
        return new string(Enumerable.Range(0, length)
            .Select(_ => (char)('0' + random.Next(10)))
            .ToArray());
    }
}
