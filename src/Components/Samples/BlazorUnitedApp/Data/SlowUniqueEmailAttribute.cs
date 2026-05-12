// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;

namespace BlazorUnitedApp.Data;

/// <summary>
/// Async validation attribute that simulates a uniqueness check against a remote service.
/// Used to demonstrate field-level async validation via <c>Validator.TryValidatePropertyAsync</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
internal sealed class SlowUniqueEmailAttribute : AsyncValidationAttribute
{
    protected override async ValueTask<ValidationResult?> IsValidAsync(
        object? value,
        ValidationContext validationContext,
        CancellationToken cancellationToken)
    {
        if (value is not string email || string.IsNullOrWhiteSpace(email))
        {
            return ValidationResult.Success;
        }

        // Simulate a network call to check uniqueness
        await Task.Delay(800, cancellationToken);

        // "taken@example.com" is always taken; everything else is available
        return string.Equals(email, "taken@example.com", StringComparison.OrdinalIgnoreCase)
            ? new ValidationResult(
                "That email is already registered. Try another.",
                new[] { validationContext.MemberName! })
            : ValidationResult.Success;
    }
}
