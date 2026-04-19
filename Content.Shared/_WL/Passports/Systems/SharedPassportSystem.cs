using System.Security.Cryptography;
using System.Text;
using Content.Shared._WL.Passports.Components;
using Content.Shared._WL.Records;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Preferences;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Roles;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WL.Passports.Systems;

public sealed class SharedPassportSystem : EntitySystem
{
    public const int CurrentYear = 3026;
    private const string NoConfederationId = "NoConfederation";
    const string PIDChars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
    private static readonly TimeSpan ToggleCooldown = TimeSpan.FromSeconds(0.5);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassportComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PassportComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, PassportComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange
            || component.IsClosed
            || component.OwnerProfile == null)
            return;

        var species = _prototypeManager.Index<SpeciesPrototype>(component.OwnerProfile.Species);

        args.PushMarkup(Loc.GetString("passport-registered-to", ("name", component.OwnerProfile.Name)), 50);
        args.PushMarkup(Loc.GetString("passport-species", ("species", Loc.GetString(species.Name))), 49);
        args.PushMarkup(Loc.GetString("passport-gender", ("gender", component.OwnerProfile.Gender.ToString())), 48);
        args.PushMarkup(Loc.GetString("passport-height", ("height", component.OwnerProfile.Height)), 47);
        args.PushMarkup(Loc.GetString("passport-year-of-birth", ("year", CurrentYear - component.OwnerProfile.Age)), 47);

        args.PushMarkup(
            Loc.GetString("passport-pid", ("pid", GenerateIdentityString(component.OwnerProfile.Name
            + component.OwnerProfile.Height
            + component.OwnerProfile.Age
            + component.OwnerProfile.Height
            + component.OwnerProfile.FlavorText))),
            46);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        var profile = ev.Profile;

        SpawnPassportForPlayer(ev.Mob, profile, ev.JobId);
    }

    public void SpawnPassportForPlayer(EntityUid mob, HumanoidCharacterProfile profile, string? jobId)
    {
        Logger.DebugS("passport", $"Attempting passport spawn for {profile.Name}, job: {jobId}, confederation: {profile.Confederation}");

        if (jobId == null || !_prototypeManager.TryIndex(
                jobId,
                out JobPrototype? jobPrototype)
            || Deleted(mob)
            || !Exists(mob))
        {
            Logger.WarningS("passport", $"No valid jobId for {profile.Name}");
            return;
        }

        var confederationId = string.IsNullOrEmpty(profile.Confederation)
            ? NoConfederationId
            : profile.Confederation;

        if (!_prototypeManager.TryIndex(confederationId, out ConfederationRecordsPrototype? confProto) ||
            !_prototypeManager.TryIndex(confProto.PassportPrototype, out EntityPrototype? entityPrototype))
        {
            if (!_prototypeManager.TryIndex<ConfederationRecordsPrototype>(NoConfederationId, out confProto) ||
                !_prototypeManager.TryIndex(confProto.PassportPrototype, out entityPrototype))
                return;
        }

        var passportEntity = _entityManager.SpawnEntity(entityPrototype.ID, _sharedTransformSystem.GetMapCoordinates(mob));
        var passportComponent = _entityManager.GetComponent<PassportComponent>(passportEntity);

        UpdatePassportProfile(new(passportEntity, passportComponent), profile);

        if (_inventory.TryGetSlotEntity(mob, "back", out var item) &&
                TryComp<StorageComponent>(item, out var inventory))

        {
            if (!TryComp<ItemComponent>(passportEntity, out var itemComp)
                || !_storage.CanInsert(item.Value, passportEntity, out _, inventory, itemComp)
                || !_storage.Insert(item.Value, passportEntity, out _, playSound: false))
            {
                _adminLogManager.Add(
                    LogType.EntitySpawn,
                    LogImpact.Low,
                    $"Passport for {profile.Name} was spawned on the floor due to missing bag space");
            }
        }
    }


    public void UpdatePassportProfile(Entity<PassportComponent> passport, HumanoidCharacterProfile profile)
    {
        passport.Comp.OwnerProfile = profile;
        var evt = new PassportProfileUpdatedEvent(profile);
        RaiseLocalEvent(passport, ref evt);
    }

    private void OnUseInHand(Entity<PassportComponent> passport, ref UseInHandEvent evt)
    {
        if (evt.Handled || !_timing.IsFirstTimePredicted)
            return;

        evt.Handled = true;

        if (_timing.CurTime < passport.Comp.ToggleCooldownEnd)
            return;

        passport.Comp.ToggleCooldownEnd = _timing.CurTime + ToggleCooldown;
        passport.Comp.IsClosed = !passport.Comp.IsClosed;

        var passportEvent = new PassportToggleEvent();
        RaiseLocalEvent(passport, ref passportEvent);

        Dirty(passport);
    }

    private static string GenerateIdentityString(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var result = new char[17];
        var hashIndex = 0;

        for (var i = 0; i < result.Length; i++)
        {
            if (i == 5 || i == 11)
            {
                result[i] = '-';
                continue;
            }
            result[i] = PIDChars[hash[hashIndex++] % PIDChars.Length];  
        }

        return new string(result);
    }

    [ByRefEvent]
    public sealed class PassportToggleEvent : HandledEntityEventArgs {}

    [ByRefEvent]
    public sealed class PassportProfileUpdatedEvent(HumanoidCharacterProfile profile) : HandledEntityEventArgs
    {
        public HumanoidCharacterProfile Profile { get; } = profile;
    }
}
