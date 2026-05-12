// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;

namespace BlazorUnitedApp.Data;

/// <summary>
/// Model that demonstrates both field-level and form-level async validation.
/// <list type="bullet">
///   <item><term>Field-level (per-keystroke/blur):</term>
///     <description><see cref="SlowUniqueEmailAttribute"/> on <see cref="Email"/> runs via
///     <c>Validator.TryValidatePropertyAsync</c> inside <c>OnFieldChanged</c>.</description></item>
///   <item><term>Form-level (on submit):</term>
///     <description><see cref="IAsyncValidatableObject.ValidateAsync"/> runs cross-field checks
///     via <c>EditContext.ValidateAsync()</c>.</description></item>
/// </list>
/// </summary>
internal sealed class RegistrationModel : IAsyncValidatableObject
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(50, ErrorMessage = "Name must be 50 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    [SlowUniqueEmail]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Username must be 3–20 characters.")]
    public string Username { get; set; } = string.Empty;

    [Range(18, 120, ErrorMessage = "Age must be between 18 and 120.")]
    public int Age { get; set; } = 25;

    /// <summary>
    /// Form-level cross-field validation — runs on submit via <c>ValidateAsync()</c>.
    /// Simulates a server-side rule: username and email must not belong to different accounts.
    /// </summary>
    public async ValueTask<IEnumerable<ValidationResult>> ValidateAsync(
        ValidationContext validationContext,
        CancellationToken cancellationToken)
    {
        var results = new List<ValidationResult>();

        // Simulate an async server call
        await Task.Delay(300, cancellationToken);

        // Cross-field rule: "admin" username with a non-admin email is suspicious
        if (string.Equals(Username, "admin", StringComparison.OrdinalIgnoreCase)
            && !Email.StartsWith("admin@", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new ValidationResult(
                "The 'admin' username requires an admin@ email address.",
                [nameof(Username), nameof(Email)]));
        }

        return results;
    }
}
