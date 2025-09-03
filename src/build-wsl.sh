#!/bin/bash
# WSL to Windows .NET Build Wrapper
# This script allows building Windows Desktop applications from WSL using Windows .NET

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Convert WSL path to Windows path format
PROJECT_DIR="$(wslpath -w "$SCRIPT_DIR")"

# Helper function to execute dotnet commands via Windows cmd.exe
execute_dotnet() {
    local description="$1"
    local command="$2"
    echo "$description"
    cmd.exe /c "cd /d $PROJECT_DIR && $command"
}

# Show usage information
show_usage() {
    echo "Usage: $0 {clean|restore|build|rebuild|verbose|quiet|test|release}"
    echo ""
    echo "Examples:"
    echo "  $0 build     - Build the solution"
    echo "  $0 verbose   - Build with detailed output"
    echo "  $0 quiet     - Build with minimal output"
    echo "  $0 rebuild   - Clean and rebuild"
    echo "  $0 test      - Run tests"
    echo "  $0 release   - Build release version"
}

echo "Building using Windows .NET from WSL..."

case "$1" in
    "clean")
        execute_dotnet "Cleaning solution..." "dotnet clean"
        ;;
    "restore")
        execute_dotnet "Restoring packages..." "dotnet restore"
        ;;
    "build")
        execute_dotnet "Building solution..." "dotnet build"
        ;;
    "rebuild")
        execute_dotnet "Cleaning and rebuilding solution..." "dotnet clean && dotnet build"
        ;;
    "verbose")
        execute_dotnet "Building with verbose output..." "dotnet build --verbosity normal"
        ;;
    "quiet")
        execute_dotnet "Building with minimal output..." "dotnet build --verbosity quiet"
        ;;
    "test")
        execute_dotnet "Running tests..." "dotnet test"
        ;;
    "release")
        execute_dotnet "Building release configuration..." "dotnet build --configuration Release"
        ;;
    "")
        show_usage
        ;;
    *)
        echo "Error: Unknown command '$1'"
        echo ""
        show_usage
        ;;
esac
