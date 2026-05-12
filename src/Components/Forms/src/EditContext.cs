// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Linq.Expressions;

namespace Microsoft.AspNetCore.Components.Forms;

/// <summary>
/// Holds metadata related to a data editing process, such as flags to indicate which
/// fields have been modified and the current set of validation messages.
/// </summary>
public sealed class EditContext
{
    // Note that EditContext tracks state for any FieldIdentifier you give to it, plus
    // the underlying storage is sparse. As such, none of the APIs have a "field not found"
    // error state. If you give us an unrecognized FieldIdentifier, that just means we
    // didn't yet track any state for it, so we behave as if it's in the default state
    // (valid and unmodified).
    private readonly Dictionary<FieldIdentifier, FieldState> _fieldStates = new Dictionary<FieldIdentifier, FieldState>();

    /// <summary>
    /// Constructs an instance of <see cref="EditContext"/>.
    /// </summary>
    /// <param name="model">The model object for the <see cref="EditContext"/>. This object should hold the data being edited, for example as a set of properties.</param>
    public EditContext(object model)
    {
        // The only reason we disallow null is because you'd almost always want one, and if you
        // really don't, you can pass an empty object then ignore it. Ensuring it's nonnull
        // simplifies things for all consumers of EditContext.
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Properties = new EditContextProperties();
    }

    /// <summary>
    /// An event that is raised when a field value changes.
    /// </summary>
    public event EventHandler<FieldChangedEventArgs>? OnFieldChanged;

    /// <summary>
    /// An event that is raised when validation is requested.
    /// </summary>
    public event EventHandler<ValidationRequestedEventArgs>? OnValidationRequested;

    /// <summary>
    /// An event that is raised when validation state has changed.
    /// </summary>
    public event EventHandler<ValidationStateChangedEventArgs>? OnValidationStateChanged;

    /// <summary>
    /// Supplies a <see cref="FieldIdentifier"/> corresponding to a specified field name
    /// on this <see cref="EditContext"/>'s <see cref="Model"/>.
    /// </summary>
    /// <param name="fieldName">The name of the editable field.</param>
    /// <returns>A <see cref="FieldIdentifier"/> corresponding to a specified field name on this <see cref="EditContext"/>'s <see cref="Model"/>.</returns>
    public FieldIdentifier Field(string fieldName)
        => new FieldIdentifier(Model, fieldName);

    /// <summary>
    /// Gets the model object for this <see cref="EditContext"/>.
    /// </summary>
    public object Model { get; }

    /// <summary>
    /// Gets a collection of arbitrary properties associated with this instance.
    /// </summary>
    public EditContextProperties Properties { get; }

    /// <summary>
    /// Gets whether field identifiers should be generated for &lt;input&gt; elements.
    /// </summary>
    public bool ShouldUseFieldIdentifiers { get; set; } = !OperatingSystem.IsBrowser();

    /// <summary>
    /// Signals that the value for the specified field has changed.
    /// </summary>
    /// <param name="fieldIdentifier">Identifies the field whose value has been changed.</param>
    public void NotifyFieldChanged(in FieldIdentifier fieldIdentifier)
    {
        GetOrAddFieldState(fieldIdentifier).IsModified = true;
        OnFieldChanged?.Invoke(this, new FieldChangedEventArgs(fieldIdentifier));
    }

    /// <summary>
    /// Signals that some aspect of validation state has changed.
    /// </summary>
    public void NotifyValidationStateChanged()
    {
        OnValidationStateChanged?.Invoke(this, ValidationStateChangedEventArgs.Empty);
    }

    /// <summary>
    /// Clears any modification flag that may be tracked for the specified field.
    /// </summary>
    /// <param name="fieldIdentifier">Identifies the field whose modification flag (if any) should be cleared.</param>
    public void MarkAsUnmodified(in FieldIdentifier fieldIdentifier)
    {
        if (_fieldStates.TryGetValue(fieldIdentifier, out var state))
        {
            state.IsModified = false;
        }
    }

    /// <summary>
    /// Clears all modification flags within this <see cref="EditContext"/>.
    /// </summary>
    public void MarkAsUnmodified()
    {
        foreach (var state in _fieldStates)
        {
            state.Value.IsModified = false;
        }
    }

