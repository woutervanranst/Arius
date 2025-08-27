using Arius.Core.Commands;
using FluentValidation.TestHelper;

namespace Arius.Core.Tests.Commands;

public class RestoreCommandValidatorTests : IClassFixture<Fixture>
{
    private readonly Fixture fixture;
    private readonly RestoreCommandValidator validator;

    public RestoreCommandValidatorTests(Fixture fixture)
    {
        this.fixture = fixture;
        this.validator = new RestoreCommandValidator();
    }

    [Fact]
    public void Validate_EmptyTargets_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateTestCommand();
        command = command with { Targets = [] };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Targets)
            .WithErrorMessage("At least one target path must be specified.");
    }

    [Fact]
    public void Validate_NonExistentPath_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateTestCommand();
        command = command with { Targets = ["/non/existent/path"] };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Targets)
            .WithErrorMessage("All specified paths must exist.");
    }

    [Fact]
    public void Validate_EmptyDirectory_ShouldBeValid()
    {
        // Arrange
        var emptyDir = fixture.TestRunSourceFolder.CreateSubdirectory("empty");
        var command = CreateTestCommand();
        command = command with { Targets = [emptyDir.FullName] };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NonEmptyDirectory_ShouldBeValid()
    {
        // Arrange
        var nonEmptyDir = fixture.TestRunSourceFolder.CreateSubdirectory("nonempty");
        File.WriteAllText(Path.Combine(nonEmptyDir.FullName, "test.txt"), "content");
        var command = CreateTestCommand();
        command = command with { Targets = [nonEmptyDir.FullName] };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_SingleFile_ShouldBeValid()
    {
        // Arrange
        var testFile = Path.Combine(fixture.TestRunSourceFolder.FullName, "single.txt");
        File.WriteAllText(testFile, "content");
        var command = CreateTestCommand();
        command = command with { Targets = [testFile] };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_MultipleFiles_ShouldBeValid()
    {
        // Arrange
        var file1 = Path.Combine(fixture.TestRunSourceFolder.FullName, "file1.txt");
        var file2 = Path.Combine(fixture.TestRunSourceFolder.FullName, "file2.txt");
        File.WriteAllText(file1, "content1");
        File.WriteAllText(file2, "content2");
        var command = CreateTestCommand();
        command = command with { Targets = [file1, file2] };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_MixedFilesAndDirectories_ShouldHaveValidationError()
    {
        // Arrange
        var testFile = Path.Combine(fixture.TestRunSourceFolder.FullName, "test.txt");
        File.WriteAllText(testFile, "content");
        var testDir = fixture.TestRunSourceFolder.CreateSubdirectory("testdir");
        var command = CreateTestCommand();
        command = command with { Targets = [testFile, testDir.FullName] };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Targets)
            .WithErrorMessage("Targets must be either: an empty directory, a non-empty directory, one file, or multiple files. Cannot mix files and directories.");
    }

    [Fact]
    public void Validate_MultipleDirectories_ShouldHaveValidationError()
    {
        // Arrange
        var dir1 = fixture.TestRunSourceFolder.CreateSubdirectory("dir1");
        var dir2 = fixture.TestRunSourceFolder.CreateSubdirectory("dir2");
        var command = CreateTestCommand();
        command = command with { Targets = [dir1.FullName, dir2.FullName] };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Targets)
            .WithErrorMessage("Targets must be either: an empty directory, a non-empty directory, one file, or multiple files. Cannot mix files and directories.");
    }

    private static RestoreCommand CreateTestCommand()
    {
        return new RestoreCommand
        {
            AccountName = "testaccount",
            AccountKey = "testkey",
            ContainerName = "testcontainer", 
            Passphrase = "testpass",
            Targets = ["dummy"],
            Synchronize = false,
            Download = false,
            KeepPointers = false
        };
    }
}