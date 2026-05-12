// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BasicTestApp.FormsTest;
using Microsoft.AspNetCore.Components.E2ETest.Infrastructure;
using Microsoft.AspNetCore.Components.E2ETest.Infrastructure.ServerFixtures;
using Microsoft.AspNetCore.E2ETesting;
using OpenQA.Selenium;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.Components.E2ETest.Tests;

/// <summary>
/// E2E tests for async field-level and form-level validation.
/// Mirrors the sync validation coverage in <see cref="FormsTest"/> but for
/// <c>AsyncValidationAttribute</c> and <c>IAsyncValidatableObject</c>.
/// </summary>
public class AsyncFieldValidationTest : ServerTestBase<ToggleExecutionModeServerFixture<BasicTestApp.Program>>
{
    public AsyncFieldValidationTest(
        BrowserFixture browserFixture,
        ToggleExecutionModeServerFixture<BasicTestApp.Program> serverFixture,
        ITestOutputHelper output)
        : base(browserFixture, serverFixture, output)
    {
    }

    protected override void InitializeAsyncCore()
    {
        Navigate(ServerPathBase);
        Browser.MountTestComponent<AsyncFieldValidationComponent>();
    }

    private Func<string[]> CreateValidationMessagesAccessor(IWebElement container, string selector = ".validation-message")
        => () => container.FindElements(By.CssSelector(selector))
            .Select(x => x.Text)
            .OrderBy(x => x)
            .ToArray();

    // ──────────────────────────────────────────────────────────
    // 1. Field-level sync validation (baseline — same as FormsTest)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void SyncValidation_RequiredFieldShowsErrorOnSubmit()
    {
        // Submit with empty required fields → OnInvalidSubmit
        var submitButton = Browser.Exists(By.Id("submit-btn"));
        submitButton.Click();

        Browser.Equal("invalid", () => Browser.Exists(By.CssSelector(".submit-result")).Text);
    }

    [Fact]
    public void SyncValidation_FieldChangeShowsSyncErrors()
    {
        // Name field has [Required] — type and clear to trigger sync error on field change
        var nameInput = Browser.Exists(By.Id("name-input"));
        nameInput.SendKeys("x\t");

        // Clear it — should show required error
        nameInput.Clear();
        nameInput.SendKeys("\t");

        var nameMessages = CreateValidationMessagesAccessor(
            Browser.Exists(By.CssSelector(".name")));
        Browser.Equal(new[] { "Name is required" }, nameMessages);
    }

    // ──────────────────────────────────────────────────────────
    // 2. Field-level async validation (AsyncValidationAttribute)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AsyncField_ShowsPendingThenCompletes()
    {
        var emailInput = Browser.Exists(By.Id("email-input"));
        emailInput.SendKeys("user@test.com");

        // Pending indicators appear while async is in flight
        Browser.Exists(By.CssSelector(".email-pending"));
        Browser.Exists(By.CssSelector(".validation-pending"));

        // After the async validator completes, pending disappears
        await Task.Delay(2000);
        Browser.DoesNotExist(By.CssSelector(".email-pending"));
        Browser.DoesNotExist(By.CssSelector(".validation-pending"));
    }

    [Fact]
    public async Task AsyncField_TakenEmailShowsError()
    {
        var nameInput = Browser.Exists(By.Id("name-input"));
        nameInput.SendKeys("Test User\t");

        var emailInput = Browser.Exists(By.Id("email-input"));
        emailInput.SendKeys("taken@test.com");

        // Wait for async validation
        await Task.Delay(2000);

        var emailMessages = CreateValidationMessagesAccessor(
            Browser.Exists(By.CssSelector(".email")));
        Browser.Equal(new[] { "Email is not available" }, emailMessages);
    }

    [Fact]
    public async Task AsyncField_ValidEmailShowsNoError()
    {
        var nameInput = Browser.Exists(By.Id("name-input"));
        nameInput.SendKeys("Test User\t");

        var emailInput = Browser.Exists(By.Id("email-input"));
        emailInput.SendKeys("valid@test.com");

        await Task.Delay(2000);

        Browser.DoesNotExist(By.CssSelector(".email-pending"));

        var emailMessages = CreateValidationMessagesAccessor(
            Browser.Exists(By.CssSelector(".email")));
        Browser.Empty(emailMessages);
    }

    [Fact]
    public async Task AsyncField_CancelOnReEdit()
    {
        var emailInput = Browser.Exists(By.Id("email-input"));

        // Type "taken@test.com" then immediately retype "ok@test.com"
        // The first async task should be cancelled by the second
        emailInput.SendKeys("taken@test.com");
        Browser.Exists(By.CssSelector(".email-pending"));

        // Clear and type a valid email before the first completes
        emailInput.Clear();
        emailInput.SendKeys("ok@test.com");

        // Wait for the second async validation to complete
        await Task.Delay(2000);

        // Should show no error — the "taken" result was cancelled
        Browser.DoesNotExist(By.CssSelector(".email-pending"));
        var emailMessages = CreateValidationMessagesAccessor(
            Browser.Exists(By.CssSelector(".email")));
        Browser.Empty(emailMessages);
    }

