﻿using System;
using System.Collections.Generic;
using System.Text.Json;
using CommandLine;
using GlazeWM.Application.IpcServer.Messages;
using GlazeWM.Application.IpcServer.Server;
using GlazeWM.Domain.Containers;
using GlazeWM.Domain.Monitors;
using GlazeWM.Domain.UserConfigs;
using GlazeWM.Domain.Windows;
using GlazeWM.Domain.Workspaces;
using GlazeWM.Infrastructure.Bussing;
using GlazeWM.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace GlazeWM.Application.IpcServer
{
  public sealed class IpcMessageHandler
  {
    private readonly Bus _bus;
    private readonly CommandParsingService _commandParsingService;
    private readonly ContainerService _containerService;
    private readonly ILogger<IpcMessageHandler> _logger;
    private readonly MonitorService _monitorService;
    private readonly WorkspaceService _workspaceService;
    private readonly WindowService _windowService;

    private readonly JsonSerializerOptions _serializeOptions =
      JsonParser.OptionsFactory((options) =>
        options.Converters.Add(new JsonContainerConverter())
      );

    /// <summary>
    /// Dictionary of event names and session IDs subscribed to that event.
    /// </summary>
    internal Dictionary<string, List<Guid>> SubscribedSessions = new();

    internal IpcMessageHandler(
      Bus bus,
      CommandParsingService commandParsingService,
      ContainerService containerService,
      ILogger<IpcMessageHandler> logger,
      MonitorService monitorService,
      WorkspaceService workspaceService,
      WindowService windowService)
    {
      _bus = bus;
      _commandParsingService = commandParsingService;
      _containerService = containerService;
      _logger = logger;
      _monitorService = monitorService;
      _workspaceService = workspaceService;
      _windowService = windowService;
    }

    internal string GetResponseMessage(ClientMessage message)
    {
      var (sessionId, messageString) = message;

      _logger.LogDebug(
        "IPC message from session {Session}: {Message}.",
        sessionId,
        messageString
      );

      var messageParts = messageString.Split(" ");

      return Parser.Default.ParseArguments<
        InvokeCommandMessage,
        SubscribeMessage,
        GetMonitorsMessage,
        GetWorkspacesMessage,
        GetWindowsMessage
      >(messageParts).MapResult(
        (InvokeCommandMessage message) => HandleInvokeCommandMessage(message),
        (SubscribeMessage message) => HandleSubscribeMessage(message, sessionId),
        (GetMonitorsMessage message) => HandleGetMonitorsMessage(message),
        (GetWorkspacesMessage message) => HandleGetWorkspacesMessage(message),
        (GetWindowsMessage message) => HandleGetWindowsMessage(message),
        _ => throw new Exception()
      );
    }

    private string HandleInvokeCommandMessage(InvokeCommandMessage message)
    {
      var contextContainer = _containerService.GetContainerById(
        message.ContextContainerId
      );

      var command = _commandParsingService.ParseCommand(
        message.Command,
        contextContainer
      );

      var commandResponse = _bus.Invoke((dynamic)command);
      return ToResponseMessage(commandResponse);
    }

    private string HandleSubscribeMessage(SubscribeMessage message, Guid sessionId)
    {
      var eventNames = message.Events.Split(',');

      foreach (var eventName in eventNames)
      {
        if (SubscribedSessions.ContainsKey(eventName))
        {
          var sessionIds = new List<Guid>();
          SubscribedSessions.TryGetValue(eventName, out sessionIds);
          sessionIds.Add(sessionId);
          continue;
        }

        SubscribedSessions.Add(eventName, new List<Guid>() { sessionId });
      }

      return ToResponseMessage(CommandResponse.Ok);
    }

    private string HandleGetMonitorsMessage(GetMonitorsMessage _)
    {
      var monitors = _monitorService.GetMonitors();
      return ToResponseMessage(monitors);
    }

    private string HandleGetWorkspacesMessage(GetWorkspacesMessage _)
    {
      var workspaces = _workspaceService.GetActiveWorkspaces();
      return ToResponseMessage(workspaces);
    }

    private string HandleGetWindowsMessage(GetWindowsMessage _)
    {
      var windows = _windowService.GetWindows();
      return ToResponseMessage(windows);
    }

    internal string ToResponseMessage<T>(T payload)
    {
      var responseMessage = new ServerMessage<T>(
        ServerMessagePayloadType.ClientResponse,
        payload
      );

      return JsonParser.ToString((dynamic)responseMessage, _serializeOptions);
    }

    internal string ToEventMessage(Event @event)
    {
      var eventMessage = new ServerMessage<Event>(
        ServerMessagePayloadType.SubscribedEvent,
        @event
      );

      return JsonParser.ToString((dynamic)eventMessage, _serializeOptions);
    }
  }
}
