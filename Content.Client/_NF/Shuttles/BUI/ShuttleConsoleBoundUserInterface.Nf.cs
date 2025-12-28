// New Frontiers - This file is licensed under AGPLv3
// Copyright (c) 2024 New Frontiers Contributors
// See AGPLv3.txt for details.
using Content.Shared._NF.Shuttles.Events;
using Content.Shared.Shuttles.Components;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void NfOpen()
    {
        if (_window == null)
            return;

        _window.OnInertiaDampeningModeChanged += (entity, mode) =>
        {
            SendMessage(new SetInertiaDampeningRequest()
            {
                ShuttleEntityUid = entity,
                Mode = mode
            });
        };

        _window.OnServiceFlagsChanged += (entity, flags) =>
        {
            SendMessage(new SetServiceFlagsRequest()
            {
                ShuttleEntityUid = entity,
                ServiceFlags = flags
            });
        };
    }
}
