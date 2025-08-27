using Arius.Core.Commands;
using Arius.Core.Tests.Builders;
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
        var command = new RestoreCommandBuilder()
            .WithTargets()
            .Build();

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
        var command = new RestoreCommandBuilder()
            .WithTargets("./non/existent/path")
            .Build();

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
        var command = new RestoreCommandBuilder()
            .WithTargets(emptyDir.FullName)
            .Build();

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
        var command = new RestoreCommandBuilder()
            .WithTargets(nonEmptyDir.FullName)
            .Build();

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
        var command = new RestoreCommandBuilder()
            .WithTargets(testFile)
            .Build();

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
        var command = new RestoreCommandBuilder()
            .WithTargets(file1, file2)
            .Build();

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
        var command = new RestoreCommandBuilder()
            .WithTargets(testFile, testDir.FullName)
            .Build();

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
        var command = new RestoreCommandBuilder()
            .WithTargets(dir1.FullName, dir2.FullName)
            .Build();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Targets)
            .WithErrorMessage("Targets must be either: an empty directory, a non-empty directory, one file, or multiple files. Cannot mix files and directories.");
    }

}