using System;
using Content.Shared.Preferences;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.Passports.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class PassportComponent : Component
{
    public bool IsClosed;

    public TimeSpan ToggleCooldownEnd;

    [ViewVariables]
    public HumanoidCharacterProfile OwnerProfile;
}
