# Async Validation — ASP.NET Core Changes Documentation

> **Branch:** `ViveliDuCh/aspnetcore@async-validation`
> **Runtime dependency:** `ViveliDuCh/runtime@async-validation` (`D:\git-worktrees\runtime\async-validation`)
> **15 files changed, 505 insertions(+), 16 deletions(-)**

---

## Table of Contents

1. [DLL Injection: How to Build Against the Runtime Branch](#dll-injection)
2. [Tier 7: Extensions.Validation (M.E.Validation)](#tier-7)
3. [Tier 11: Minimal APIs (No Changes)](#tier-11)
4. [Tier 4: Blazor Forms](#tier-4)
5. [Tier 3: MVC Model Validation](#tier-3)
6. [Build Commands Summary](#build-commands)

---

## <a id="dll-injection"></a>DLL Injection: How to Build Against the Runtime Branch

### The Problem

Aspnetcore compiles against `System.ComponentModel.Annotations.dll` from the .NET ref pack. The stock version does **not** contain `AsyncValidationAttribute`, `IAsyncValidatableObject`, or `Validator.*Async()`. These types only exist in the `ViveliDuCh/runtime@async-validation` branch.

### Key Discovery

Aspnetcore resolves the ref assembly from the **NuGet packages cache**, NOT from `.dotnet\packs\`:

```
Actual compile-time ref:  C:\Users\vivianad\.nuget\packages\microsoft.netcore.app.ref\11.0.0-preview.5.26230.101\ref\net11.0\System.ComponentModel.Annotations.dll
NOT used:                 D:\git-worktrees\aspnetcore\async-validation\.dotnet\packs\Microsoft.NETCore.App.Ref\...\ref\net11.0\System.ComponentModel.Annotations.dll
```

### Step 1: Build the Runtime Branch

```powershell
cd D:\git-worktrees\runtime\async-validation

# Build libs (produces ref + impl DLLs)
.\build.cmd libs -rc Debug
```

**Output DLLs that matter:**

| DLL | Path | Purpose |
|-----|------|---------|
| Ref assembly (compiler sees types) | `artifacts\bin\microsoft.netcore.app.ref\ref\net11.0\System.ComponentModel.Annotations.dll` | Contains `AsyncValidationAttribute`, `IAsyncValidatableObject`, `Validator.*Async()` type signatures |
| Impl assembly (runtime loads code) | `artifacts\bin\runtime\net11.0-windows-Debug-x64\System.ComponentModel.Annotations.dll` | Contains the actual method implementations |

### Step 2: Overlay Into Aspnetcore

Run from **any directory** (PowerShell):

```powershell
$runtimeRoot = "D:\git-worktrees\runtime\async-validation"
$aspnetRoot  = "D:\git-worktrees\aspnetcore\async-validation"

# ── Source DLLs from runtime build ──
$runtimeRef  = "$runtimeRoot\artifacts\bin\microsoft.netcore.app.ref\ref\net11.0\System.ComponentModel.Annotations.dll"
$runtimeImpl = "$runtimeRoot\artifacts\bin\runtime\net11.0-windows-Debug-x64\System.ComponentModel.Annotations.dll"

# ── 1. Overlay into NuGet cache ref pack (THIS IS WHAT THE COMPILER USES) ──
$nugetRef = "C:\Users\vivianad\.nuget\packages\microsoft.netcore.app.ref\11.0.0-preview.5.26230.101\ref\net11.0\System.ComponentModel.Annotations.dll"
if (-not (Test-Path "$nugetRef.original")) { Copy-Item $nugetRef "$nugetRef.original" }
Copy-Item $runtimeRef $nugetRef -Force

# ── 2. Overlay into .dotnet ref pack (belt-and-suspenders) ──
$dotnetRef = "$aspnetRoot\.dotnet\packs\Microsoft.NETCore.App.Ref\11.0.0-preview.4.26210.111\ref\net11.0\System.ComponentModel.Annotations.dll"
if (-not (Test-Path "$dotnetRef.original")) { Copy-Item $dotnetRef "$dotnetRef.original" }
Copy-Item $runtimeRef $dotnetRef -Force

# ── 3. Overlay into shared runtimes (for test execution) ──
foreach ($v in @("11.0.0-preview.4.26210.111", "11.0.0-preview.5.26230.101")) {
    $target = "$aspnetRoot\.dotnet\shared\Microsoft.NETCore.App\$v\System.ComponentModel.Annotations.dll"
    if (Test-Path $target) {
        if (-not (Test-Path "$target.original")) { Copy-Item $target "$target.original" }
        Copy-Item $runtimeImpl $target -Force
    }
}

Write-Host "DLL overlay complete!"
```

### Step 3: After Rebuilding Runtime

If you change code in the runtime `async-validation` branch and rebuild:

```powershell
# 1. Rebuild runtime
cd D:\git-worktrees\runtime\async-validation
.\build.cmd libs -rc Debug

# 2. Re-run the overlay script from Step 2 above
#    (it will overwrite the previously-overlaid DLLs with the new build)
```

That's it — aspnetcore will pick up the new types on the next `dotnet build`.

### How to Restore Originals

```powershell
# Restore all backed-up originals
$nugetRef = "C:\Users\vivianad\.nuget\packages\microsoft.netcore.app.ref\11.0.0-preview.5.26230.101\ref\net11.0\System.ComponentModel.Annotations.dll"
Copy-Item "$nugetRef.original" $nugetRef -Force

$dotnetRef = "D:\git-worktrees\aspnetcore\async-validation\.dotnet\packs\Microsoft.NETCore.App.Ref\11.0.0-preview.4.26210.111\ref\net11.0\System.ComponentModel.Annotations.dll"
Copy-Item "$dotnetRef.original" $dotnetRef -Force

foreach ($v in @("11.0.0-preview.4.26210.111", "11.0.0-preview.5.26230.101")) {
    $target = "D:\git-worktrees\aspnetcore\async-validation\.dotnet\shared\Microsoft.NETCore.App\$v\System.ComponentModel.Annotations.dll"
    if (Test-Path "$target.original") { Copy-Item "$target.original" $target -Force }
}
```

---

## <a id="tier-7"></a>Tier 7: Extensions.Validation (M.E.Validation)

**Difficulty:** 🟢 Low — method signatures are already `async Task`, only leaf calls are sync
**Files changed:** 3
**Lines:** ~20 changed

### Why These Changes

The `Microsoft.Extensions.Validation` pipeline is already async end-to-end at the method level (`ValidateAsync` returns `Task`), but internally calls synchronous `attribute.GetValidationResult()`. These changes make the leaf calls async-aware so that `AsyncValidationAttribute` instances are validated asynchronously while stock sync attributes continue to work unchanged.

### Pattern Used

```csharp
// Async-aware dispatch: uses async path for AsyncValidationAttribute, sync for everything else
var result = attribute is AsyncValidationAttribute asyncAttr
    ? await asyncAttr.GetValidationResultAsync(value, context.ValidationContext, cancellationToken)
    : attribute.GetValidationResult(value, context.ValidationContext);
```

### File 1: `src/Validation/src/ValidatablePropertyInfo.cs`

**What changed and why:**

- **`ValidateValue` → `ValidateValueAsync`** — The local function that loops over validation attributes was sync (`void`). Converted to `async ValueTask` with a `CancellationToken` parameter so it can `await` async attributes.
- **Per-attribute loop** — Each `attribute.GetValidationResult()` call now checks if the attribute `is AsyncValidationAttribute` and dispatches accordingly.
- **Required attribute check left sync** — `_requiredAttribute` is typed as `RequiredAttribute`, which is a sealed stock type that can never be an `AsyncValidationAttribute`. The C# compiler enforces this (CS8121 error if you try `_requiredAttribute is AsyncValidationAttribute`). The required check stays sync — correct behavior since `RequiredAttribute` is always sync.

```diff
 // Line 92: Call site
-ValidateValue(propertyValue, Name, context.CurrentValidationPath, validationAttributes, value);
+await ValidateValueAsync(propertyValue, Name, context.CurrentValidationPath, validationAttributes, value, cancellationToken);

 // Lines 150-173: Local function signature + body
-void ValidateValue(object? val, string name, string errorPrefix, ValidationAttribute[] validationAttributes, object? container)
+async ValueTask ValidateValueAsync(object? val, string name, string errorPrefix, ValidationAttribute[] validationAttributes, object? container, CancellationToken ct)
 {
     for (var i = 0; i < validationAttributes.Length; i++)
     {
         var attribute = validationAttributes[i];
         try
         {
-            var result = attribute.GetValidationResult(val, context.ValidationContext);
+            var result = attribute is AsyncValidationAttribute asyncAttr
+                ? await asyncAttr.GetValidationResultAsync(val, context.ValidationContext, ct)
+                : attribute.GetValidationResult(val, context.ValidationContext);
             // ... error handling unchanged ...
```

### File 2: `src/Validation/src/ValidatableTypeInfo.cs`

**What changed and why:**

- **`ValidateTypeAttributes` → `ValidateTypeAttributesAsync`** — Converted from `void` to `async Task` with `CancellationToken`. The attribute loop now dispatches async for `AsyncValidationAttribute`.
- **`ValidateValidatableObjectInterface` → `ValidateValidatableObjectInterfaceAsync`** — Converted from `void` to `async Task`. Now checks for `IAsyncValidatableObject` FIRST (using `value is IAsyncValidatableObject`), falls back to `IValidatableObject` if not implemented. This is the only place where `IAsyncValidatableObject` is consumed in the Extensions.Validation pipeline.
- **Call sites in `ValidateAsync`** — Updated to `await` the new async methods.

```diff
 // Line 87: Type-level attribute call site
-ValidateTypeAttributes(value, context);
+await ValidateTypeAttributesAsync(value, context, cancellationToken);

 // Line 96: IValidatableObject call site
-ValidateValidatableObjectInterface(value, context);
+await ValidateValidatableObjectInterfaceAsync(value, context, cancellationToken);

 // Lines 125-150: Type attributes method
-private void ValidateTypeAttributes(object? value, ValidateContext context)
+private async Task ValidateTypeAttributesAsync(object? value, ValidateContext context, CancellationToken cancellationToken)
 {
     // ...
-    var result = attribute.GetValidationResult(value, context.ValidationContext);
+    var result = attribute is AsyncValidationAttribute asyncAttr
+        ? await asyncAttr.GetValidationResultAsync(value, context.ValidationContext, cancellationToken)
+        : attribute.GetValidationResult(value, context.ValidationContext);

 // Lines 152-190: IValidatableObject method — now also supports IAsyncValidatableObject
-private void ValidateValidatableObjectInterface(object? value, ValidateContext context)
+private async Task ValidateValidatableObjectInterfaceAsync(object? value, ValidateContext context, CancellationToken cancellationToken)
 {
-    if (Type.ImplementsInterface(typeof(IValidatableObject)) && value is IValidatableObject validatable)
+    if (value is IAsyncValidatableObject asyncValidatable)
+    {
+        // ... async path: await asyncValidatable.ValidateAsync() ...
+    }
+    else if (Type.ImplementsInterface(typeof(IValidatableObject)) && value is IValidatableObject validatable)
     {
         // ... existing sync path UNCHANGED ...
     }
```

### File 3: `src/Validation/src/ValidatableParameterInfo.cs`

**What changed and why:**

Same pattern as `ValidatablePropertyInfo` — the per-attribute validation loop now dispatches async. The `_requiredAttribute` check is left sync for the same CS8121 reason.

```diff
 // Lines 88-91: Per-attribute loop
-var result = attribute.GetValidationResult(value, context.ValidationContext);
+var result = attribute is AsyncValidationAttribute asyncAttr
+    ? await asyncAttr.GetValidationResultAsync(value, context.ValidationContext, cancellationToken)
+    : attribute.GetValidationResult(value, context.ValidationContext);
```

---

## <a id="tier-11"></a>Tier 11: Minimal APIs (No Changes Needed)

**Difficulty:** 🟢 None — 0 files changed

### Why No Changes

Minimal APIs use `ValidationEndpointFilterFactory` which already:
1. Awaits `entry.Parameter.ValidateAsync(...)` (this calls into Tier 7's `ValidatableParameterInfo`)
2. Passes `CancellationToken` via `invocationContext.HttpContext.RequestAborted`
3. Returns `400 BadRequest` with `HttpValidationProblemDetails` on errors

Once Tier 7 changes land, **Minimal APIs automatically support async validation**. The entire pipeline from HTTP request → endpoint filter → parameter validation → attribute validation is already async. The only missing piece was the leaf calls, which Tier 7 fixes.

---

## <a id="tier-4"></a>Tier 4: Blazor Forms

**Difficulty:** 🟡 Medium — small localized changes, async infrastructure partially existed
**Files changed:** 4 (3 source + 1 public API baseline)
**Lines:** ~115 added

### Why These Changes

Blazor's `EditForm` calls `EditContext.Validate()` which is synchronous. The `DataAnnotationsValidator` component hooks into the sync `OnValidationRequested` event and explicitly **blocks async** with a sync guard:

```csharp
// BEFORE — EditContextDataAnnotationsExtensions.cs line 163-166:
var validationTask = _validatorTypeInfo.ValidateAsync(..., CancellationToken.None);
if (!validationTask.IsCompleted) {
    throw new InvalidOperationException("Async validation is not supported");
}
```

The Blazor changes add a parallel async path that bypasses this guard entirely.

### File 1: `src/Components/Forms/src/EditContext.cs`

**What changed and why:**

- **Added `ValidateAsync()` method** — Returns `Task<bool>`. Fires `OnAsyncValidationRequested` if a handler is registered, otherwise falls back to the sync `OnValidationRequested` event. This either-or design avoids double-fire and maintains backward compatibility.
- **Added `OnAsyncValidationRequested` field** — A `Func<object?, ValidationRequestedEventArgs, Task>?` delegate (not a C# `event`). The `DataAnnotationsValidator` hooks into this instead of the sync event.
- **XML doc comments** — Use plain text ("async validation attributes and async validatable objects") instead of `<see cref>` references to runtime types, because `EditContext` doesn't directly reference `System.ComponentModel.DataAnnotations` and the cref would fail to resolve (CS1574).

```diff
+public async Task<bool> ValidateAsync()
+{
+    if (OnAsyncValidationRequested is not null)
+    {
+        await OnAsyncValidationRequested.Invoke(this, ValidationRequestedEventArgs.Empty);
+    }
+    else
+    {
+        // Fall back to sync validation if no async handler registered
+        OnValidationRequested?.Invoke(this, ValidationRequestedEventArgs.Empty);
+    }
+    return !GetValidationMessages().Any();
+}
+
+public Func<object?, ValidationRequestedEventArgs, Task>? OnAsyncValidationRequested;
```

### File 2: `src/Components/Web/src/Forms/EditForm.cs`

**What changed and why:**

Single line change — the existing comment even said "This will likely become ValidateAsync later":

```diff
-var isValid = _editContext.Validate(); // This will likely become ValidateAsync later
+var isValid = await _editContext.ValidateAsync();
```

`HandleSubmitAsync` is already an `async Task` method, so `await` works directly.

### File 3: `src/Components/Forms/src/EditContextDataAnnotationsExtensions.cs`

**What changed and why:**

- **Registration** — Hooks `OnAsyncValidationRequested` instead of `OnValidationRequested`. This routes form-submit validation through the new async path.
- **`OnAsyncValidationRequested` handler** — New `async Task` method that mirrors the sync `OnValidationRequested` but calls `TryValidateTypeInfoAsync` and `ValidateWithDefaultValidatorAsync`.
- **`TryValidateTypeInfoAsync`** — The async version of `TryValidateTypeInfo` that properly `await`s `_validatorTypeInfo.ValidateAsync()` instead of checking `.IsCompleted` and throwing. This is what removes the "Async validation is not supported" sync guard.
- **`ValidateWithDefaultValidatorAsync`** — Uses `Validator.TryValidateObjectAsync()` from the runtime (the Phase 1 async API) as the fallback when no source-generated `ValidatableTypeInfo` is available.
- **`#pragma warning disable ASP0029`** — Required because `TryValidateTypeInfoAsync` uses experimental types (`ValidateContext`, `ValidationErrorContext`). The pragma scope was adjusted to cover both the new async method and the existing `AddMapping` helper.
- **Disposal** — Unhooks `OnAsyncValidationRequested` instead of `OnValidationRequested`.
- **Existing sync methods preserved** — `OnValidationRequested`, `TryValidateTypeInfo`, `ValidateWithDefaultValidator` are all kept unchanged for backward compatibility.

### File 4: `src/Components/Forms/src/PublicAPI.Unshipped.txt`

Added public API entries for the two new public members on `EditContext`:

```
Microsoft.AspNetCore.Components.Forms.EditContext.OnAsyncValidationRequested -> System.Func<...>?
Microsoft.AspNetCore.Components.Forms.EditContext.ValidateAsync() -> System.Threading.Tasks.Task<bool>!
```

---

## <a id="tier-3"></a>Tier 3: MVC Model Validation

**Difficulty:** 🔴 High — 5 types changed, interface cascade through MVC infrastructure
**Files changed:** 8 (5 source + 1 new + 2 test + 2 public API baselines)
**Lines:** ~320 added

### Why These Changes

MVC has its **own validation pipeline** completely separate from `Validator.TryValidateObject()`. It calls `ValidationAttribute.GetValidationResult()` per attribute individually and walks the object graph via `ValidationVisitor`. To support async validation, MVC needs:

1. A new interface (`IAsyncModelValidator`) that validators can implement alongside `IModelValidator`
2. The built-in validators (`DataAnnotationsModelValidator`, `ValidatableObjectAdapter`) to implement it
3. The visitor to prefer the async path when available
4. The top-level `IObjectModelValidator` to expose an async entry point

### File 1 (NEW): `src/Mvc/Mvc.Abstractions/src/ModelBinding/Validation/IAsyncModelValidator.cs`

**Why a new file:**

MVC's existing `IModelValidator` interface returns `IEnumerable<ModelValidationResult>` synchronously. We can't change that signature without breaking every existing validator. Instead, we add a companion interface `IAsyncModelValidator` that validators can **optionally** implement. The validation pipeline checks `validators[i] is IAsyncModelValidator` and prefers the async path when available, falling back to `IModelValidator.Validate()` otherwise.

```csharp
public interface IAsyncModelValidator
{
    ValueTask<IReadOnlyList<ModelValidationResult>> ValidateAsync(
        ModelValidationContext context,
        CancellationToken cancellationToken = default);
}
```

### File 2: `src/Mvc/Mvc.DataAnnotations/src/DataAnnotationsModelValidator.cs`

**What changed and why:**

- **Added `IAsyncModelValidator` to class declaration** — The class is `internal sealed`, so this doesn't affect public API.
- **Implemented `ValidateAsync()`** — Mirrors the existing sync `Validate()` method exactly, but dispatches to `GetValidationResultAsync()` when the attribute `is AsyncValidationAttribute`. For stock sync attributes, it calls the regular `GetValidationResult()`. Localization handling (`_stringLocalizer`) is preserved identically.
- **Existing `Validate()` left unchanged** — Complete backward compatibility.

```diff
-internal sealed class DataAnnotationsModelValidator : IModelValidator
+internal sealed class DataAnnotationsModelValidator : IModelValidator, IAsyncModelValidator

+public async ValueTask<IReadOnlyList<ModelValidationResult>> ValidateAsync(
+    ModelValidationContext validationContext, CancellationToken cancellationToken = default)
+{
+    // ... same parameter setup as Validate() ...
+    ValidationResult? result;
+    if (Attribute is AsyncValidationAttribute asyncAttr)
+    {
+        result = await asyncAttr.GetValidationResultAsync(
+            validationContext.Model, context, cancellationToken);
+    }
+    else
+    {
+        result = Attribute.GetValidationResult(validationContext.Model, context);
+    }
+    // ... same result processing as Validate() ...
+}
```

### File 3: `src/Mvc/Mvc.DataAnnotations/src/ValidatableObjectAdapter.cs`

**What changed and why:**

- **Added `IAsyncModelValidator`** — Same pattern as `DataAnnotationsModelValidator`.
- **Implemented `ValidateAsync()`** — Checks for `IAsyncValidatableObject` first (async path), falls back to `IValidatableObject` (sync path). If neither is implemented, throws the same `InvalidOperationException` as the sync version.

```diff
-internal sealed class ValidatableObjectAdapter : IModelValidator
+internal sealed class ValidatableObjectAdapter : IModelValidator, IAsyncModelValidator

+public async ValueTask<IReadOnlyList<ModelValidationResult>> ValidateAsync(...)
+{
+    if (model is IAsyncValidatableObject asyncValidatable)
+        validationResults = await asyncValidatable.ValidateAsync(validationContext, cancellationToken);
+    else if (model is IValidatableObject validatable)
+        validationResults = validatable.Validate(validationContext);
+    else
+        throw new InvalidOperationException(...);
+}
```

### File 4: `src/Mvc/Mvc.Core/src/ModelBinding/Validation/ValidationVisitor.cs`

**What changed and why:**

- **Added `ValidateNodeAsync(CancellationToken)`** — A new `protected virtual` method alongside the existing `ValidateNode()`. In the validator loop, it checks `validators[i] is IAsyncModelValidator` and calls `ValidateAsync()`, otherwise falls back to sync `Validate()`. All result processing and `ModelState` population is identical.
- **Why `protected virtual`** — Matches the existing `ValidateNode()` pattern, allowing derived visitors to override.
- **Note:** The callers of `ValidateNode()` (in `Visit()`, `VisitChildren()`) have NOT been updated to call `ValidateNodeAsync()` yet — that cascade is a larger .NET 12+ effort. `ValidateNodeAsync()` is available for custom `ValidationVisitor` subclasses and future MVC pipeline changes.

### File 5: `src/Mvc/Mvc.Core/src/ModelBinding/Validation/IObjectModelValidator.cs`

**What changed and why:**

- **Added `ValidateAsync()` with default interface method (DIM)** — The default implementation calls sync `Validate()` and returns `Task.CompletedTask`. This means existing `IObjectModelValidator` implementations (like `DefaultObjectValidator`) don't break. Implementors can override to provide true async behavior.

```diff
+Task ValidateAsync(
+    ActionContext actionContext,
+    ValidationStateDictionary? validationState,
+    string prefix,
+    object? model,
+    CancellationToken cancellationToken = default)
+{
+    Validate(actionContext, validationState, prefix, model);
+    return Task.CompletedTask;
+}
```

### Test Files

- **`DataAnnotationsModelValidatorTest.cs`** — Added `ValidateAsync_UsesAsyncValidationAttribute` test with a `TestAsyncValidationAttribute` mock that verifies the async path is taken, `CancellationToken` is propagated, and `DisplayName`/`MemberName` are set correctly.
- **`ValidatableObjectAdapterTest.cs`** — Added `ValidateAsync_UsesIAsyncValidatableObject` test with an `AsyncSampleModel` that verifies `IAsyncValidatableObject.ValidateAsync()` is called in preference to `IValidatableObject.Validate()`.

### Public API Baselines

- `Mvc.Abstractions/src/PublicAPI.Unshipped.txt` — Added `IAsyncModelValidator` interface + `ValidateAsync` method.
- `Mvc.Core/src/PublicAPI.Unshipped.txt` — Added `IObjectModelValidator.ValidateAsync` DIM + `ValidationVisitor.ValidateNodeAsync`.

---

## <a id="build-commands"></a>Build Commands Summary

All commands run from the **repo root**: `D:\git-worktrees\aspnetcore\async-validation`

**Prerequisites:** Activate the local .NET environment first:
```powershell
. .\activate.ps1
```

### Build Individual Tiers

```powershell
# Tier 7: Extensions.Validation
dotnet build src\Validation\src\Microsoft.Extensions.Validation.csproj --no-restore -v:q

# Tier 4: Blazor Forms (EditContext + DataAnnotationsExtensions)
dotnet build src\Components\Forms\src\Microsoft.AspNetCore.Components.Forms.csproj --no-restore -v:q

# Tier 4: Blazor EditForm (Components.Web)
dotnet build src\Components\Web\src\Microsoft.AspNetCore.Components.Web.csproj --no-restore -v:q

# Tier 3: MVC Abstractions (IAsyncModelValidator)
dotnet build src\Mvc\Mvc.Abstractions\src\Microsoft.AspNetCore.Mvc.Abstractions.csproj --no-restore -v:q

# Tier 3: MVC Core (ValidationVisitor, IObjectModelValidator)
dotnet build src\Mvc\Mvc.Core\src\Microsoft.AspNetCore.Mvc.Core.csproj --no-restore -v:q

# Tier 3: MVC DataAnnotations (DataAnnotationsModelValidator, ValidatableObjectAdapter)
dotnet build src\Mvc\Mvc.DataAnnotations\src\Microsoft.AspNetCore.Mvc.DataAnnotations.csproj --no-restore -v:q
```

### Build All Changed Projects at Once

```powershell
dotnet build src\Validation\src\Microsoft.Extensions.Validation.csproj --no-restore -v:q && `
dotnet build src\Components\Forms\src\Microsoft.AspNetCore.Components.Forms.csproj --no-restore -v:q && `
dotnet build src\Components\Web\src\Microsoft.AspNetCore.Components.Web.csproj --no-restore -v:q && `
dotnet build src\Mvc\Mvc.DataAnnotations\src\Microsoft.AspNetCore.Mvc.DataAnnotations.csproj --no-restore -v:q
```

> **Note:** Building `Mvc.DataAnnotations` transitively builds `Mvc.Core` and `Mvc.Abstractions`, so you don't need to build those separately.

### Verify All Pass

```powershell
. .\activate.ps1

@(
    "src\Validation\src\Microsoft.Extensions.Validation.csproj",
    "src\Components\Forms\src\Microsoft.AspNetCore.Components.Forms.csproj",
    "src\Components\Web\src\Microsoft.AspNetCore.Components.Web.csproj",
    "src\Mvc\Mvc.Abstractions\src\Microsoft.AspNetCore.Mvc.Abstractions.csproj",
    "src\Mvc\Mvc.Core\src\Microsoft.AspNetCore.Mvc.Core.csproj",
    "src\Mvc\Mvc.DataAnnotations\src\Microsoft.AspNetCore.Mvc.DataAnnotations.csproj"
) | ForEach-Object {
    $r = dotnet build $_ --no-restore --no-dependencies -v:q 2>&1
    $name = [System.IO.Path]::GetFileNameWithoutExtension($_)
    if ($LASTEXITCODE -eq 0) { Write-Host "[PASS] $name" }
    else { Write-Host "[FAIL] $name"; $r | Select-String "error" }
}
```
