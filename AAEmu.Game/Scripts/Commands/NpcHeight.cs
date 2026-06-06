using System;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// /npcheight (alias /nph) - diagnostic Z des NPCs sans modifier le gameplay.
///
/// Modes :
///   /npcheight                       Dump ponctuel sur la cible
///   /npcheight track start           Demarre suivi 10 Hz sur la cible
///   /npcheight track stop            Arrete tous les suivis
///   /npcheight track status          Liste les NPCs en suivi
///
/// Sortie : Data/Custom/npcheight.csv (header auto) + recap court dans le chat.
/// </summary>
public class NpcHeight : ICommand
{
    public string[] CommandNames { get; set; } = ["npcheight", "nph"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "[track start|stop|status]";

    public string GetCommandHelpText() =>
        "Diag Z des NPCs. Sans args : dump ponctuel sur la cible. " +
        "Sous-commande 'track' pour suivi continu 10 Hz. " +
        "CSV: Data/Custom/npcheight.csv";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        // Dispatch
        if (args.Length > 0 && string.Equals(args[0], "track", StringComparison.OrdinalIgnoreCase))
        {
            HandleTrack(character, args, messageOutput);
            return;
        }

        // Default : dump ponctuel sur la cible
        if (character.CurrentTarget is not Npc npc)
        {
            CommandManager.SendErrorText(this, messageOutput,
                "Cible invalide : selectionne un NPC d'abord.");
            return;
        }

        var sample = NpcHeightTracker.Instance.SampleNpc(npc, "dump");
        NpcHeightTracker.Instance.WriteCsv(sample);
        SendChatSummary(messageOutput, sample);
    }

    private void HandleTrack(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2)
        {
            CommandManager.SendErrorText(this, messageOutput,
                "Usage : /npcheight track <start|stop|status>");
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "start":
                if (character.CurrentTarget is not Npc npc)
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        "Cible invalide : selectionne un NPC d'abord.");
                    return;
                }
                if (NpcHeightTracker.Instance.StartTracking(npc))
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"|cFF80FF80Tracking start|r : {npc.Name} (objId={npc.ObjId}) @ 10 Hz");
                    CommandManager.SendNormalText(this, messageOutput,
                        "  Sortie : Data/Custom/npcheight.csv (mode=track)");
                    CommandManager.SendNormalText(this, messageOutput,
                        "  Stoppe avec : /npcheight track stop");
                }
                else
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"|cFFFFFF80Already tracking|r : {npc.Name} (objId={npc.ObjId})");
                }
                break;

            case "stop":
                var stopped = NpcHeightTracker.Instance.StopAll();
                CommandManager.SendNormalText(this, messageOutput,
                    $"|cFFFF8080Tracking stop|r : {stopped} NPC(s) untracked");
                break;

            case "status":
                var list = NpcHeightTracker.Instance.GetTrackedList();
                if (list.Count == 0)
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        "|cFFCCCCCCAucun NPC en cours de suivi.|r");
                }
                else
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"|cFF80FFFFTracking {list.Count} NPC(s) :|r");
                    foreach (var (objId, name) in list)
                        CommandManager.SendNormalText(this, messageOutput,
                            $"  - {name} (objId={objId})");
                }
                break;

            default:
                CommandManager.SendErrorText(this, messageOutput,
                    $"Sous-commande inconnue : '{args[1]}'. " +
                    "Utilise : start | stop | status");
                break;
        }
    }

    private static string ColorDelta(float v, float greenAbs = 0.5f, float yellowAbs = 2.0f)
    {
        var abs = Math.Abs(v);
        if (float.IsNaN(v)) return "|cFF808080N/A|r";
        if (abs <= greenAbs) return $"|cFF80FF80{v:+0.00;-0.00;0.00}|r";
        if (abs <= yellowAbs) return $"|cFFFFFF80{v:+0.00;-0.00;0.00}|r";
        return $"|cFFFF8080{v:+0.00;-0.00;0.00}|r";
    }

    private void SendChatSummary(IMessageOutput messageOutput, NpcHeightTracker.Sample s)
    {
        CommandManager.SendNormalText(this, messageOutput,
            $"[npcheight] {s.Name} (objId={s.ObjId}) @ X:{s.Pos.X:F1} Y:{s.Pos.Y:F1} " +
            $"Z:|cFFFFFFFF{s.Pos.Z:F2}|r");

        if (s.HasSpawnerZ)
        {
            var d = s.Pos.Z - s.SpawnerZ;
            CommandManager.SendNormalText(this, messageOutput,
                $"  spawnerZ (JSON): |cFFFFFFFF{s.SpawnerZ:F2}|r   drift: {ColorDelta(d)}");
        }
        else
        {
            CommandManager.SendNormalText(this, messageOutput, "  spawnerZ (JSON): |cFF808080N/A|r");
        }

        if (s.HasHmapZ)
        {
            var d = s.Pos.Z - s.HeightmapZ;
            CommandManager.SendNormalText(this, messageOutput,
                $"  heightmapZ    : |cFFFFFFFF{s.HeightmapZ:F2}|r   delta: {ColorDelta(d)}");
        }
        else
        {
            CommandManager.SendNormalText(this, messageOutput, "  heightmapZ    : |cFF808080N/A|r");
        }

        if (s.HasRaycastZ)
        {
            var d = s.Pos.Z - s.RaycastZ;
            var hitColor = s.RaycastHit switch
            {
                "brush" => "|cFF80FFFF",        // cyan = structure (recherché)
                "voxel" => "|cFFFFFFFF",        // blanc = terrain solide
                "heightmap" => "|cFFCCCCCC",    // gris = heightmap (par défaut)
                _ => "|cFFFF8080",              // rouge = autre/erreur
            };
            CommandManager.SendNormalText(this, messageOutput,
                $"  raycastZ      : |cFFFFFFFF{s.RaycastZ:F2}|r   hit: {hitColor}{s.RaycastHit}|r   delta: {ColorDelta(d)}");
        }
        else
        {
            CommandManager.SendNormalText(this, messageOutput, "  raycastZ      : |cFF808080N/A|r");
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"  state         : behavior=|cFFFFFFFF{s.Behavior}|r combat=|cFFFFFFFF{s.IsInCombat}|r" +
            (string.IsNullOrEmpty(s.AggroTarget) ? "" : $" target=|cFFFFFFFF{s.AggroTarget}|r"));
    }
}
