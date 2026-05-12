// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Validation;

[assembly: MetadataUpdateHandler(typeof(Microsoft.AspNetCore.Components.Forms.EditContextDataAnnotationsExtensions))]

namespace Microsoft.AspNetCore.Components.Forms;

/// <summary>
/// Extension methods to add DataAnnotations validation to an <see cref="EditContext"/>.
/// </summary>
public static partial class EditContextDataAnnotationsExtensions
{
    /// <summary>
    /// Enables DataAnnotations validation support for the <see cref="EditContext"/>.
    /// </summary>
    /// <param name="editContext">The <see cref="EditContext"/>.</param>
    /// <param name="serviceProvider">The <see cref="IServiceProvider"/> to be used in the <see cref="ValidationContext"/>.</param>
    /// <returns>A disposable object whose disposal will remove DataAnnotations validation support from the <see cref="EditContext"/>.</returns>
    public static IDisposable EnableDataAnnotationsValidation(this EditContext editContext, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return new DataAnnotationsEventSubscriptions(editContext, serviceProvider);
    }

    private static event Action? OnClearCache;

#pragma warning disable IDE0051 // Remove unused private members
    private static void ClearCache(Type[]? _)
    {
        OnClearCache?.Invoke();
    }
#pragma warning restore IDE0051 // Remove unused private members

    private sealed partial class DataAnnotationsEventSubscriptions : IDisposable
    {
        private static readonly ConcurrentDictionary<(Type ModelType, string FieldName), PropertyInfo?> _propertyInfoCache = new();

        private readonly EditContext _editContext;
        private readonly IServiceProvider? _serviceProvider;
        private readonly ValidationMessageStore _messages;
        private readonly ValidationOptions? _validationOptions;
#pragma warning disable ASP0029 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        private readonly IValidatableInfo? _validatorTypeInfo;
#pragma warning restore ASP0029 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        private readonly Dictionary<string, FieldIdentifier> _validationPathToFieldIdentifierMapping = new();

