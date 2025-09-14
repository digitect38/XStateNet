# TimelineWPF - XStateNet Timeline Visualization Component

A WPF library component for visualizing state machine transitions and timelines in XStateNet applications.

## Features

- Real-time and playback modes for timeline visualization
- Interactive zooming and panning
- State, event, and action visualization
- Step display mode for detailed state transitions
- Customizable playback speed

## Usage

### As a Library Component

```csharp
using TimelineWPF;

// Create and show the timeline window
var timelineWindow = new TimelineWindow();
timelineWindow.ShowTimeline();
```

### Integration with XStateNet

The TimelineWPF component is designed to work with XStateNet state machines. It can visualize:
- State transitions
- Events
- Actions
- Timing information

## Project Structure

- `TimelineComponent.xaml/cs` - Main UserControl for the timeline visualization
- `TimelineWindow.cs` - Window wrapper for standalone usage
- `Control/` - Custom WPF controls for timeline rendering
- `Models/` - Data models for timeline items and state machines
- `ViewModels/` - MVVM view models for data binding

## Dependencies

- .NET 8.0
- Windows Presentation Foundation (WPF)
- XStateNet

## Building

```bash
dotnet build TimelineWPF.csproj
```

## License

Part of the XStateNet project.