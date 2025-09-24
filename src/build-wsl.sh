#!/bin/bash
# WSL to Windows .NET Build Wrapper
# This script allows building Windows Desktop applications from WSL using Windows .NET
#
# https://github.com/vtjballeng/WSL-to-Windows-.NET-Build-Wrapper
# Update https://chatgpt.com/c/68b84e17-fe3c-8324-917f-307438a84179

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Convert WSL path to Windows path format
PROJECT_DIR="$(wslpath -w "$SCRIPT_DIR")"

# Helper function to execute dotnet commands via Windows cmd.exe
execute_dotnet() {
    local description="$1"
    shift
    local command="$*"
    echo "$description"
    cmd.exe /c "cd /d $PROJECT_DIR && $command"
}

# Show usage information
show_usage() {
    echo "Usage: $0 {clean|restore|build|rebuild|verbose|quiet|test|release} [args]"
    echo ""
    echo "Examples:"
    echo "  $0 build     - Build the solution"
    echo "  $0 verbose   - Build with detailed output"
    echo "  $0 quiet     - Build with minimal output"
    echo "  $0 rebuild   - Clean and rebuild"
    echo "  $0 test      - Run all tests"
    echo "  $0 test Arius.sln --filter \"FullyQualifiedName~Namespace.Class.Method\""
    echo "  $0 release   - Build release version"
}

echo "Building using Windows .NET from WSL..."

COMMAND="$1"
shift || true  # shift so that $@ contains extra args after the main command

case "$COMMAND" in
    "clean")
        execute_dotnet "Cleaning solution..." "dotnet clean $*"
        ;;
    "restore")
        execute_dotnet "Restoring packages..." "dotnet restore $*"
        ;;
    "build")
        execute_dotnet "Building solution..." "dotnet build $*"
        ;;
    "rebuild")
        execute_dotnet "Cleaning and rebuilding solution..." "dotnet clean && dotnet build $*"
        ;;
    "verbose")
        execute_dotnet "Building with verbose output..." "dotnet build --verbosity normal $*"
        ;;
    "quiet")
        execute_dotnet "Building with minimal output..." "dotnet build --verbosity quiet $*"
        ;;
    "test")
        execute_dotnet "Running tests..." "dotnet test $*"
        ;;
    "release")
        execute_dotnet "Building release configuration..." "dotnet build --configuration Release $*"
        ;;
    "")
        show_usage
        ;;
    *)
        echo "Error: Unknown command '$COMMAND'"
        echo ""
        show_usage
        ;;
esac
