// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

namespace Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

/// <summary>
/// Provides methods to validate an object graph.
/// </summary>
public interface IObjectModelValidator
{
    /// <summary>
    /// Validates the provided object.
    /// </summary>
    /// <param name="actionContext">The <see cref="ActionContext"/> associated with the current request.</param>
    /// <param name="validationState">The <see cref="ValidationStateDictionary"/>. May be null.</param>
    /// <param name="prefix">
    /// The model prefix. Used to map the model object to entries in <paramref name="validationState"/>.
    /// </param>
    /// <param name="model">The model object.</param>
    void Validate(
        ActionContext actionContext,
        ValidationStateDictionary? validationState,
        string prefix,
        object? model);

    /// <summary>
    /// Validates the provided object asynchronously, supporting async validation attributes.
    /// </summary>
    /// <param name="actionContext">The <see cref="ActionContext"/> associated with the current request.</param>
    /// <param name="validationState">The <see cref="ValidationStateDictionary"/>. May be null.</param>
    /// <param name="prefix">
    /// The model prefix. Used to map the model object to entries in <paramref name="validationState"/>.
    /// </param>
    /// <param name="model">The model object.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous validation operation.</returns>
    Task ValidateAsync(
        ActionContext actionContext,
        ValidationStateDictionary? validationState,
        string prefix,
        object? model,
        CancellationToken cancellationToken = default)
    {
        Validate(actionContext, validationState, prefix, model);
        return Task.CompletedTask;
    }
}
