using System.Linq;
using Content.Server._NF.Bank;
using System.Numerics;
using Content.Server.Cargo.Systems;
//using Content.Server.Emp; // Frontier: Upstream - #28984
using Content.Server.Cargo.Components;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Emp;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Throwing;
using Content.Shared.UserInterface;
using Content.Shared.VendingMachines;
using Content.Shared.Wall;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Content.Server.Administration.Logs; // Frontier
using Content.Shared.Database; // Frontier
using Content.Shared._NF.Bank.BUI; // Frontier
using Content.Server._NF.Contraband.Systems; // Frontier
using Content.Shared.Stacks; // Frontier
using Content.Server.Stack;
// using Content.Server._Mono.VendingMachine; // Removed: namespace no longer exists in this branch
using Content.Shared._Mono.Traits.Physical;
using Robust.Shared.Containers; // Frontier
using Content.Shared._NF.Bank.Components; // Frontier
using Robust.Shared.Configuration; // HL: CVars
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;

namespace Content.Server.VendingMachines
{
    public sealed class VendingMachineSystem : SharedVendingMachineSystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly PricingSystem _pricing = default!;
        [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!; // HL: vending CVars
        [Dependency] private readonly AccessReaderSystem _accessReader = default!;

        [Dependency] private readonly SharedAudioSystem _audioSystem = default!; // Frontier
        [Dependency] private readonly BankSystem _bankSystem = default!; // Frontier
        [Dependency] private readonly PopupSystem _popupSystem = default!; // Frontier
        [Dependency] private readonly IAdminLogManager _adminLogger = default!; // Frontier
        [Dependency] private readonly ContrabandTurnInSystem _contraband = default!; // Frontier
        [Dependency] private readonly StackSystem _stack = default!; // Frontier

        private const float WallVendEjectDistanceFromWall = 1f;

        // HL: Lazy restock queue to amortize vending initialization cost on heavy maps/anchors
        private readonly Queue<(EntityUid Uid, VendingMachineComponent Comp, float Quality)> _pendingRestock = new();
        private TimeSpan _nextRestockTick;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<VendingMachineComponent, PowerChangedEvent>(OnPowerChanged);
            SubscribeLocalEvent<VendingMachineComponent, BreakageEventArgs>(OnBreak);
            SubscribeLocalEvent<VendingMachineComponent, DamageChangedEvent>(OnDamageChanged);
            SubscribeLocalEvent<VendingMachineComponent, PriceCalculationEvent>(OnVendingPrice);
            //SubscribeLocalEvent<VendingMachineComponent, EmpPulseEvent>(OnEmpPulse); // Frontier: Upstream - #28984

            SubscribeLocalEvent<VendingMachineComponent, ActivatableUIOpenAttemptEvent>(OnActivatableUIOpenAttempt);

            SubscribeLocalEvent<VendingMachineComponent, VendingMachineSelfDispenseEvent>(OnSelfDispense);

            SubscribeLocalEvent<VendingMachineComponent, RestockDoAfterEvent>(OnDoAfter);

            SubscribeLocalEvent<VendingMachineRestockComponent, PriceCalculationEvent>(OnPriceCalculation);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            // HL: Drain lazy restock queue in small batches to amortize load spikes
            if (_pendingRestock.Count > 0)
            {
                var now = _timing.CurTime;
                var interval = TimeSpan.FromMilliseconds(_cfg.GetCVar(Content.Shared.HL.CCVar.HLCCVars.VendingRestockTickMs));
                if (now >= _nextRestockTick)
                {
                    _nextRestockTick = now + interval;
                    var batch = Math.Max(1, _cfg.GetCVar(Content.Shared.HL.CCVar.HLCCVars.VendingRestockBatch));

                    for (var i = 0; i < batch && _pendingRestock.Count > 0; i++)
                    {
                        var (uid, comp, quality) = _pendingRestock.Dequeue();
                        if (Deleted(uid) || TerminatingOrDeleted(uid))
                            continue;
                        try
                        {
                            RestockInventoryFromPrototype(uid, comp, quality);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Vending restock failed for {ToPrettyString(uid)}: {ex}");
                        }
                    }
                }
            }

            // Frontier: finite random ejections
            var query = EntityQueryEnumerator<VendingMachineComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                // Added block for charges
                if (comp.EjectRandomCounter == comp.EjectRandomMax || _timing.CurTime < comp.EjectNextChargeTime)
                    continue;

                AddCharges(uid, 1, comp);
                comp.EjectNextChargeTime = _timing.CurTime + comp.EjectRechargeDuration;
                // Added block for charges
            }
            // End Frontier: finite random ejections

