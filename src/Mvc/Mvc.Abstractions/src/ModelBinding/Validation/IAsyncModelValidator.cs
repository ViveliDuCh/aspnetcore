// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

/// <summary>
/// Validates a model value asynchronously. Implementations should also implement
/// <see cref="IModelValidator"/> for backward compatibility.
/// When the validation pipeline encounters a validator implementing both interfaces,
/// <see cref="ValidateAsync"/> is called in preference to <see cref="IModelValidator.Validate"/>.
/// </summary>
public interface IAsyncModelValidator
{
    /// <summary>
    /// Validates the model value asynchronously.
    /// </summary>
    /// <param name="context">The <see cref="ModelValidationContext"/>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> containing a read-only list of
    /// <see cref="ModelValidationResult"/> indicating the results of validating the model value.
    /// </returns>
    ValueTask<IReadOnlyList<ModelValidationResult>> ValidateAsync(
        ModelValidationContext context,
        CancellationToken cancellationToken = default);
}
