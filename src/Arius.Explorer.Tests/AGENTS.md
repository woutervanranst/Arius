# UI Automation Test Playbook

## Framework and Tooling
- `Arius.Explorer.Tests` is an NUnit test project that drives the shipping `Arius.Explorer.exe` through the FlaUI UIA3 automation stack.
- Use Shouldly for expressing expectations against automation elements. Reserve NUnit assertions for fixture control flow only (e.g. `Assert.Ignore` in the base class when skipping on non-Windows hosts).
- Always build the Explorer application before invoking the tests so `Arius.Explorer.exe` is present under the test output directory. Execute the suite with `dotnet test src/Arius.Explorer.Tests/Arius.Explorer.Tests.csproj` from a Windows environment.

## Base Fixture Lifecycle
- All UI automation fixtures must inherit from `ExplorerUiTestBase`.
  - The base `OneTimeSetUp` launches `Arius.Explorer.exe` once per fixture, resolves the main window, and keeps a `UIA3Automation` instance alive for the fixture’s duration.
  - The base `SetUp` focuses the main window before each test and is invoked before any derived `[SetUp]` logic.
  - The base `TearDown` closes stray modal dialogs after each test, and the base `OneTimeTearDown` disposes automation resources and the Explorer process.
  - Reuse the provided helpers (`WaitForElement`, `WaitForCondition`, and `OpenChooseRepositoryDialog`) rather than duplicating polling or launch code.
- When adding per-test setup/teardown to a derived fixture, use additional `[SetUp]`/`[TearDown]` methods (as demonstrated in `ChooseRepositoryFunctionalTests`) and let the base hooks run automatically—do not override the base members.

## Fixture Conventions
- Decorate every UI automation fixture with:
  - `[TestFixture]`
  - `[Apartment(ApartmentState.STA)]`
  - `[NonParallelizable]`
  - `[Platform(Include = "Win")]`
- Capture automation elements into locals immediately after a `ShouldNotBeNull` check to avoid null-forgiving operators and to document the intended control type via `.AsButton()`, `.AsTextBox()`, etc.
- Prefer the base wait helpers instead of `Thread.Sleep`; assertions should occur only after the relevant condition has been observed.

## UI Contracts and Automation IDs
- The tests target controls via `AutomationProperties.AutomationId`. Maintain or introduce stable identifiers for any UI you exercise. Current IDs under test include:
  - Main window menu and status bar: `FileMenu`, `OpenMenuItem`, `SelectedItemsStatus`, `ArchiveStatisticsStatus`.
  - Repository tree: `RepositoryTree`.
  - Choose Repository dialog: `LocalPathTextBox`, `BrowseLocalPathButton`, `AccountNameTextBox`, `AccountKeyTextBox`, `ContainerComboBox`, `PassphrasePasswordBox`, and `OpenRepositoryButton`.
- When new functionality requires automation, assign meaningful `AutomationId` values to the relevant WPF elements and update tests to reference them through `FindFirstDescendant`.

## Data and Assertion Patterns
- UI assertions reflect the sample data bundled with the Explorer application:
  - The repository tree exposes a root "Sample Repository" folder with "Documents", "Images", and "Videos" children.
  - The Choose Repository dialog defaults to `C:\SampleRepository`, the `samplestorageaccount` name, empty account key, and offers the container list `{ container1, container2, backups, archives }`.
  - The simulated Browse command populates `C:\Users\Sample\Documents\MyRepository`.
- Keep these expectations in sync with the application’s seeded state; adjust tests whenever the defaults change.

## Extending the Suite
- Add new UI surface coverage by creating additional fixtures that inherit from `ExplorerUiTestBase` and follow the conventions above.
- Centralize any new cross-cutting helpers in the base class so future fixtures can reuse them.
- Ensure each test leaves the Explorer window in a clean state (close modals, deselect transient UI) so subsequent tests can rely on the base cleanup logic alone.