            var disabled = EntityQueryEnumerator<EmpDisabledComponent, VendingMachineComponent>();
            while (disabled.MoveNext(out var uid2, out _, out var comp2))
            {
                if (comp2.NextEmpEject < _timing.CurTime)
                {
                    EjectRandom(uid2, true, false, comp2);
                    comp2.NextEmpEject += (5 * comp2.EjectDelay);
                }
            }
        }

        private void OnVendingPrice(EntityUid uid, VendingMachineComponent component, ref PriceCalculationEvent args)
        {
            var price = 0.0;

            foreach (var entry in component.Inventory.Values)
            {
                if (!PrototypeManager.TryIndex<EntityPrototype>(entry.ID, out var proto))
                {
                    Log.Error($"Unable to find entity prototype {entry.ID} on {ToPrettyString(uid)} vending.");
                    continue;
                }

                price += entry.Amount; //* _pricing.GetEstimatedPrice(proto); Frontier - This is used to price the worth of a vending machine with the inventory it has.
            }

            //args.Price += price; Frontier - This is used to price the worth of a vending machine with the inventory it has.
        }

        protected override void OnMapInit(EntityUid uid, VendingMachineComponent component, MapInitEvent args)
        {
            // HL: Optionally defer restock to amortize cost across ticks
            if (_cfg.GetCVar(Content.Shared.HL.CCVar.HLCCVars.VendingLazyRestock))
            {
                // Create cash slot immediately to keep behavior, defer inventory population
                if (component.CashSlot != null && component.CashSlotName != null)
                    ItemSlots.AddItemSlot(uid, component.CashSlotName, component.CashSlot);

                // Enqueue for later restock using initial quality
                _pendingRestock.Enqueue((uid, component, component.InitialStockQuality));
            }
            else
            {
                // Original behavior: restock immediately via shared logic
                base.OnMapInit(uid, component, args);
            }

            if (HasComp<ApcPowerReceiverComponent>(uid))
            {
                TryUpdateVisualState((uid, component));
            }
        }

        private void OnActivatableUIOpenAttempt(EntityUid uid, VendingMachineComponent component, ActivatableUIOpenAttemptEvent args)
        {
            if (component.Broken)
                args.Cancel();
        }

        private void OnPowerChanged(EntityUid uid, VendingMachineComponent component, ref PowerChangedEvent args)
        {
            TryUpdateVisualState((uid, component));
        }

        private void OnBreak(EntityUid uid, VendingMachineComponent vendComponent, BreakageEventArgs eventArgs)
        {
            vendComponent.Broken = true;
            TryUpdateVisualState((uid, vendComponent));
        }

        private void OnDamageChanged(EntityUid uid, VendingMachineComponent component, DamageChangedEvent args)
        {
            if (!args.DamageIncreased && component.Broken)
            {
                component.Broken = false;
                TryUpdateVisualState((uid, component));
                return;
            }

            if (component.Broken || component.DispenseOnHitCoolingDown ||
                component.DispenseOnHitChance == null || args.DamageDelta == null)
                return;

            if (args.DamageIncreased && args.DamageDelta.GetTotal() >= component.DispenseOnHitThreshold &&
                _random.Prob(component.DispenseOnHitChance.Value))
            {
                if (component.DispenseOnHitCooldown != null)
                {
                    component.DispenseOnHitEnd = Timing.CurTime + component.DispenseOnHitCooldown.Value;
                }

                EjectRandom(uid, throwItem: true, forceEject: true, component);
            }
        }

        private void OnSelfDispense(EntityUid uid, VendingMachineComponent component, VendingMachineSelfDispenseEvent args)
        {
            if (args.Handled)
                return;

            args.Handled = true;
            EjectRandom(uid, throwItem: true, forceEject: false, component);
        }

        private void OnDoAfter(EntityUid uid, VendingMachineComponent component, DoAfterEvent args)
        {
            if (args.Handled || args.Cancelled || args.Args.Used == null)
                return;

            if (!TryComp<VendingMachineRestockComponent>(args.Args.Used, out var restockComponent))
            {
                Log.Error($"{ToPrettyString(args.Args.User)} tried to restock {ToPrettyString(uid)} with {ToPrettyString(args.Args.Used.Value)} which did not have a VendingMachineRestockComponent.");
                return;
            }

            TryRestockInventory(uid, component);

            Popup.PopupEntity(Loc.GetString("vending-machine-restock-done-self", ("target", uid)), args.Args.User, args.Args.User, PopupType.Medium);
            var othersFilter = Filter.PvsExcept(args.Args.User);
            Popup.PopupEntity(Loc.GetString("vending-machine-restock-done-others", ("user", Identity.Entity(args.User, EntityManager)), ("target", uid)), args.Args.User, othersFilter, true, PopupType.Medium);

            Audio.PlayPvs(restockComponent.SoundRestockDone, uid, AudioParams.Default.WithVolume(-2f).WithVariation(0.2f));

            Del(args.Args.Used.Value);

            args.Handled = true;
        }

        /// <summary>
        /// Sets the <see cref="VendingMachineComponent.CanShoot"/> property of the vending machine.
        /// </summary>
        public void SetShooting(EntityUid uid, bool canShoot, VendingMachineComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            component.CanShoot = canShoot;
        }

        /// <summary>
        /// Sets the <see cref="VendingMachineComponent.Contraband"/> property of the vending machine.
        /// </summary>
        public void SetContraband(EntityUid uid, bool contraband, VendingMachineComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            component.Contraband = contraband;
            Dirty(uid, component);
        }

        public void Deny(EntityUid uid, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            if (vendComponent.Denying)
                return;

            // Set DenyEnd to trigger denying state (Denying property is read-only)
            vendComponent.DenyEnd = _timing.CurTime + vendComponent.DenyDelay;
            Audio.PlayPvs(vendComponent.SoundDeny, uid, AudioParams.Default.WithVolume(-2f));
            TryUpdateVisualState(uid, vendComponent);
        }

        /// <summary>
        /// Checks if the user is authorized to use this vending machine
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="sender">Entity trying to use the vending machine</param>
        /// <param name="vendComponent"></param>
        public bool IsAuthorized(EntityUid uid, EntityUid sender, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return false;

            if (!TryComp<AccessReaderComponent>(uid, out var accessReader))
                return true;

            if (_accessReader.IsAllowed(sender, uid, accessReader))
                return true;

            Popup.PopupEntity(Loc.GetString("vending-machine-component-try-eject-access-denied"), uid);
            Deny(uid, vendComponent);
            return false;
        }

        /// <summary>
        /// Tries to eject the provided item. Will do nothing if the vending machine is incapable of ejecting, already ejecting
        /// or the item doesn't exist in its inventory.
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="type">The type of inventory the item is from</param>
        /// <param name="itemId">The prototype ID of the item</param>
        /// <param name="throwItem">Whether the item should be thrown in a random direction after ejection</param>
        /// <param name="vendComponent"></param>
        public bool TryEjectVendorItem(EntityUid uid, InventoryType type, string itemId, bool throwItem, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return false;

            if (vendComponent.Ejecting || vendComponent.Broken || !this.IsPowered(uid, EntityManager))
            {
                return false;
            }

            var entry = GetEntry(uid, itemId, type, vendComponent);

            if (entry == null)
            {
                Popup.PopupEntity(Loc.GetString("vending-machine-component-try-eject-invalid-item"), uid);
                Deny(uid, vendComponent);
                return false;
            }

            if (entry.Amount <= 0)
            {
                Popup.PopupEntity(Loc.GetString("vending-machine-component-try-eject-out-of-stock"), uid);
                Deny(uid, vendComponent);
                return false;
            }

            if (string.IsNullOrEmpty(entry.ID))
                return false;

            if (!TryComp<TransformComponent>(vendComponent.Owner, out var transformComp))
                return false;

            // Start ejecting: set end time rather than assigning read-only property.
            vendComponent.EjectEnd = Timing.CurTime + vendComponent.EjectDelay;
            vendComponent.NextItemToEject = entry.ID;
            vendComponent.ThrowNextItem = throwItem;

            // SpeakOnUIClosed handled in shared system; remove server duplicate dependency.

            // Frontier: unlimited vending
            // Infinite supplies must stay infinite.
            if (entry.Amount != uint.MaxValue)
                entry.Amount--;
            // End Frontier

            Dirty(uid, vendComponent);
            TryUpdateVisualState(uid, vendComponent);
            Audio.PlayPvs(vendComponent.SoundVend, uid);
            return true;
        }

        // Removed duplicate AuthorizedVend; keep the override implementation below.

        /// <summary>
        /// Tries to update the visuals of the component based on its current state.
        /// </summary>
        public void TryUpdateVisualState(EntityUid uid, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            var finalState = VendingMachineVisualState.Normal;
            if (vendComponent.Broken)
            {
                finalState = VendingMachineVisualState.Broken;
            }
            else if (vendComponent.Ejecting)
            {
                finalState = VendingMachineVisualState.Eject;
            }
            else if (vendComponent.Denying)
            {
                finalState = VendingMachineVisualState.Deny;
            }
            else if (!this.IsPowered(uid, EntityManager))
            {
                finalState = VendingMachineVisualState.Off;
            }

            // Light and appearance handled by shared system; server override retains visual state via base system dependencies.
            base.TryUpdateVisualState((uid, vendComponent));
        }

        /// <summary>
        /// Ejects a random item from the available stock. Will do nothing if the vending machine is empty.
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="throwItem">Whether to throw the item in a random direction after dispensing it.</param>
        /// <param name="forceEject">Whether to skip the regular ejection checks and immediately dispense the item without animation.</param>
        /// <param name="vendComponent"></param>
        public void EjectRandom(EntityUid uid, bool throwItem, bool forceEject = false, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            if (!this.IsPowered(uid, EntityManager))
                return;

            if (vendComponent.Ejecting)
                return;

            if (vendComponent.EjectRandomCounter <= 0)
            {
                _audioSystem.PlayPvs(_audioSystem.ResolveSound(vendComponent.SoundDeny), uid); // Frontier: ResolveSound, warning suppression
                _popupSystem.PopupEntity(Loc.GetString("vending-machine-component-try-eject-access-abused"), uid, PopupType.MediumCaution);
                return;
            }

            var availableItems = GetAvailableInventory(uid, vendComponent);
            if (availableItems.Count <= 0)
                return;
            var item = _random.Pick(availableItems);

            if (forceEject)
            {
                vendComponent.NextItemToEject = item.ID;
                vendComponent.ThrowNextItem = throwItem;
                var entry = GetEntry(uid, item.ID, item.Type, vendComponent);
                if (entry != null)
                    entry.Amount--;
                EjectItem(uid, vendComponent, forceEject);
            }
            else
            {
                TryEjectVendorItem(uid, item.Type, item.ID, throwItem, user: null, vendComponent: vendComponent);
            }
            vendComponent.EjectRandomCounter--; // Frontier: finite random ejections
        }

        // Frontier: finite random ejections
        public void AddCharges(EntityUid uid, int change, VendingMachineComponent? comp = null)
        {
            if (!Resolve(uid, ref comp, false))
                return;

            var old = comp.EjectRandomCounter;
            comp.EjectRandomCounter = Math.Clamp(comp.EjectRandomCounter + change, 0, comp.EjectRandomMax);
            if (comp.EjectRandomCounter != old)
                Dirty(uid, comp);
        }
        // End Frontier: finite random ejections

        protected override void EjectItem(EntityUid uid, VendingMachineComponent? vendComponent = null, bool forceEject = false)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            // No need to update the visual state because we never changed it during a forced eject
            if (!forceEject)
                TryUpdateVisualState((uid, vendComponent));

            if (string.IsNullOrEmpty(vendComponent.NextItemToEject))
            {
                vendComponent.ThrowNextItem = false;
                return;
            }

            // Default spawn coordinates
            var spawnCoordinates = Transform(uid).Coordinates;

            //Make sure the wallvends spawn outside of the wall.

            if (TryComp<WallMountComponent>(uid, out var wallMountComponent))
            {

                var offset = wallMountComponent.Direction.ToWorldVec() * WallVendEjectDistanceFromWall;
                spawnCoordinates = spawnCoordinates.Offset(offset);
            }

            var ent = Spawn(vendComponent.NextItemToEject, spawnCoordinates);

            _contraband.ClearContrabandValue(ent); // Frontier

            if (vendComponent.ThrowNextItem)
            {
                var range = vendComponent.NonLimitedEjectRange;
                var direction = new Vector2(_random.NextFloat(-range, range), _random.NextFloat(-range, range));
                _throwingSystem.TryThrow(ent, direction, vendComponent.NonLimitedEjectForce);
            }

            vendComponent.NextItemToEject = null;
            vendComponent.ThrowNextItem = false;
        }

        // (Removed duplicate Update override; merged into single method above)

        public void TryRestockInventory(EntityUid uid, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            RestockInventoryFromPrototype(uid, vendComponent);

            Dirty(uid, vendComponent);
            TryUpdateVisualState((uid, vendComponent));
        }

        private void OnPriceCalculation(EntityUid uid, VendingMachineRestockComponent component, ref PriceCalculationEvent args)
        {
            // Frontier: respect cargo blacklist
            args.Price = 0;
            return;
            /*
            List<double> priceSets = new();
            // Find the most expensive inventory and use that as the highest price.
            foreach (var vendingInventory in component.CanRestock)
            {
                double total = 0;
                if (PrototypeManager.TryIndex(vendingInventory, out VendingMachineInventoryPrototype? inventoryPrototype))
                {
                    foreach (var (item, amount) in inventoryPrototype.StartingInventory)
                    {
                        if (PrototypeManager.TryIndex(item, out EntityPrototype? entity))
                            total += _pricing.GetEstimatedPrice(entity) * amount;
                    }
                }
                priceSets.Add(total);
            }

            args.Price += priceSets.Max();
            */
            // End Frontier: respect cargo blacklist
        }

        //private void OnEmpPulse(EntityUid uid, VendingMachineComponent component, ref EmpPulseEvent args) // Frontier: Upstream - #28984
        //{
        //    if (!component.Broken && this.IsPowered(uid, EntityManager))
        //    {
        //        args.Affected = true;
        //        args.Disabled = true;
        //        component.NextEmpEject = _timing.CurTime;
        //    }
        //}

        // Frontier: custom vending check
        /// <summary>
        /// Checks whether the user is authorized to use the vending machine, then ejects the provided item if true
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="sender">Entity that is trying to use the vending machine</param>
        /// <param name="type">The type of inventory the item is from</param>
        /// <param name="itemId">The prototype ID of the item</param>
        /// <param name="component"></param>
        public override void AuthorizedVend(EntityUid uid, EntityUid sender, InventoryType type, string itemId, VendingMachineComponent component)
        {
            if (!PrototypeManager.TryIndex<EntityPrototype>(itemId, out var proto))
                return;

            var price = _pricing.GetEstimatedPrice(proto);
            // Somewhere deep in the code of pricing, a hardcoded 20 dollar value exists for anything without
            // a staticprice component for some god forsaken reason, and I cant find it or think of another way to
            // get an accurate price from a prototype with no staticprice comp.
            // this will undoubtably lead to vending machine exploits if I cant find wtf pricing system is doing.
            // also stacks, food, solutions, are handled poorly too f
            if (price == 0)
                price = 20;

            if (TryComp<MarketModifierComponent>(uid, out var modifier))
                price *= modifier.Mod;

            var totalPrice = (int)price;

            // If any price has a vendor price, explicitly use its value - higher OR lower, over others.
            var priceVend = _pricing.GetEstimatedVendPrice(proto);
            if (priceVend > 0.0) // if vending price exists, overwrite it.
                totalPrice = (int)priceVend;

            if (IsAuthorized(uid, sender, component))
            {
                int bankBalance = 0;
                if (TryComp<BankAccountComponent>(sender, out var bank))
                    bankBalance = bank.Balance;

                int cashSlotBalance = 0;
                Entity<StackComponent>? cashEntity = null;
                if (component.CashSlotName != null
                    && component.CurrencyStackType != null
                    && ItemSlots.TryGetSlot(uid, component.CashSlotName, out var cashSlot)
                    && TryComp<StackComponent>(cashSlot?.ContainerSlot?.ContainedEntity, out var stackComp)
                    && stackComp!.StackTypeId == component.CurrencyStackType)
                {
                    cashSlotBalance = stackComp!.Count;
                    cashEntity = (cashSlot!.ContainerSlot!.ContainedEntity.Value, stackComp!);
                }

                if (totalPrice > bankBalance + cashSlotBalance)
                {
                    Popup.PopupEntity(Loc.GetString("bank-insufficient-funds"), uid);
                    Deny((uid, component));
                    return;
                }

                bool paidFully = false;
                if (TryEjectVendorItem(uid, type, itemId, component.CanShoot, vendComponent: component))
                {
                    if (cashEntity != null)
                    {
                        var newCashSlotBalance = Math.Max(cashSlotBalance - totalPrice, 0);
                        _stack.SetCount(cashEntity.Value.Owner, newCashSlotBalance, cashEntity.Value.Comp);
                        component.CashSlotBalance = newCashSlotBalance;
                        paidFully = true; // Either we paid fully with cash, or we need to withdraw the remainder
                    }
                    if (totalPrice > cashSlotBalance)
                    {
                        paidFully = _bankSystem.TryBankWithdraw(sender, totalPrice - cashSlotBalance);
                    }

                    // If we paid completely, pay our station taxes
                    if (paidFully)
                    {
                        foreach (var (account, taxCoeff) in component.TaxAccounts)
                        {
                            if (!float.IsFinite(taxCoeff) || taxCoeff <= 0.0f)
                                continue;
                            var tax = (int)Math.Floor(totalPrice * taxCoeff);
                            _bankSystem.TrySectorDeposit(account, tax, LedgerEntryType.VendorTax);
                        }
                    }

                    // Something was ejected, update the vending component's state
                    Dirty(uid, component);

                    _adminLogger.Add(LogType.Action, LogImpact.Low,
                        $"{ToPrettyString(sender):user} bought from [vendingMachine:{ToPrettyString(uid)}, product:{proto.Name}, cost:{totalPrice},  with ${cashSlotBalance} in the cash slot and ${bankBalance} in the bank.");
                }
            }
        }
        // End Frontier: cash slot logic, custom vending check
    }
}
