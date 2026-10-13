using Festival.ContentAdapter;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

internal sealed partial class CrowdBodies
{
    private const string FightOwnedMeta = "FisticuffsOwned";
    private readonly Dictionary<string, AnimationLibrary> _fightLibraries = [];
    private readonly Dictionary<ulong, (AnimationPlayer Player, AnimationMixer.AnimationCallbackModeProcess Prior)> _fightPlayers = [];
    public static bool FightOwned(Node3D root) => root.HasMeta(FightOwnedMeta) && root.GetMeta(FightOwnedMeta).AsBool();
    private static string FightClip(FightPose pose) => pose switch
    {
        FightPose.Enter => "fight_enter", FightPose.Guard => "fight_guard", FightPose.SwingLeft => "fight_swing_l",
        FightPose.SwingRight => "fight_swing_r", FightPose.Flinch => "fight_flinch", _ => "fight_stand_down"
    };
    private AnimationPlayer? FightPlayer(Node3D root)
    {
        if (!root.GetMeta(RigMeta, false).AsBool() || RigBody(root)?.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().FirstOrDefault() is not { } player) return null;
        var sex = RigSex(root);
        var file = $"res://assets/characters/fisticuffs-v2/lwf_attendee_{sex}_fisticuffs_v2.tres";
        if (!ResourceLoader.Exists(file)) return null; // Pending/missing assets cannot claim a generic animation player.
        if (!_fightLibraries.TryGetValue(sex, out var library))
        {
            // Designer's native library restores constant leg/rest tracks stripped by glTF import.
            library = GD.Load<AnimationLibrary>(file);
            _fightLibraries.Add(sex, library);
        }
        if (!player.HasAnimationLibrary("fisticuffs")) player.AddAnimationLibrary("fisticuffs", library);
        return player;
    }
    public FightClipLengths? FisticuffsLengths(Node3D root)
    {
        var player = FightPlayer(root); if (player is null) return null;
        double Length(string name) => player.GetAnimation("fisticuffs/" + name).Length;
        return new(Length("fight_enter"), Length("fight_guard"), Math.Max(Length("fight_swing_l"), Length("fight_swing_r")), Length("fight_flinch"), Length("fight_stand_down"));
    }
    public bool ApplyFisticuffs(Node3D root, FightPoseSample sample, double delta)
        => ApplyFisticuffsClip(root, FightClip(sample.Pose), sample.Seconds, delta);
    public bool ApplyArgument(Node3D root, double seconds, double delta)
        => ApplyFisticuffsClip(root, "argue_gesture", seconds, delta);
    private bool ApplyFisticuffsClip(Node3D root, string clip, double seconds, double delta)
    {
        var player = FightPlayer(root); if (player is null) return false;
        var id = root.GetInstanceId();
        if (!_fightPlayers.TryGetValue(id, out var view) || view.Player != player)
        {
            _fightPlayers[id] = (player, player.CallbackModeProcess);
            player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            root.SetMeta(FightOwnedMeta, true);
        }
        player.SpeedScale = 1;
        var name = "fisticuffs/" + clip;
        if (player.AssignedAnimation != name)
        {
            var quickJab = clip.StartsWith("fight_swing", StringComparison.Ordinal) || clip == "fight_flinch" ||
                clip == "fight_guard" && player.AssignedAnimation.ToString().Contains("fight_swing", StringComparison.Ordinal);
            player.Play(name, quickJab ? .035 : .125);
        }
        player.Advance(Math.Max(0, delta));
        player.Seek(Math.Min(seconds, player.GetAnimation(name).Length), true);
        return true;
    }
    public void EndFisticuffs(Node3D root)
    {
        root.RemoveMeta(FightOwnedMeta);
        if (!_fightPlayers.Remove(root.GetInstanceId(), out var view) || !GodotObject.IsInstanceValid(view.Player)) return;
        view.Player.CallbackModeProcess = view.Prior;
        view.Player.SpeedScale = 0; // Generic activity reclaims this player next frame, including its current pause state.
        view.Player.Play("idle", .15);
    }
}