        [UnconditionalSuppressMessage("Trimming", "IL2066", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
        public DataAnnotationsEventSubscriptions(EditContext editContext, IServiceProvider serviceProvider)
        {
            _editContext = editContext ?? throw new ArgumentNullException(nameof(editContext));
            _serviceProvider = serviceProvider;
            _messages = new ValidationMessageStore(_editContext);
            _validationOptions = _serviceProvider?.GetService<IOptions<ValidationOptions>>()?.Value;
#pragma warning disable ASP0029 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            _validatorTypeInfo = _validationOptions != null && _validationOptions.TryGetValidatableTypeInfo(_editContext.Model.GetType(), out var typeInfo)
                ? typeInfo
                : null;
#pragma warning restore ASP0029 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            _editContext.OnFieldChanged += OnFieldChanged;
            _editContext.OnValidationRequested += OnValidationRequested;
            _editContext.OnAsyncValidationRequested += OnAsyncValidationRequested;

            if (MetadataUpdater.IsSupported)
            {
                OnClearCache += ClearCache;
            }
        }

        // ── Field-level: async via Validator.TryValidatePropertyAsync ──

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
        private void OnFieldChanged(object? sender, FieldChangedEventArgs eventArgs)
        {
            var fieldIdentifier = eventArgs.FieldIdentifier;

            // Use core runtime Validator.TryValidatePropertyAsync() which handles both
            // sync and async attributes correctly via two-phase execution:
            // Phase 1 runs all sync attributes; Phase 2 runs async only if sync passed.
            if (TryGetValidatableProperty(fieldIdentifier, out var propertyInfo))
            {
                var cts = new CancellationTokenSource();
                var task = ValidateFieldWithRuntimeAsync(fieldIdentifier, propertyInfo, cts.Token);

                if (task.IsCompleted)
                {
                    // Sync-only attributes — completed immediately, no async tracking needed
                    cts.Dispose();
                }
                else
                {
                    // Truly async — clear messages immediately to show neutral state
                    // while async validation runs, then track the pending task
                    _messages.Clear(fieldIdentifier);
                    _editContext.NotifyValidationStateChanged();
                    _editContext.AddValidationTask(fieldIdentifier, task, cts);
                }
            }
        }

        /// <summary>
        /// Field-level async validation using core runtime Validator.TryValidatePropertyAsync().
        /// Mirrors the form-level async pattern. When all attributes are synchronous, this
        /// completes synchronously with zero async overhead.
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
        private async Task ValidateFieldWithRuntimeAsync(
            FieldIdentifier fieldIdentifier,
            PropertyInfo reflectedProperty,
            CancellationToken cancellationToken)
        {
            var propertyValue = reflectedProperty.GetValue(fieldIdentifier.Model);
            var validationContext = new ValidationContext(
                fieldIdentifier.Model, _serviceProvider, items: null)
            {
                MemberName = reflectedProperty.Name
            };
            var results = new List<ValidationResult>();

            try
            {
                await Validator.TryValidatePropertyAsync(
                    propertyValue, validationContext, results, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _messages.Clear(fieldIdentifier);
                _editContext.NotifyValidationStateChanged();
                return;
            }

            _messages.Clear(fieldIdentifier);
            foreach (var result in CollectionsMarshal.AsSpan(results))
            {
                _messages.Add(fieldIdentifier, result.ErrorMessage!);
            }

            _editContext.NotifyValidationStateChanged();
        }

        // ── Form-level: sync handler for Validate(), async handler for ValidateAsync() ──

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
        private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
        {
            var validationContext = new ValidationContext(_editContext.Model, _serviceProvider, items: null);

            if (!TryValidateTypeInfo(validationContext))
            {
                ValidateWithDefaultValidator(validationContext);
            }

            _editContext.NotifyValidationStateChanged();
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
        private async Task OnAsyncValidationRequested(object? sender, ValidationRequestedEventArgs e)
        {
            var validationContext = new ValidationContext(_editContext.Model, _serviceProvider, items: null);

            if (!await TryValidateTypeInfoAsync(validationContext))
            {
                await ValidateWithDefaultValidatorAsync(validationContext);
            }

            _editContext.NotifyValidationStateChanged();
        }

        // ── Sync form-level validators (for Validate()) ──

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
        private void ValidateWithDefaultValidator(ValidationContext validationContext)
        {
            var validationResults = new List<ValidationResult>();
            Validator.TryValidateObject(_editContext.Model, validationContext, validationResults, true);

            // Transfer results to the ValidationMessageStore
            _messages.Clear();
            foreach (var validationResult in validationResults)
            {
                if (validationResult is null)
                {
                    continue;
                }

                var hasMemberNames = false;
                foreach (var memberName in validationResult.MemberNames)
                {
                    hasMemberNames = true;
                    _messages.Add(_editContext.Field(memberName), validationResult.ErrorMessage!);
                }

                if (!hasMemberNames)
                {
                    _messages.Add(new FieldIdentifier(_editContext.Model, fieldName: string.Empty), validationResult.ErrorMessage!);
                }
            }
        }

#pragma warning disable ASP0029 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        private bool TryValidateTypeInfo(ValidationContext validationContext)
        {
            if (_validatorTypeInfo is null)
            {
                return false;
            }

            var validateContext = new ValidateContext
            {
                ValidationOptions = _validationOptions!,
                ValidationContext = validationContext,
            };
            try
            {
                validateContext.OnValidationError += AddMapping;

                var validationTask = _validatorTypeInfo.ValidateAsync(_editContext.Model, validateContext, CancellationToken.None);
                if (!validationTask.IsCompleted)
                {
                    throw new InvalidOperationException("Async validation is not supported in the synchronous Validate() path. Use ValidateAsync() instead.");
                }

                var validationErrors = validateContext.ValidationErrors;

                // Transfer results to the ValidationMessageStore
                _messages.Clear();

                if (validationErrors is not null && validationErrors.Count > 0)
                {
                    foreach (var (fieldKey, messages) in validationErrors)
                    {
                        var fieldIdentifier = _validationPathToFieldIdentifierMapping[fieldKey];
                        _messages.Add(fieldIdentifier, messages);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                validateContext.OnValidationError -= AddMapping;
                _validationPathToFieldIdentifierMapping.Clear();
            }

            return true;
        }

        // ── Async form-level validators (for ValidateAsync()) ──

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
        private async Task ValidateWithDefaultValidatorAsync(ValidationContext validationContext)
        {
            var validationResults = new List<ValidationResult>();
            await Validator.TryValidateObjectAsync(
                _editContext.Model, validationContext, validationResults,
                validateAllProperties: true, CancellationToken.None);

            // Transfer results to the ValidationMessageStore
            _messages.Clear();
            foreach (var validationResult in validationResults)
            {
                if (validationResult is null)
                {
                    continue;
                }

                var hasMemberNames = false;
                foreach (var memberName in validationResult.MemberNames)
                {
                    hasMemberNames = true;
                    _messages.Add(_editContext.Field(memberName), validationResult.ErrorMessage!);
                }

                if (!hasMemberNames)
                {
                    _messages.Add(new FieldIdentifier(_editContext.Model, fieldName: string.Empty), validationResult.ErrorMessage!);
                }
            }
        }

#pragma warning disable ASP0029 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        private async Task<bool> TryValidateTypeInfoAsync(ValidationContext validationContext)
        {
            if (_validatorTypeInfo is null)
            {
                return false;
            }

            var validateContext = new ValidateContext
            {
                ValidationOptions = _validationOptions!,
                ValidationContext = validationContext,
            };
            try
            {
                validateContext.OnValidationError += AddMapping;

                await _validatorTypeInfo.ValidateAsync(_editContext.Model, validateContext, CancellationToken.None);

                var validationErrors = validateContext.ValidationErrors;

                // Transfer results to the ValidationMessageStore
                _messages.Clear();

                if (validationErrors is not null && validationErrors.Count > 0)
                {
                    foreach (var (fieldKey, messages) in validationErrors)
                    {
                        var fieldIdentifier = _validationPathToFieldIdentifierMapping[fieldKey];
                        _messages.Add(fieldIdentifier, messages);
                    }
                }
            }
            finally
            {
                validateContext.OnValidationError -= AddMapping;
                _validationPathToFieldIdentifierMapping.Clear();
            }

            return true;
        }

        private void AddMapping(ValidationErrorContext context)
        {
            _validationPathToFieldIdentifierMapping[context.Path] =
                new FieldIdentifier(context.Container ?? _editContext.Model, context.Name);
        }
#pragma warning restore ASP0029

        public void Dispose()
        {
            _messages.Clear();
            _editContext.OnFieldChanged -= OnFieldChanged;
            _editContext.OnValidationRequested -= OnValidationRequested;
            _editContext.OnAsyncValidationRequested -= OnAsyncValidationRequested;
            _editContext.NotifyValidationStateChanged();

            if (MetadataUpdater.IsSupported)
            {
                OnClearCache -= ClearCache;
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2080", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
        private static bool TryGetValidatableProperty(in FieldIdentifier fieldIdentifier, [NotNullWhen(true)] out PropertyInfo? propertyInfo)
        {
            var cacheKey = (ModelType: fieldIdentifier.Model.GetType(), fieldIdentifier.FieldName);
            if (!_propertyInfoCache.TryGetValue(cacheKey, out propertyInfo))
            {
                // DataAnnotations only validates public properties, so that's all we'll look for
                // If we can't find it, cache 'null' so we don't have to try again next time
                propertyInfo = cacheKey.ModelType.GetProperty(cacheKey.FieldName);

                // No need to lock, because it doesn't matter if we write the same value twice
                _propertyInfoCache[cacheKey] = propertyInfo;
            }

            return propertyInfo != null;
        }

        internal void ClearCache()
        {
            _propertyInfoCache.Clear();
        }
    }
}
