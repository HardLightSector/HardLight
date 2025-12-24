using System.Numerics;
using Content.Client._Common.Consent;
using Content.Shared._Common.Consent;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed class HumanoidAppearanceSystem : SharedHumanoidAppearanceSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MarkingManager _markingManager = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly IClientConsentManager _consentManager = default!; // Hardlight
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private static readonly ProtoId<ConsentTogglePrototype> GenitalMarkingsConsent = "GenitalMarkings"; // Hardlight

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HumanoidAppearanceComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<HumanoidAppearanceComponent, AppearanceChangeEvent>(OnAppearanceChange);

        _consentManager.OnServerDataLoaded += OnConsentChanged;
    }

    private void OnHandleState(EntityUid uid, HumanoidAppearanceComponent component, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite(uid, component, Comp<SpriteComponent>(uid));
    }

    private void UpdateSprite(EntityUid uid, HumanoidAppearanceComponent component, SpriteComponent sprite)
    {
        UpdateLayers(uid, component, sprite);
        ApplyMarkingSet(uid, component, sprite);

        var eyeIndex = _sprite.LayerMapReserve((uid, sprite), HumanoidVisualLayers.Eyes);
        sprite[eyeIndex].Color = component.EyeColor;
        //starlight start
        if (_sprite.TryGetLayer((uid, sprite), eyeIndex, out var eyeLayer, true))
        {
            if (component.EyeGlowing)
                eyeLayer.ShaderPrototype = SpriteSystem.UnshadedId;
            else
                eyeLayer.ShaderPrototype = null;
        }
        //starlight end

        // Apply networked height/width to sprite scale on the client.
        // Clamp to a sane minimum to avoid issues with zero/near-zero scales from legacy data.
        var width = component.Width <= 0.005f ? 1.0f : component.Width;
        var height = component.Height <= 0.005f ? 1.0f : component.Height;
        _sprite.SetScale((uid, sprite), new Vector2(width, height));
    }

    private void OnAppearanceChange(EntityUid uid, HumanoidAppearanceComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        // If the server pushed a visuals value for scale, apply it directly.
        if (_appearance.TryGetData<Vector2>(uid, HumanoidVisuals.Scale, out var scale, args.Component))
        {
            _sprite.SetScale((uid, args.Sprite!), scale);
        }
    }

    private void OnConsentChanged()
    {
        var humanoidQuery = EntityManager.AllEntityQueryEnumerator<HumanoidAppearanceComponent, SpriteComponent>();
        while (humanoidQuery.MoveNext(out var owner, out var humanoid, out var sprite))
        {
            UpdateSprite(owner, humanoid, sprite);
        }
    }

    private static bool IsHidden(HumanoidAppearanceComponent humanoid, HumanoidVisualLayers layer)
        => humanoid.HiddenLayers.ContainsKey(layer) || humanoid.PermanentlyHidden.Contains(layer);

    private void UpdateLayers(EntityUid uid, HumanoidAppearanceComponent component, SpriteComponent sprite)
    {
        var oldLayers = new HashSet<HumanoidVisualLayers>(component.BaseLayers.Keys);
        component.BaseLayers.Clear();

        // add default species layers
        var speciesProto = _prototypeManager.Index(component.Species);
        var baseSprites = _prototypeManager.Index<HumanoidSpeciesBaseSpritesPrototype>(speciesProto.SpriteSet);
        foreach (var (key, id) in baseSprites.Sprites)
        {
            oldLayers.Remove(key);
            if (!component.CustomBaseLayers.ContainsKey(key))
                SetLayerData(uid, component, sprite, key, id, sexMorph: true);
        }

        // add custom layers
        foreach (var (key, info) in component.CustomBaseLayers)
        {
            oldLayers.Remove(key);
            // Shitmed Change: For whatever reason these weren't actually ignoring the skin color as advertised.
            SetLayerData(uid, component, sprite, key, info.Id, sexMorph: false, color: info.Color, overrideSkin: true);
        }

        // hide old layers
        // TODO maybe just remove them altogether?
        foreach (var key in oldLayers)
        {
            if (_sprite.LayerMapTryGet((uid, sprite), key, out var index, false))
                _sprite.LayerSetVisible((uid, sprite), index, false);
        }
    }

    private void SetLayerData(
        EntityUid uid,
        HumanoidAppearanceComponent component,
        SpriteComponent sprite,
        HumanoidVisualLayers key,
        string? protoId,
        bool sexMorph = false,
        Color? color = null,
        bool overrideSkin = false) // Shitmed Change
    {
        var layerIndex = _sprite.LayerMapReserve((uid, sprite), key);
        var layer = sprite[layerIndex];
        _sprite.LayerSetVisible((uid, sprite), layerIndex, !IsHidden(component, key));

        if (color != null)
            _sprite.LayerSetColor((uid, sprite), layerIndex, color.Value);

        if (protoId == null)
            return;

        if (sexMorph)
            protoId = HumanoidVisualLayersExtension.GetSexMorph(key, component.Sex, protoId);

        var proto = _prototypeManager.Index<HumanoidSpeciesSpriteLayer>(protoId);
        component.BaseLayers[key] = proto;

        if (proto.MatchSkin && !overrideSkin) // Shitmed Change
            layer.Color = component.SkinColor.WithAlpha(proto.LayerAlpha);

        if (proto.BaseSprite != null)
            _sprite.LayerSetSprite((uid, sprite), layerIndex, proto.BaseSprite);
    }

    /// <summary>
    ///     Loads a profile directly into a humanoid.
    /// </summary>
    /// <param name="uid">The humanoid entity's UID</param>
    /// <param name="profile">The profile to load.</param>
    /// <param name="humanoid">The humanoid entity's humanoid component.</param>
    /// <remarks>
    ///     This should not be used if the entity is owned by the server. The server will otherwise
    ///     override this with the appearance data it sends over.
    /// </remarks>
    public override void LoadProfile(EntityUid uid, HumanoidCharacterProfile? profile, HumanoidAppearanceComponent? humanoid = null)
    {
        if (profile == null)
            return;

        if (!Resolve(uid, ref humanoid))
        {
            return;
        }

        var customBaseLayers = new Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo>();

        var speciesPrototype = _prototypeManager.Index<SpeciesPrototype>(profile.Species);
        var markings = new MarkingSet(speciesPrototype.MarkingPoints, _markingManager, _prototypeManager);

        // Add markings that doesn't need coloring. We store them until we add all other markings that doesn't need it.
        var markingFColored = new Dictionary<Marking, MarkingPrototype>();
        foreach (var marking in profile.Appearance.Markings)
        {
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                if (!prototype.ForcedColoring)
                {
                    markings.AddBack(prototype.MarkingCategory, marking);
                }
                else
                {
                    markingFColored.Add(marking, prototype);
                }
            }
        }

        // legacy: remove in the future?
        //markings.RemoveCategory(MarkingCategories.Hair);
        //markings.RemoveCategory(MarkingCategories.FacialHair);

        // We need to ensure hair before applying it or coloring can try depend on markings that can be invalid
        var hairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.Hair, out var hairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(hairAlpha)
            : profile.Appearance.HairColor;
        var hair = new Marking(profile.Appearance.HairStyleId,
            new[] { hairColor }, profile.Appearance.HairGlowing); //starlight

        var facialHairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.FacialHair, out var facialHairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(facialHairAlpha)
            : profile.Appearance.FacialHairColor;
        var facialHair = new Marking(profile.Appearance.FacialHairStyleId,
            new[] { facialHairColor }, profile.Appearance.FacialHairGlowing); //starlight

        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, hair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.Hair, hair);
        }
        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, facialHair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.FacialHair, facialHair);
        }

        // Finally adding marking with forced colors
        foreach (var (marking, prototype) in markingFColored)
        {
            var markingColors = MarkingColoring.GetMarkingLayerColors(
                prototype,
                profile.Appearance.SkinColor,
                profile.Appearance.EyeColor,
                markings
            );
            markings.AddBack(prototype.MarkingCategory, new Marking(marking.MarkingId, markingColors, marking.IsGlowing)); //starlight, glowing
        }

        markings.EnsureSpecies(profile.Species, profile.Appearance.SkinColor, _markingManager, _prototypeManager);
        markings.EnsureSexes(profile.Sex, _markingManager);
        markings.EnsureDefault(
            profile.Appearance.SkinColor,
            profile.Appearance.EyeColor,
            _markingManager);

        DebugTools.Assert(IsClientSide(uid));

        humanoid.MarkingSet = markings;
        humanoid.PermanentlyHidden = new HashSet<HumanoidVisualLayers>();
        humanoid.HiddenLayers = new Dictionary<HumanoidVisualLayers, SlotFlags>();
        humanoid.CustomBaseLayers = customBaseLayers;
        humanoid.Sex = profile.Sex;
        humanoid.Gender = profile.Gender;
        humanoid.Age = profile.Age;
        humanoid.Species = profile.Species;
        humanoid.SkinColor = profile.Appearance.SkinColor; //starlight
        humanoid.EyeColor = profile.Appearance.EyeColor;
        humanoid.EyeGlowing = profile.Appearance.EyeGlowing;
        humanoid.Height = profile.Appearance.Height;
        humanoid.Width = profile.Appearance.Width;

        // Apply scaling for client-side preview (width, height)
        var sprite = Comp<SpriteComponent>(uid);
        // Check to prevent sprite scale errors for old profiles
        var width = profile.Appearance.Width <= 0.005f ? 1.0f : profile.Appearance.Width;
        var height = profile.Appearance.Height <= 0.005f ? 1.0f : profile.Appearance.Height;
        _sprite.SetScale((uid, sprite), new Vector2(width, height));

        UpdateSprite(uid, humanoid, sprite);
    }

    private void ApplyMarkingSet(EntityUid uid, HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        // I am lazy and I CBF resolving the previous mess, so I'm just going to nuke the markings.
        // Really, markings should probably be a separate component altogether.
        ClearAllMarkings(uid, humanoid, sprite);

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype))
                {
                    ApplyMarking(uid, markingPrototype, marking.MarkingColors, marking.IsGlowing, marking.Visible, humanoid, sprite); //starlight, glowing
                }
            }
        }

        humanoid.ClientOldMarkings = new MarkingSet(humanoid.MarkingSet);
    }

    private void ClearAllMarkings(EntityUid uid, HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        foreach (var markingList in humanoid.ClientOldMarkings.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(uid, marking, sprite);
            }
        }

        humanoid.ClientOldMarkings.Clear();

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(uid, marking, sprite);
            }
        }
    }

    private void RemoveMarking(EntityUid uid, Marking marking, SpriteComponent spriteComp)
    {
        if (!_markingManager.TryGetMarking(marking, out var prototype))
        {
            return;
        }

        foreach (var sprite in prototype.Sprites)
        {
            if (sprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerId = $"{marking.MarkingId}-{rsi.RsiState}";
            if (!_sprite.LayerMapTryGet((uid, spriteComp), layerId, out var index, false))
            {
                continue;
            }

            _sprite.LayerMapRemove((uid, spriteComp), layerId);
            _sprite.RemoveLayer((uid, spriteComp), index);
        }
    }

    private void AddUndergarments(EntityUid uid, HumanoidAppearanceComponent humanoid, SpriteComponent sprite, bool undergarmentTop, bool undergarmentBottom)
    {
        if (undergarmentTop && humanoid.UndergarmentTop != null)
        {
            var marking = new Marking(humanoid.UndergarmentTop, new List<Color> { new Color() }, false); //starlight, glowing
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                // Markings are added to ClientOldMarkings because otherwise it causes issues when toggling the feature on/off.
                humanoid.ClientOldMarkings.Markings.Add(MarkingCategories.UndergarmentTop, new List<Marking>{ marking });
                ApplyMarking(uid, prototype, null, false, true, humanoid, sprite); //starlight, glowing
            }
        }

        if (undergarmentBottom && humanoid.UndergarmentBottom != null)
        {
            var marking = new Marking(humanoid.UndergarmentBottom, new List<Color> { new Color() }, false); //starlight, glowing
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                humanoid.ClientOldMarkings.Markings.Add(MarkingCategories.UndergarmentBottom, new List<Marking>{ marking });
                ApplyMarking(uid, prototype, null, false, true, humanoid, sprite); //starlight, glowing
            }
        }
    }

    private void ApplyMarking(EntityUid uid, MarkingPrototype markingPrototype,
        IReadOnlyList<Color>? colors,
        bool isGlowing, //starlight
        bool visible,
        HumanoidAppearanceComponent humanoid,
        SpriteComponent sprite)
    {
            if (!_sprite.LayerMapTryGet((uid, sprite), markingPrototype.BodyPart, out int targetLayer, false))
        {
            return;
        }

        visible &= !IsHidden(humanoid, markingPrototype.BodyPart);
        visible &= humanoid.BaseLayers.TryGetValue(markingPrototype.BodyPart, out var setting)
           && setting.AllowsMarkings;

        visible &= !humanoid.HiddenMarkings.Contains(markingPrototype.ID); // FLOOF ADD
        // FLOOF ADD END

        // Hardlight: genital markings consent toggle
        if (!(_consentManager.GetConsentSettings().Toggles.TryGetValue(GenitalMarkingsConsent, out var val) && val == "on"))
        {
            visible &= markingPrototype.MarkingCategory != MarkingCategories.Genital;
        }

        for (var j = 0; j < markingPrototype.Sprites.Count; j++)
        {
            var markingSprite = markingPrototype.Sprites[j];

            if (markingSprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerId = $"{markingPrototype.ID}-{rsi.RsiState}";

            if (!_sprite.LayerMapTryGet((uid, sprite), layerId, out _ , false))
            {
                var layer = _sprite.AddLayer((uid, sprite), markingSprite, targetLayer + j + 1);
                _sprite.LayerMapSet((uid, sprite), layerId, layer);
                _sprite.LayerSetSprite((uid, sprite), layerId, rsi);
            }
            // impstation edit begin - check if there's a shader defined in the markingPrototype's shader datafield, and if there is...
            if (markingPrototype.Shader != null)
            {
                // set shader prototype directly on the layer
                if (_sprite.TryGetLayer((uid, sprite), layerId, out var layer, true))
                {
                    layer.ShaderPrototype = markingPrototype.Shader;
                }
            }
            // impstation edit end
            _sprite.LayerSetVisible((uid, sprite), layerId, visible);

            if (!visible || setting == null) // this is kinda implied
            {
                continue;
            }

            // Okay so if the marking prototype is modified but we load old marking data this may no longer be valid
            // and we need to check the index is correct.
            // So if that happens just default to white?
                if (colors != null && j < colors.Count)
                {
                    _sprite.LayerSetColor((uid, sprite), layerId, colors[j]);
                }
                else
                {
                    _sprite.LayerSetColor((uid, sprite), layerId, Color.White);
                }

            //starlight start
                if (isGlowing)
                {
                    if (_sprite.TryGetLayer((uid, sprite), layerId, out var layerGlow, true))
                    {
                        layerGlow.ShaderPrototype = SpriteSystem.UnshadedId;
                    }
                }
            //starlight end
        }
    }

    public override void SetSkinColor(EntityUid uid, Color skinColor, bool sync = true, bool verify = true, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid) || humanoid.SkinColor == skinColor)
            return;

        base.SetSkinColor(uid, skinColor, false, verify, humanoid);

        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        foreach (var (layer, spriteInfo) in humanoid.BaseLayers)
        {
            if (!spriteInfo.MatchSkin)
                continue;

            var index = _sprite.LayerMapReserve((uid, sprite), layer);
            _sprite.LayerSetColor((uid, sprite), index, skinColor.WithAlpha(spriteInfo.LayerAlpha));
        }
    }

    public override void SetLayerVisibility(
        Entity<HumanoidAppearanceComponent> ent,
        HumanoidVisualLayers layer,
        bool visible,
        SlotFlags? slot,
        ref bool dirty)
    {
        base.SetLayerVisibility(ent, layer, visible, slot, ref dirty);

        var sprite = Comp<SpriteComponent>(ent);
        if (!_sprite.LayerMapTryGet((ent.Owner, sprite), layer, out var index, false))
        {
            if (!visible)
                return;
            index = _sprite.LayerMapReserve((ent.Owner, sprite), layer);
        }

        if (_sprite.TryGetLayer((ent.Owner, sprite), index, out var spriteLayer, true))
        {
            if (spriteLayer.Visible == visible)
                return;

            _sprite.LayerSetVisible((ent.Owner, sprite), index, visible);
        }

        // I fucking hate this. I'll get around to refactoring sprite layers eventually I swear
        // Just a week away...

        foreach (var markingList in ent.Comp.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype) && markingPrototype.BodyPart == layer)
                    ApplyMarking(ent.Owner, markingPrototype, marking.MarkingColors, marking.IsGlowing, marking.Visible, ent.Comp, sprite); //starlight, glowing
            }
        }
    }
}