    /// <summary>
    /// Determines whether any of the fields in this <see cref="EditContext"/> have been modified.
    /// </summary>
    /// <returns>True if any of the fields in this <see cref="EditContext"/> have been modified; otherwise false.</returns>
    public bool IsModified()
    {
        // If necessary, we could consider caching the overall "is modified" state and only recomputing
        // when there's a call to NotifyFieldModified/NotifyFieldUnmodified
        foreach (var state in _fieldStates)
        {
            if (state.Value.IsModified)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the current validation messages across all fields.
    ///
    /// This method does not perform validation itself. It only returns messages determined by previous validation actions.
    /// </summary>
    /// <returns>The current validation messages.</returns>
    public IEnumerable<string> GetValidationMessages()
    {
        // Since we're only enumerating the fields for which we have a non-null state, the cost of this grows
        // based on how many fields have been modified or have associated validation messages
        foreach (var state in _fieldStates)
        {
            foreach (var message in state.Value.GetValidationMessages())
            {
                yield return message;
            }
        }
    }

    /// <summary>
    /// Gets the current validation messages for the specified field.
    ///
    /// This method does not perform validation itself. It only returns messages determined by previous validation actions.
    /// </summary>
    /// <param name="fieldIdentifier">Identifies the field whose current validation messages should be returned.</param>
    /// <returns>The current validation messages for the specified field.</returns>
    public IEnumerable<string> GetValidationMessages(FieldIdentifier fieldIdentifier)
    {
        if (_fieldStates.TryGetValue(fieldIdentifier, out var state))
        {
            foreach (var message in state.GetValidationMessages())
            {
                yield return message;
            }
        }
    }

    /// <summary>
    /// Gets the current validation messages for the specified field.
    ///
    /// This method does not perform validation itself. It only returns messages determined by previous validation actions.
    /// </summary>
    /// <param name="accessor">Identifies the field whose current validation messages should be returned.</param>
    /// <returns>The current validation messages for the specified field.</returns>
    public IEnumerable<string> GetValidationMessages(Expression<Func<object>> accessor)
        => GetValidationMessages(FieldIdentifier.Create(accessor));

    /// <summary>
    /// Determines whether the specified fields in this <see cref="EditContext"/> has been modified.
    /// </summary>
    /// <returns>True if the field has been modified; otherwise false.</returns>
    public bool IsModified(in FieldIdentifier fieldIdentifier)
        => _fieldStates.TryGetValue(fieldIdentifier, out var state)
        ? state.IsModified
        : false;

    /// <summary>
    /// Determines whether the specified fields in this <see cref="EditContext"/> has been modified.
    /// </summary>
    /// <param name="accessor">Identifies the field whose current validation messages should be returned.</param>
    /// <returns>True if the field has been modified; otherwise false.</returns>
    public bool IsModified(Expression<Func<object>> accessor)
        => IsModified(FieldIdentifier.Create(accessor));

    /// <summary>
    /// Determines whether the specified fields in this <see cref="EditContext"/> has no associated validation messages.
    /// </summary>
    /// <returns>True if the field has no associated validation messages after validation; otherwise false.</returns>
    public bool IsValid(in FieldIdentifier fieldIdentifier)
        => !GetValidationMessages(fieldIdentifier).Any();

    /// <summary>
    /// Determines whether the specified fields in this <see cref="EditContext"/> has no associated validation messages.
    /// </summary>
    /// <param name="accessor">Identifies the field whose current validation messages should be returned.</param>
    /// <returns>True if the field has no associated validation messages after validation; otherwise false.</returns>
    public bool IsValid(Expression<Func<object>> accessor)
        => IsValid(FieldIdentifier.Create(accessor));

    /// <summary>
    /// Validates this <see cref="EditContext"/>.
    /// </summary>
    /// <returns>True if there are no validation messages after validation; otherwise false.</returns>
    public bool Validate()
    {
        OnValidationRequested?.Invoke(this, ValidationRequestedEventArgs.Empty);
        return !GetValidationMessages().Any();
    }

    /// <summary>
    /// Validates this <see cref="EditContext"/> asynchronously, supporting
    /// async validation attributes and async validatable objects.
    /// </summary>
    /// <returns>True if there are no validation messages after validation; otherwise false.</returns>
    public async Task<bool> ValidateAsync()
    {
        CancelAllPendingValidationTasks();

        if (OnAsyncValidationRequested is not null)
        {
            await OnAsyncValidationRequested.Invoke(this, ValidationRequestedEventArgs.Empty);
        }
        else
        {
            OnValidationRequested?.Invoke(this, ValidationRequestedEventArgs.Empty);
        }

        return !GetValidationMessages().Any();
    }

    /// <summary>
    /// Registers an async validation task for a specific field. If there is already
    /// a pending validation task for the field, it will be cancelled and replaced.
    /// </summary>
    /// <param name="fieldIdentifier">Identifies the field being validated.</param>
    /// <param name="task">The async validation task to track.</param>
    /// <param name="cts">The <see cref="CancellationTokenSource"/> that can cancel the task.</param>
    public void AddValidationTask(in FieldIdentifier fieldIdentifier, Task task, CancellationTokenSource cts)
    {
        var state = GetOrAddFieldState(fieldIdentifier);
        if (state.PendingValidationCts is { } previousCts)
        {
            previousCts.Cancel();
            previousCts.Dispose();
        }

        state.PendingValidationTask = task;
        state.PendingValidationCts = cts;
        state.IsValidationFaulted = false;
        NotifyValidationStateChanged();
        _ = ObserveValidationTaskAsync(state, task);
    }

    /// <summary>
    /// Determines whether the specified field has a pending async validation task.
    /// </summary>
    /// <param name="fieldIdentifier">Identifies the field to check.</param>
    /// <returns>True if the field has a pending validation task; otherwise false.</returns>
    public bool IsValidationPending(in FieldIdentifier fieldIdentifier)
        => _fieldStates.TryGetValue(fieldIdentifier, out var state)
            && state.PendingValidationTask is { IsCompleted: false };

    /// <summary>
    /// Determines whether the specified field has a pending async validation task.
    /// </summary>
    /// <param name="accessor">Identifies the field to check.</param>
    /// <returns>True if the field has a pending validation task; otherwise false.</returns>
    public bool IsValidationPending<TField>(Expression<Func<TField>> accessor)
        => IsValidationPending(FieldIdentifier.Create(accessor));

    /// <summary>
    /// Determines whether any field in this <see cref="EditContext"/> has a pending async validation task.
    /// </summary>
    /// <returns>True if any field has a pending validation task; otherwise false.</returns>
    public bool IsValidationPending()
    {
        foreach (var state in _fieldStates)
        {
            if (state.Value.PendingValidationTask is { IsCompleted: false })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the specified field's last async validation task faulted.
    /// </summary>
    /// <param name="fieldIdentifier">Identifies the field to check.</param>
    /// <returns>True if the field's last validation task faulted; otherwise false.</returns>
    public bool IsValidationFaulted(in FieldIdentifier fieldIdentifier)
        => _fieldStates.TryGetValue(fieldIdentifier, out var state)
            && state.IsValidationFaulted;

    /// <summary>
    /// Determines whether the specified field's last async validation task faulted.
    /// </summary>
    /// <param name="accessor">Identifies the field to check.</param>
    /// <returns>True if the field's last validation task faulted; otherwise false.</returns>
    public bool IsValidationFaulted<TField>(Expression<Func<TField>> accessor)
        => IsValidationFaulted(FieldIdentifier.Create(accessor));

    /// <summary>
    /// Determines whether any field in this <see cref="EditContext"/> has a faulted async validation task.
    /// </summary>
    /// <returns>True if any field has a faulted validation task; otherwise false.</returns>
    public bool IsValidationFaulted()
    {
        foreach (var state in _fieldStates)
        {
            if (state.Value.IsValidationFaulted)
            {
                return true;
            }
        }

        return false;
    }

    private void CancelAllPendingValidationTasks()
    {
        foreach (var state in _fieldStates)
        {
            if (state.Value.PendingValidationCts is { } cts)
            {
                cts.Cancel();
                cts.Dispose();
                state.Value.PendingValidationCts = null;
                state.Value.PendingValidationTask = null;
            }
        }
    }

    private async Task ObserveValidationTaskAsync(FieldState state, Task task)
    {
        try
        {
            await task;

            // Completed successfully — clear pending state
            if (ReferenceEquals(state.PendingValidationTask, task))
            {
                state.PendingValidationTask = null;
                if (state.PendingValidationCts is { } cts)
                {
                    cts.Dispose();
                    state.PendingValidationCts = null;
                }

                NotifyValidationStateChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled — silently clear state without notification
            if (ReferenceEquals(state.PendingValidationTask, task))
            {
                state.PendingValidationTask = null;
                if (state.PendingValidationCts is { } cts)
                {
                    cts.Dispose();
                    state.PendingValidationCts = null;
                }
            }
        }
        catch (Exception)
        {
            // Faulted — mark as faulted and notify
            if (ReferenceEquals(state.PendingValidationTask, task))
            {
                state.IsValidationFaulted = true;
                state.PendingValidationTask = null;
                if (state.PendingValidationCts is { } cts)
                {
                    cts.Dispose();
                    state.PendingValidationCts = null;
                }

                NotifyValidationStateChanged();
            }
        }
    }

    /// <summary>
    /// An event that is raised when async validation is requested.
    /// </summary>
    public Func<object?, ValidationRequestedEventArgs, Task>? OnAsyncValidationRequested;

    internal FieldState? GetFieldState(in FieldIdentifier fieldIdentifier)
    {
        _fieldStates.TryGetValue(fieldIdentifier, out var state);
        return state;
    }

    internal FieldState GetOrAddFieldState(in FieldIdentifier fieldIdentifier)
    {
        if (!_fieldStates.TryGetValue(fieldIdentifier, out var state))
        {
            state = new FieldState(fieldIdentifier);
            _fieldStates.Add(fieldIdentifier, state);
        }

        return state;
    }
}
