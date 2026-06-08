using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class LosDebug : ICommand
{
    public string[] CommandNames { get; set; } = ["losdebug", "los"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "[track start|stop|status]";

    public string GetCommandHelpText() =>
        "Test la ligne de vue (LoS) entre toi et ta cible.\n" +
        "  /los              : dump ponctuel\n" +
        "  /los track start  : tracking 10Hz vers Data/Custom/losdebug.csv\n" +
        "  /los track stop   : arret du tracking\n" +
        "  /los track status : etat actuel";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length > 0 && string.Equals(args[0], "track", StringComparison.OrdinalIgnoreCase))
        {
            HandleTrack(character, args, messageOutput);
            return;
        }

        if (character.CurrentTarget is not BaseUnit target)
        {
            CommandManager.SendErrorText(this, messageOutput,
                "Cible invalide : selectionne quelqu'un d'abord.");
            return;
        }

        var sample = LosDebugTracker.Instance.SampleLos(character, target, "dump");
        LosDebugTracker.Instance.WriteCsv(sample);
        SendChatSummary(messageOutput, sample);
    }

    private void HandleTrack(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2)
        {
            CommandManager.SendErrorText(this, messageOutput,
                "Usage : /los track <start|stop|status>");
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "start":
                if (character.CurrentTarget is not BaseUnit target)
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        "Cible invalide : selectionne quelqu'un d'abord.");
                    return;
                }
                if (LosDebugTracker.Instance.StartTracking(character, target))
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"Tracking LoS demarre vers {target.Name} a 10Hz");
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        "Tracking deja actif (1 paire max).");
                }
                break;

            case "stop":
                LosDebugTracker.Instance.StopTracking();
                CommandManager.SendNormalText(this, messageOutput, "Tracking LoS arrete");
                break;

            case "status":
                CommandManager.SendNormalText(this, messageOutput,
                    LosDebugTracker.Instance.StatusSummary());
                break;

            default:
                CommandManager.SendErrorText(this, messageOutput,
                    "Usage : /los track <start|stop|status>");
                break;
        }
    }

    private void SendChatSummary(IMessageOutput messageOutput, LosDebugTracker.Sample s)
    {
        var color = s.Clear ? "|cFF00FF00" : "|cFFFF4444";
        var status = s.Clear ? "CLEAR" : "BLOCKED";
        var line1 = $"LoS {color}{status}|r ({s.HitType}) " +
                    $"dist3D: |cFFFFFFFF{s.Distance:F1}|rm";
        CommandManager.SendNormalText(this, messageOutput, line1);

        if (!s.Clear && s.DistanceToHit > 0f)
        {
            var line2 = $"  hitPoint : ({s.HitPoint.X:F1}, {s.HitPoint.Y:F1}, {s.HitPoint.Z:F1})  " +
                        $"distToHit : |cFFFFFFFF{s.DistanceToHit:F1}|rm";
            CommandManager.SendNormalText(this, messageOutput, line2);
        }

        var line3 = $"  caster ({s.CasterName}) : ({s.CasterPos.X:F1}, {s.CasterPos.Y:F1}, {s.CasterPos.Z:F1})";
        var line4 = $"  target ({s.TargetName}) : ({s.TargetPos.X:F1}, {s.TargetPos.Y:F1}, {s.TargetPos.Z:F1})";
        CommandManager.SendNormalText(this, messageOutput, line3);
        CommandManager.SendNormalText(this, messageOutput, line4);
    }
}
