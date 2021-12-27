﻿using GlazeWM.Infrastructure.Bussing;
using GlazeWM.Domain.Workspaces.Commands;
using GlazeWM.Domain.Containers;
using GlazeWM.Domain.Windows;
using GlazeWM.Domain.Monitors;
using GlazeWM.Domain.Containers.Commands;
using GlazeWM.Infrastructure.WindowsApi;

namespace GlazeWM.Domain.Workspaces.CommandHandlers
{
  class MoveFocusedWindowToWorkspaceHandler : ICommandHandler<MoveFocusedWindowToWorkspaceCommand>
  {
    private Bus _bus;
    private WorkspaceService _workspaceService;
    private MonitorService _monitorService;
    private ContainerService _containerService;

    public MoveFocusedWindowToWorkspaceHandler(
      Bus bus,
      WorkspaceService workspaceService,
      MonitorService monitorService,
      ContainerService containerService
    )
    {
      _bus = bus;
      _workspaceService = workspaceService;
      _monitorService = monitorService;
      _containerService = containerService;
    }

    public CommandResponse Handle(MoveFocusedWindowToWorkspaceCommand command)
    {
      var workspaceName = command.WorkspaceName;
      var focusedWindow = _containerService.FocusedContainer as Window;

      // Ignore cases where focused container is not a window or not in foreground.
      if (focusedWindow == null || !_containerService.IsFocusSynced)
        return CommandResponse.Ok;

      var currentWorkspace = _workspaceService.GetFocusedWorkspace();
      var targetWorkspace = _workspaceService.GetActiveWorkspaceByName(workspaceName)
        ?? ActivateWorkspace(workspaceName);

      // Since target workspace could be on a different monitor, adjustments might need to be made
      // because of DPI.
      if (_monitorService.HasDpiDifference(currentWorkspace, targetWorkspace))
        focusedWindow.HasPendingDpiAdjustment = true;

      if (focusedWindow is FloatingWindow)
        MoveFloatingWindowToWorkspace(focusedWindow as FloatingWindow, targetWorkspace);

      else
        MoveTilingWindowToWorkspace(focusedWindow as TilingWindow, targetWorkspace);

      // Reassign focus to descendant within the current workspace.
      _bus.Invoke(new FocusWorkspaceCommand(currentWorkspace.Name));

      _containerService.ContainersToRedraw.Add(currentWorkspace);
      _containerService.ContainersToRedraw.Add(targetWorkspace);
      _bus.Invoke(new RedrawContainersCommand());

      return CommandResponse.Ok;
    }

    private void MoveFloatingWindowToWorkspace(FloatingWindow focusedWindow, Workspace targetWorkspace)
    {
      var currentWorkspace = focusedWindow.Parent;
      var currentMonitor = currentWorkspace.Parent;
      var targetMonitor = targetWorkspace.Parent;

      // If floating window is moved to a different workspace on the same monitor, there is no need
      // to adjust its position.
      if (currentMonitor != targetMonitor)
      {
        // Get the window's X/Y coordinates relative to the current workspace's X/Y coordinates.
        var workspaceOffsetX = focusedWindow.X - currentWorkspace.X;
        var workspaceOffsetY = focusedWindow.Y - currentWorkspace.Y;

        // Get the window's X/Y coordinates relative to the target workspace's X/Y coordinates.
        var targetWorkspaceOffsetX = (workspaceOffsetX * targetWorkspace.Width)
          / currentWorkspace.Width;
        var targetWorkspaceOffsetY = (workspaceOffsetY * targetWorkspace.Height)
          / currentWorkspace.Height;

        // TODO: Need to scale width/height according to monitor DPI?
        focusedWindow.FloatingPlacement = focusedWindow.FloatingPlacement.TranslateToCoordinates(
          targetWorkspace.X + targetWorkspaceOffsetX,
          targetWorkspace.Y + targetWorkspaceOffsetY
        );
      }

      // Change the window's parent workspace.
      _bus.Invoke(new MoveContainerWithinTreeCommand(focusedWindow, targetWorkspace, false));
    }

    private void MoveTilingWindowToWorkspace(TilingWindow focusedWindow, Workspace targetWorkspace)
    {
      var insertionTarget = targetWorkspace.LastFocusedDescendantOfType(typeof(IResizable));

      // Insert the focused window into the target workspace.
      if (insertionTarget == null)
        _bus.Invoke(new MoveContainerWithinTreeCommand(focusedWindow, targetWorkspace, true));
      else
        _bus.Invoke(
          new MoveContainerWithinTreeCommand(
            focusedWindow,
            insertionTarget.Parent,
            insertionTarget.Index + 1,
            true
          )
        );
    }

    /// <summary>
    /// Activate a given workspace on the currently focused monitor.
    /// </summary>
    /// <param name="workspaceName">Name of the workspace to activate.</param>
    private Workspace ActivateWorkspace(string workspaceName)
    {
      var inactiveWorkspace = _workspaceService.GetInactiveWorkspaceByName(workspaceName);
      var focusedMonitor = _monitorService.GetFocusedMonitor();

      _bus.Invoke(new AttachWorkspaceToMonitorCommand(inactiveWorkspace, focusedMonitor));

      return inactiveWorkspace;
    }
  }
}
