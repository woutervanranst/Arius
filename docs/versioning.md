# Versioning Strategy

This document explains how versioning works in the Arius project, from code implementation to build and release processes.

## Overview

Arius uses a semantic versioning approach with automatic patch version incrementing based on CI/CD run numbers. The versioning system is designed to:

- Provide meaningful version numbers for releases
- Differentiate between production releases and development builds
- Support both local development and automated CI/CD workflows
- Create prerelease versions for non-main branches

## Version Format

- **Production releases** (main branch): `5.0.{GITHUB_RUN_NUMBER}` (e.g., `5.0.123`)
- **Prerelease versions** (other branches): `5.0.{GITHUB_RUN_NUMBER}-prerelease` (e.g., `5.0.123-prerelease`)
- **Local development builds**: `5.0.0-local`

## Implementation Components

### 1. Project Configuration (`src/Arius.Cli/Arius.Cli.csproj`)

The version is configured using MSBuild conditional properties:
- Sets version to `5.0.$(GITHUB_RUN_NUMBER)` when running in GitHub Actions
- Falls back to `5.0.0-local` for local development builds
- Uses the `GITHUB_RUN_NUMBER` environment variable automatically provided by GitHub Actions

### 2. Runtime Version Retrieval (`src/Arius.Cli/Program.cs`)

The `GetVersion()` method in the CLI application:
- Reads the `AssemblyFileVersionAttribute` from the compiled assembly
- Provides fallbacks to assembly version and "unknown" if needed
- This version is displayed to users when running the CLI

## CI/CD Integration (`.github/workflows/ci.yml`)

### Build and Release Process

The CI/CD pipeline handles versioning through several steps:

1. **Version Determination**: Extracts version from `.csproj`, substitutes run number, adds `-prerelease` for non-main branches
2. **Build Process**: MSBuild automatically resolves `$(GITHUB_RUN_NUMBER)` during compilation
3. **Artifact Creation**: Creates versioned `.tar.gz` files and uploads to GitHub Actions
4. **Docker Images**: Tags images with the same version (e.g., `arius5:5.0.123`)
5. **GitHub Releases**: Creates releases with `v` prefix and prerelease flags as appropriate

## Version Flow Summary

1. **Local Development**: Version shows as `5.0.0-local`
2. **CI Build**: MSBuild resolves `$(GITHUB_RUN_NUMBER)` to actual run number (e.g., `5.0.123`)
3. **CI Processing**: Shell script adds `-prerelease` suffix for non-main branches
4. **Artifact Generation**: All outputs (CLI binary, Docker image, GitHub release) use the final version
5. **Runtime Display**: CLI reads version from assembly attributes and displays to users

## Maintenance

To update the major or minor version, update both conditional `<Version>` elements in `src/Arius.Cli/Arius.Cli.csproj`:

- Keep the `$(GITHUB_RUN_NUMBER)` placeholder for CI builds
- Keep the `-local` suffix for local development
- Example: Change `5.0.$(GITHUB_RUN_NUMBER)` to `6.0.$(GITHUB_RUN_NUMBER)` for version 6.0