    [Fact]
    public async Task AsyncField_InputCssClassUpdatesAfterAsyncCompletion()
    {
        var emailInput = Browser.Exists(By.Id("email-input"));

        // Initially valid (unmodified)
        Browser.Equal("valid", () => emailInput.GetDomAttribute("class"));

        // Type a valid email — becomes "modified valid" after async completes
        emailInput.SendKeys("ok@test.com");
        await Task.Delay(2000);
        Browser.Equal("modified valid", () => emailInput.GetDomAttribute("class"));

        // Type the taken email — becomes "modified invalid" after async completes
        emailInput.Clear();
        emailInput.SendKeys("taken@test.com");
        await Task.Delay(2000);
        Browser.Equal("modified invalid", () => emailInput.GetDomAttribute("class"));
    }

    // ──────────────────────────────────────────────────────────
    // 3. Form-level async validation (IAsyncValidatableObject)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FormLevel_CrossFieldAsyncValidationOnSubmit()
    {
        // "admin" with age < 21 triggers IAsyncValidatableObject error
        var nameInput = Browser.Exists(By.Id("name-input"));
        nameInput.SendKeys("admin\t");

        var emailInput = Browser.Exists(By.Id("email-input"));
        emailInput.SendKeys("ok@test.com");

        // Set age to 18 (under 21)
        var ageInput = Browser.Exists(By.Id("age-input"));
        ageInput.Clear();
        ageInput.SendKeys("18\t");

        // Wait for async field validation on email to complete
        await Task.Delay(2000);

        // Submit — IAsyncValidatableObject should reject
        var submitButton = Browser.Exists(By.Id("submit-btn"));
        submitButton.Click();

        Browser.Equal("invalid", () => Browser.Exists(By.CssSelector(".submit-result")).Text);

        // The cross-field error should appear in ValidationSummary
        var summaryMessages = CreateValidationMessagesAccessor(
            Browser.Exists(By.CssSelector(".all-errors")));
        Browser.True(() => summaryMessages().Contains("Admin users must be at least 21"));
    }

    [Fact]
    public async Task FormLevel_CrossFieldValidationPassesWhenValid()
    {
        // "admin" with age >= 21 should pass
        var nameInput = Browser.Exists(By.Id("name-input"));
        nameInput.SendKeys("admin\t");

        var emailInput = Browser.Exists(By.Id("email-input"));
        emailInput.SendKeys("ok@test.com");

        var ageInput = Browser.Exists(By.Id("age-input"));
        ageInput.Clear();
        ageInput.SendKeys("30\t");

        await Task.Delay(2000);

        var submitButton = Browser.Exists(By.Id("submit-btn"));
        submitButton.Click();

        Browser.Equal("valid", () => Browser.Exists(By.CssSelector(".submit-result")).Text);
    }

    // ──────────────────────────────────────────────────────────
    // 4. ValidationSummary with mixed sync + async errors
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidationSummary_ShowsBothSyncAndAsyncErrors()
    {
        // Leave name empty ([Required]), type taken email ([SlowValidation])
        var emailInput = Browser.Exists(By.Id("email-input"));
        emailInput.SendKeys("taken@test.com");

        await Task.Delay(2000);

        // Submit — should have both sync (Required) and async (taken) errors
        var submitButton = Browser.Exists(By.Id("submit-btn"));
        submitButton.Click();

        var summaryMessages = CreateValidationMessagesAccessor(
            Browser.Exists(By.CssSelector(".all-errors")));
        Browser.True(() => summaryMessages().Contains("Name is required"));
        Browser.True(() => summaryMessages().Contains("Email is not available"));
    }

    // ──────────────────────────────────────────────────────────
    // 5. Submit with async field validators (full round-trip)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_AllValidWithAsyncFieldAndFormLevel()
    {
        // Fill everything valid — non-admin name, valid email, valid age
        var nameInput = Browser.Exists(By.Id("name-input"));
        nameInput.SendKeys("Test User\t");

        var emailInput = Browser.Exists(By.Id("email-input"));
        emailInput.SendKeys("valid@test.com");

        await Task.Delay(2000);

        var submitButton = Browser.Exists(By.Id("submit-btn"));
        submitButton.Click();

        Browser.Equal("valid", () => Browser.Exists(By.CssSelector(".submit-result")).Text);
    }

    [Fact]
    public async Task Submit_TakenEmailCausesInvalidSubmit()
    {
        var nameInput = Browser.Exists(By.Id("name-input"));
        nameInput.SendKeys("Test User\t");

        var emailInput = Browser.Exists(By.Id("email-input"));
        emailInput.SendKeys("taken@test.com");

        await Task.Delay(2000);

        var submitButton = Browser.Exists(By.Id("submit-btn"));
        submitButton.Click();

        Browser.Equal("invalid", () => Browser.Exists(By.CssSelector(".submit-result")).Text);
    }
}
