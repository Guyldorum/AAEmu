using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// /pathdebug (alias /pd) - diagnostic pathfinding des NPCs sans modifier le gameplay.
///
/// Modes :
///   /pathdebug                       Dump ponctuel sur la cible
///   /pathdebug track start           Demarre suivi 10 Hz sur la cible
///   /pathdebug track stop            Arrete tous les suivis
///   /pathdebug track status          Liste les NPCs en suivi
///
/// Sortie : Data/Custom/pathdebug.csv (header auto) + recap court dans le chat.
/// </summary>
public class PathDebug : ICommand
{
    public string[] CommandNames { get; set; } = ["pathdebug", "pd"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "[track start|stop|status]";

    public string GetCommandHelpText() =>
        "Diag pathfinding des NPCs. Sans args : dump ponctuel sur la cible. " +
        "Sous-commande 'track' pour suivi continu 10 Hz. " +
        "CSV: Data/Custom/pathdebug.csv";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length > 0 && string.Equals(args[0], "track", StringComparison.OrdinalIgnoreCase))
        {
            HandleTrack(character, args, messageOutput);
            return;
        }

        if (character.CurrentTarget is not Npc npc)
        {
            CommandManager.SendErrorText(this, messageOutput,
                "Cible invalide : selectionne un NPC d'abord.");
            return;
        }

        var sample = PathDebugTracker.Instance.SampleNpc(npc, "dump");
        PathDebugTracker.Instance.WriteCsv(sample);
        SendChatSummary(messageOutput, sample);
    }

    private void HandleTrack(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2)
        {
            CommandManager.SendErrorText(this, messageOutput,
                "Usage : /pathdebug track <start|stop|status>");
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
                if (PathDebugTracker.Instance.StartTracking(npc))
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"|cFF80FF80Tracking start|r : {npc.Name} (objId={npc.ObjId}, tpl={npc.TemplateId}) @ 10 Hz");
                    CommandManager.SendNormalText(this, messageOutput,
                        "  Sortie : Data/Custom/pathdebug.csv (mode=track)");
                    CommandManager.SendNormalText(this, messageOutput,
                        "  Stoppe avec : /pathdebug track stop");
                }
                else
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"|cFFFFFF80Already tracking|r : {npc.Name} (objId={npc.ObjId})");
                }
                break;

            case "stop":
                var stopped = PathDebugTracker.Instance.StopAll();
                CommandManager.SendNormalText(this, messageOutput,
                    $"|cFFFF8080Tracking stop|r : {stopped} NPC(s) untracked");
                break;

            case "status":
                var list = PathDebugTracker.Instance.GetTrackedList();
                if (list.Count == 0)
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        "|cFFCCCCCCAucun NPC en cours de suivi.|r");
                }
                else
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"|cFF80FFFFTracking {list.Count} NPC(s) :|r");
                    foreach (var (objId, tpl, name) in list)
                        CommandManager.SendNormalText(this, messageOutput,
                            $"  - {name} (objId={objId}, tpl={tpl})");
                }
                break;

            default:
                CommandManager.SendErrorText(this, messageOutput,
                    $"Sous-commande inconnue : '{args[1]}'. Utilise : start | stop | status");
                break;
        }
    }

    private static string ColorDist(float v, float greenMax = 1.0f, float yellowMax = 5.0f)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return "|cFF808080N/A|r";
        if (v <= greenMax) return $"|cFF80FF80{v:F2}|r";
        if (v <= yellowMax) return $"|cFFFFFF80{v:F2}|r";
        return $"|cFFFF8080{v:F2}|r";
    }

    private void SendChatSummary(IMessageOutput messageOutput, PathDebugTracker.Sample s)
    {
        CommandManager.SendNormalText(this, messageOutput,
            $"[pathdebug] {s.Name} (objId={s.ObjId}, tpl={s.TemplateId})");
        CommandManager.SendNormalText(this, messageOutput,
            $"  pos    : |cFFFFFFFF{s.Pos.X:F1} {s.Pos.Y:F1} {s.Pos.Z:F2}|r" +
            $"  behavior=|cFFFFFFFF{s.Behavior}|r  combat=|cFFFFFFFF{(s.IsInCombat ? "Y" : "N")}|r");

        if (s.HasTarget)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"  target : |cFFFFFFFF{s.TargetName}|r (objId={s.TargetObjId}) " +
                $"@ {s.TargetPos.X:F1} {s.TargetPos.Y:F1} {s.TargetPos.Z:F2}");
        }
        else
        {
            CommandManager.SendNormalText(this, messageOutput, "  target : |cFF808080N/A|r");
        }

        if (!s.HasPathNode)
        {
            CommandManager.SendNormalText(this, messageOutput, "  path   : |cFF808080PathNode null|r");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"  path   : count=|cFFFFFFFF{s.FoundPathCount}|r " +
            $" dCurr={ColorDist(s.DCurr)} dEnd={ColorDist(s.DEnd, 2f, 20f)} " +
            $" dDrift={ColorDist(s.DDrift, 0.5f, 2f)} " +
            $" dTgtMoved={ColorDist(s.DTargetMoved, 0.5f, 2f)}");
        CommandManager.SendNormalText(this, messageOutput,
            $"  curr   : |cFFFFFFFF{s.CurrentTargetPos.X:F1} {s.CurrentTargetPos.Y:F1} {s.CurrentTargetPos.Z:F2}|r" +
            $"   end : |cFFFFFFFF{s.EndPointPos.X:F1} {s.EndPointPos.Y:F1} {s.EndPointPos.Z:F2}|r");
        if (s.HasPeekNext)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"  peek   : |cFFFFFFFF{s.PeekNext.X:F1} {s.PeekNext.Y:F1} {s.PeekNext.Z:F2}|r");
        }
    }
}
