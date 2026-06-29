using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.ViewModels.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptSettingsViewModelTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly CrptSettingsStore _store;
    private readonly FakeAuthService _authService;
    private readonly CrptAuthRuntimeState _runtimeState;

    public CrptSettingsViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _store = new CrptSettingsStore(_tempDirectory);
        _authService = new FakeAuthService();
        _runtimeState = new CrptAuthRuntimeState();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void DisplayTokenExpiresAt_WhenNull_ShowsNotReceivedMessage()
    {
        CrptSettingsViewModel.DisplayTokenExpiresAt(null).Should().Be("Токен не получен");
    }

    [Fact]
    public void DisplayTokenExpiresAt_WhenDefaultDate_ShowsNotReceivedMessage()
    {
        CrptSettingsViewModel.DisplayTokenExpiresAt(default(DateTimeOffset))
            .Should().Be("Токен не получен");
    }

    [Fact]
    public void DisplayTokenExpiresAt_WhenSet_ShowsLocalExpiry()
    {
        var expiresAt = new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
        CrptSettingsViewModel.DisplayTokenExpiresAt(expiresAt)
            .Should().Contain("2026");
    }

    [Fact]
    public void Load_ReflectsRuntimeTokenExpiry()
    {
        _runtimeState.TokenExpiresAt = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
        var viewModel = CreateViewModel();

        viewModel.Load();

        viewModel.TokenExpiresText.Should().Contain("2026");
    }

    [Fact]
    public async Task SaveAsync_PersistsSettingsAndSecrets()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();
        viewModel.Inn = "0000000000";
        viewModel.OmsId = "00000000-0000-0000-0000-000000000001";
        viewModel.ConnectionId = "00000000-0000-0000-0000-000000000002";
        viewModel.CertificateThumbprint = "ABC123";
        viewModel.AutoRefreshToken = false;

        await viewModel.SaveAsync();

        var merged = _store.LoadMerged();
        merged.Settings.Inn.Should().Be("0000000000");
        merged.Settings.AutoRefreshToken.Should().BeFalse();
        merged.Secrets.OmsId.Should().Be("00000000-0000-0000-0000-000000000001");
        merged.Secrets.ConnectionId.Should().Be("00000000-0000-0000-0000-000000000002");
        merged.Secrets.CertificateThumbprint.Should().Be("ABC123");
    }

    [Fact]
    public void Load_BuildsCategorySelectionsFromSettings()
    {
        _store.Save(
            new CrptSettings
            {
                NkKnownCategories = ["Synthetic Alpha", "Synthetic Beta"],
                NkVisibleCategories = ["Synthetic Beta"],
            },
            new CrptSecrets());

        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.CategorySelections.Select(category => category.CategoryName)
            .Should().Equal("Synthetic Alpha", "Synthetic Beta");
        viewModel.CategorySelections.Single(category => category.CategoryName == "Synthetic Alpha").IsSelected
            .Should().BeFalse();
        viewModel.CategorySelections.Single(category => category.CategoryName == "Synthetic Beta").IsSelected
            .Should().BeTrue();
    }

    [Fact]
    public void BuildVisibleCategories_WhenAllSelected_ReturnsEmptyList()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();
        viewModel.CategorySelections.Clear();
        viewModel.CategorySelections.Add(new CrptCategorySelectionViewModel("Synthetic Alpha", true));
        viewModel.CategorySelections.Add(new CrptCategorySelectionViewModel("Synthetic Beta", true));

        viewModel.BuildVisibleCategories().Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_PersistsVisibleCategoriesSubset()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();
        viewModel.CategorySelections.Clear();
        viewModel.CategorySelections.Add(new CrptCategorySelectionViewModel("Synthetic Alpha", true));
        viewModel.CategorySelections.Add(new CrptCategorySelectionViewModel("Synthetic Beta", false));

        await viewModel.SaveAsync();

        _store.LoadSettings().NkVisibleCategories.Should().Equal("Synthetic Alpha");
    }

    private CrptSettingsViewModel CreateViewModel() =>
        new(_store, _authService, new EmptyCertificateProvider(), _runtimeState);

    private sealed class FakeAuthService : ICrptAuthService
    {
        public DateTimeOffset? TokenExpiresAt { get; private set; } =
            new DateTimeOffset(2026, 6, 25, 8, 0, 0, TimeSpan.Zero);

        public Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default)
        {
            TokenExpiresAt = new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero);
            return Task.FromResult("synthetic-jwt");
        }

        public Task RefreshTokenAsync(CancellationToken cancellationToken = default)
        {
            TokenExpiresAt = new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyCertificateProvider : ICrptCertificateProvider
    {
        public System.Security.Cryptography.X509Certificates.X509Certificate2 FindCertificate(CrptConnectionSettings settings) =>
            throw new InvalidOperationException("No certificate in tests.");

        public IReadOnlyList<CrptCertificateDescriptor> ListEligibleCertificates(string? innFilter = null) =>
            Array.Empty<CrptCertificateDescriptor>();
    }
}
