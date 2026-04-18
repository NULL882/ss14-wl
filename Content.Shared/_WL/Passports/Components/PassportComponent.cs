using System;
using Content.Shared.Preferences;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.Passports.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PassportComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsClosed;

    [DataField, AutoNetworkedField]
    public TimeSpan ToggleCooldownEnd;

    [ViewVariables]
    public HumanoidCharacterProfile? OwnerProfile;
}
