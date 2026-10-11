using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private const string ServerAssets = "res://assets/characters/service-servers-v1/";
    private sealed class ServerView
    {
        public required Node3D Vendor, Root;
        public required AnimationPlayer Player;
        public required ServiceServerPresentation State;
        public required Dictionary<ServerPhase, string> Clips;
        public required Dictionary<ServerPhase, double> Lengths;
        public required Dictionary<ImmersionProduct, Node3D> Props;
        public string Clip = "";
        public string? SoftClip;
        public ImmersionProduct? Prop;
        public double Time;
    }
    private readonly Dictionary<string, ServerView> _serverViews = new(StringComparer.Ordinal);
    private readonly List<string> _staleServers = [];
    private readonly ServerPurchaseCursor _serverPurchases = new();
    private GameSession? _serverSession;
    private int _serverAttempt;

    private ServerView CreateServiceServer(string id, Node3D vendor, string? sexOverride = null)
    {
        var food = Stalls.IsVan(id);
        // Stable vendor identity, no simulation random draws or staff identities.
        var sex = sexOverride ?? (Stalls.Number(id) % 2 == (food ? 1 : 0) ? "female" : "male");
        var root = InstantiateAsset(ServerAssets + $"lwf_server_{(food ? "food" : "bar")}_{sex}_v1.glb");
        root.Name = "ServiceServer"; root.Position = food ? new(0, .47f, .88f) : new(.35f, 0, .30f);
        root.Rotation = new(0, Mathf.Pi, 0); vendor.AddChild(root);
        var player = root.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().Single();
        player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        var clips = new Dictionary<ServerPhase, string>(); var lengths = new Dictionary<ServerPhase, double>();
        foreach (var phase in Enum.GetValues<ServerPhase>())
        {
            var clip = "server_" + phase.ToString().ToLowerInvariant();
            var name = player.GetAnimationList().Single(n => n == clip || n.EndsWith("/" + clip, StringComparison.Ordinal));
            var animation = player.GetAnimation(name);
            animation.LoopMode = phase == ServerPhase.Idle ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
            clips.Add(phase, name); lengths.Add(phase, animation.Length);
        }
        var grip = (Node3D)root.FindChild(food ? "LWF_RightHand_Food" : "LWF_RightHand_Cup", true, false);
        var props = new Dictionary<ImmersionProduct, Node3D>();
        foreach (var product in food ? new[] { ImmersionProduct.Chips, ImmersionProduct.Pizza, ImmersionProduct.Curry }
            : new[] { ImmersionProduct.Beer, ImmersionProduct.SoftDrink, ImmersionProduct.Water })
        {
            // Water shares the existing soft cup. Every visual starts stowed.
            var prop = InstantiateAsset(HeldAsset(product)); grip.AddChild(prop); prop.Visible = false; props.Add(product, prop);
        }
        var view = new ServerView { Vendor = vendor, Root = root, Player = player, Props = props, Clips = clips,
            Lengths = lengths, State = new(_serverPurchases.Latest(id)?.Id),
            SoftClip = player.GetAnimationList().FirstOrDefault(n => n == "server_offer_soft" || n.EndsWith("/server_offer_soft", StringComparison.Ordinal)) };
        ApplyServiceServer(view, 0, true);
        return view;
    }

    private static void ApplyServiceServer(ServerView view, double delta, bool snap = false)
    {
        var clip = view.Clips[view.State.Phase];
        if (view.State.Phase == ServerPhase.Offer && view.State.Prop is ImmersionProduct.SoftDrink or ImmersionProduct.Water)
        {
            if (view.SoftClip is not null) clip = view.SoftClip;
        }
        var changed = clip != view.Clip;
        // The pickup and offer endpoints coincide. Do not blend a skipped low-hand pose
        // into a visible product: the authoritative clock may jump straight into offer.
        if (changed) { view.Player.Play(clip, snap || view.State.Phase == ServerPhase.Offer ? 0 : .14); view.Clip = clip; }
        var time = view.State.Seconds;
        // Manual processing advances blends only by visible, unpaused time. Seek catches skipped service ticks.
        view.Player.Seek(time, false); view.Player.Advance(0);
        if (!snap && delta > 0) view.Player.Advance(delta);
        view.Time = view.Player.CurrentAnimationPosition;
        if (view.Prop != view.State.Prop)
        {
            if (view.Prop is { } old && view.Props.TryGetValue(old, out var prior)) prior.Visible = false;
            if (view.State.Prop is { } product && view.Props.TryGetValue(product, out var next)) next.Visible = true;
            view.Prop = view.State.Prop;
        }
    }

    private bool ServersPaused => CharacterPresentationPaused || StartMenu.IsOpen || ResultsPaper.IsOpen ||
        _challengeReveal.IsOpen || _fieldGuide is not null;

    private void ProcessServiceServers(double delta)
    {
        var immersion = _session.CaptureImmersion();
        var attempt = _session.CapturePreparation()?.Attempt ?? 0;
        if (_serverSession != _session || _serverAttempt != attempt)
        {
            foreach (var view in _serverViews.Values)
                if (IsInstanceValid(view.Root)) { view.Root.Hide(); view.Root.QueueFree(); }
            _serverViews.Clear(); _serverSession = _session; _serverAttempt = attempt;
            _serverPurchases.Bind(immersion?.Purchases ?? []);
        }
        if (immersion is null) return;
        _serverPurchases.Observe(immersion.Purchases);
        _staleServers.Clear();
        foreach (var (id, view) in _serverViews)
            if (!_immersionVendors.TryGetValue(id, out var vendor) || vendor != view.Vendor || !IsInstanceValid(view.Root))
                _staleServers.Add(id);
        foreach (var id in _staleServers)
        {
            var view = _serverViews[id];
            if (IsInstanceValid(view.Root)) { view.Root.Hide(); view.Root.QueueFree(); }
            _serverViews.Remove(id);
            if (!_immersionVendors.ContainsKey(id)) _serverPurchases.Forget(id);
        }
        foreach (var vendor in _session.CaptureVendors())
        {
            if (!(Stalls.IsBar(vendor.Id) || Stalls.IsVan(vendor.Id)) || !_immersionVendors.TryGetValue(vendor.Id, out var body)) continue;
            // Unpitched van placeholders must not acquire a bar server.
            if (Stalls.IsVan(vendor.Id) && _session.TraderAt(vendor.Id) is null) continue;
            if (!_serverViews.TryGetValue(vendor.Id, out var view)) _serverViews[vendor.Id] = view = CreateServiceServer(vendor.Id, body);
            ImmersionPerson? person = null;
            if (vendor.OwnerId is { } owner)
                foreach (var candidate in immersion.People)
                    if (candidate.AgentId == owner) { person = candidate; break; }
            var order = person?.Order;
            var eligible = vendor.OwnerId is { } buyer && order is { } product && _session.ImmersionOrderCanComplete(buyer, product);
            var latest = _serverPurchases.Latest(vendor.Id);
            var frozen = ServersPaused || !view.Root.IsVisibleInTree();
            view.State.Advance(vendor.OwnerId, order, vendor.ServiceTicks - (frozen ? 0 : _host.Clock.InterpolationFraction),
                order is { } selected ? GameSession.ImmersionServiceDuration(selected) : 0, eligible,
                latest?.Id, latest?.Tick ?? -1, _session.CurrentTick, delta, frozen,
                view.Lengths[ServerPhase.Turn], view.Lengths[ServerPhase.Pickup], view.Lengths[ServerPhase.Offer],
                view.Lengths[ServerPhase.Retract], view.Lengths[ServerPhase.Reset], view.Lengths[ServerPhase.Idle]);
            if (!frozen) ApplyServiceServer(view, delta);
        }
    }
}
