# CLAUDE.md

This file contains additional guidance, on top of the CLAUDE.MD in the root of the repository, specifically for the Arius.Explorer project.

## Arius.Explorer WPF/MVVM Development Guidelines

### Project Structure
- Use **vertical slice architecture** - group files by feature/view rather than by type (no Views/, ViewModels/, Converters/ folders)
- Each feature should have its own folder containing View, ViewModel, Converters, and related files
- Example: `RepositoryExplorer/` contains Window.xaml, WindowViewModel.cs, ...

### MVVM Best Practices
- Use **CommunityToolkit.Mvvm** for ViewModels (ObservableObject, RelayCommand, etc.)
- ViewModels should use **dependency injection** and be registered in the DI container
- Views should be data-bound to ViewModels - avoid code-behind logic
- Use **ICommand** for user interactions, never event handlers in code-behind
- Converters for data transformation between View and ViewModel
- Use **INotifyPropertyChanged** for all bindable properties

### Data Binding Patterns
- Use **{Binding}** for ViewModel properties
- Use **FallbackValue** for design-time support
- Implement **INotifyPropertyChanged** for all mutable ViewModel properties
- Use **ObservableCollection<T>** for dynamic lists

### Command Pattern
- Use **RelayCommand** from CommunityToolkit.Mvvm
- Commands should be async when calling Core services via Mediator
- Commands should handle loading states and error handling
- Use **CanExecute** to control command availability