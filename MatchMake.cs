using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using static CounterStrikeSharp.API.Core.Listeners;

namespace MatchMake;
public enum MatchState
{
    NotStarted,
    Warmup,
    AllPlayersReady,
    KnifeRound,
    WaitingChoice,
    MainMatch,
    Timeout,
    Finished
}
public class MatchMake : BasePlugin, IPluginConfig<MatchMakeConfig>
{
    public override string ModuleName => "MatchMake";
    public override string ModuleAuthor => "Phoenix";
    public override string ModuleVersion => "1.0";
    private bool allPlayerJoin = false;
    private bool LeaderCanTakePause = false;
    private bool IsTimeOut = false;
    private bool IsStay = true;
    private Dictionary<CCSPlayerController, int> SaveHealth = new();
    private HashSet<CCSPlayerController> AllPlayersGame = new();
    private HashSet<CCSPlayerController> PlayerReadyList = new();
    private HashSet<CCSPlayerController> TeamLeaders = new();
    private HashSet<CCSPlayerController> FirstTeam = new();
    private HashSet<CCSPlayerController> SecondTeam = new();
    private Dictionary<ulong, CsTeam> PlayerLastTeamAfterDisconnect = new();
    private Dictionary<CCSPlayerController, Dictionary<CCSPlayerController, StatsDamage>> EpicDamage = new();
    private List<CounterStrikeSharp.API.Modules.Timers.Timer> TimersPlayers = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? endMessage;
    private CounterStrikeSharp.API.Modules.Timers.Timer? TimeOutsMessage;
    private CounterStrikeSharp.API.Modules.Timers.Timer? messageCountPlayerNeed;
    private int knifeWinnerTeam;    
    private int requiredPlayer = 0;
    private static int countTimeOut = 2;
    private int timeTimeOuts = 30;
    private List<int> CountTimeOuts = [countTimeOut, countTimeOut];
    private CCSPlayerController? WhoTakePause;
    public MatchState currentState = MatchState.NotStarted;
    public MatchMakeConfig Config {get; set;} = new();
    public void OnConfigParsed(MatchMakeConfig cfg)
    {
        Config = cfg;
    }
    public override void Load(bool hotReload)
    {
        Config.Reload();
        requiredPlayer = Config.NeedPlayer;
        countTimeOut = Config.CountTimersAll;
        timeTimeOuts = Config.TimeTimersSecond;
        RegisterEventHandler<EventPlayerTeam>(CheckFullTeam);
        RegisterEventHandler<EventRoundStart>(KnifeRoundStart);
        RegisterEventHandler<EventRoundEnd>(KnifeRoundEnd);
        RegisterEventHandler<EventPlayerConnectFull>(ChangeTeamConnectedPlayer);
        RegisterEventHandler<EventPlayerDisconnect>(PlayerDisconnectReadLastTeam);
        RegisterEventHandler<EventWarmupEnd>(LeaderNotChooie);
        RegisterEventHandler<EventRoundStart>(GiveTimerUsers);
        RegisterEventHandler<EventRoundStart>(MessageAboutDamageFirstStep);
        RegisterEventHandler<EventPlayerHurt>(MessageAboutDamageSecondStep);
        RegisterEventHandler<EventRoundEnd>(MessageAboutDamageThirdStep);
        RegisterEventHandler<EventPlayerHurt>(TeamDamage);
        RegisterEventHandler<EventRoundStart>(SaveHp);
        RegisterEventHandler<EventPlayerDeath>(InfinityMoneyAndClearWeapons);
        RegisterEventHandler<EventRoundStart>(WaitingTimeForStartTimer);
        RegisterEventHandler<EventPlayerTeam>(CheckEmptyServer);
        RegisterEventHandler<EventCsWinPanelMatch>(FinalFeatures);
        RegisterEventHandler<EventPlayerDisconnect>(AllPlayersLeave);
        RegisterEventHandler<EventPlayerConnectFull>(DeleteAllTimer);
        RegisterEventHandler<EventPlayerDisconnect>(AutoReplacePlayer);
        RegisterListener<OnMapStart>(ApplyMatchSettings);
        AddCommand("css_ready", "Подтверждает готовность игрока", ReadyPlayer);
        AddCommand("css_unready", "Убирает готовность игроков", UnReadyPlayer);
        AddCommand("css_r", "Подтверждает готовность игрока", ReadyPlayer);
        AddCommand("css_к", "Подтверждает готовность игрока", ReadyPlayer);
        AddCommand("css_ur", "Убирает готовность игроков", UnReadyPlayer);
        AddCommand("css_гк", "Убирает готовность игроков", UnReadyPlayer);
        AddCommand("css_switch", "Лидер выбирает поменятся сторонами", ChooiseSwitch);
        AddCommand("css_stay", "Лидер выбирает остаться за свою сторону", ChooiseStay);
        AddCommand("css_pause", "Лидер ставит матч на паузу", LeaderSetPause);
        AddCommand("css_unpause", "Лидер убирает паузу с матча насильно", LeaderSetUnPause);
        AddCommand("css_changename", "Лидер меняет название своей команды", LeaderSetNameTeam);
        AddCommandListener("jointeam", LockSwitchTeam);
        AddCommandListener("mp_warmup_end", LeaderNotChooised);
    }
    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_mm_reload", "Обновляет конфигурацию")]
    public void ConfigUpdate(CCSPlayerController player, CommandInfo info)
    {
        Config.Reload();
        requiredPlayer = Config.NeedPlayer;
        countTimeOut = Config.CountTimersAll;
        timeTimeOuts = Config.TimeTimersSecond;
    }
    private HookResult AutoReplacePlayer (EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (!Config.AutoReplacePlayers || currentState == MatchState.NotStarted || currentState == MatchState.Warmup || currentState == MatchState.Finished) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid || player.Team == CsTeam.None || player.Team == CsTeam.Spectator || player.IsBot) return HookResult.Continue;
        CsTeam OldTeam = player.Team;
        bool findNeedPlayer = false;
        CCSPlayerController? replacePlayer = null;
        
        foreach (var players in Utilities.GetPlayers())
        {
            if (players.Team == CsTeam.Spectator && !findNeedPlayer)
            {
                replacePlayer = players;
                findNeedPlayer = true;
            }
        }

        if (!findNeedPlayer || replacePlayer == null || !replacePlayer.IsValid) return HookResult.Continue;

        if (AllPlayersGame.Contains(player))
        {
            if (FirstTeam.Contains(player))
            {
                FirstTeam.Remove(player);
                FirstTeam.Add(replacePlayer);
            }
            else
            {
                SecondTeam.Remove(player);
                SecondTeam.Add(replacePlayer);
            }
            AllPlayersGame.Remove(player);
            AllPlayersGame.Add(replacePlayer);
        }

        replacePlayer.ChangeTeam(OldTeam);

        return HookResult.Continue;
    }
    private HookResult DeleteAllTimer (EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (currentState == MatchState.Timeout) return HookResult.Continue;

        TimeOutsMessage?.Kill();

        return HookResult.Continue;
    }
    private void ApplyMatchSettings (string map)
    {
        Server.NextFrame(() =>
        {
            currentState = MatchState.NotStarted;
        });
    }
    private HookResult FinalFeatures (EventCsWinPanelMatch @event, GameEventInfo info)
    {
        currentState = MatchState.Finished;
        
        AddTimer(15.0f, () =>
        {
            string currentMap = Server.MapName;
            Server.ExecuteCommand($"changelevel {currentMap}");
        });

        return HookResult.Continue;
    }
    private HookResult AllPlayersLeave (EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid == null || @event.Userid.IsBot) return HookResult.Continue;
        Server.NextFrame(() =>
        {
            int countTruePlayer = 0;
            foreach (var player in Utilities.GetPlayers())
            {
                if (player != null && player.IsValid && !player.IsBot && player.Team != CsTeam.Spectator && player.Team != CsTeam.None) countTruePlayer++;
            }
            if (countTruePlayer == 0)
            {
                currentState = MatchState.NotStarted;
            }
        });
        return HookResult.Continue;
    }
    private HookResult CheckEmptyServer (EventPlayerTeam @event, GameEventInfo info)
    {
        if (currentState != MatchState.NotStarted || !Config.EnableAutoStart) return HookResult.Continue;
        int test = @event.Team;
        if (@event.Userid == null || @event.Userid.IsBot) return HookResult.Continue;
        var player = @event.Userid;
        Server.NextFrame(() =>
        {
            int countAllPlayers = 0;
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IsBot || player.Team == CsTeam.Spectator || player.Team == CsTeam.None) continue;
                countAllPlayers++;
            }
            if (countAllPlayers >= 2) return;
            if (test == 2)
            {
                ApplyMatchSetting();
                player.ChangeTeam(CsTeam.Terrorist);
            }
            else if (test == 3)
            {
                ApplyMatchSetting();
                player.ChangeTeam(CsTeam.CounterTerrorist);
            }
        });

        return HookResult.Continue;
    }
    private HookResult WaitingTimeForStartTimer (EventRoundStart @event, GameEventInfo info)
    {
        if (currentState != MatchState.MainMatch && currentState != MatchState.KnifeRound) return HookResult.Continue;
        LeaderCanTakePause = false;
        
        var FreezeTime = ConVar.Find("mp_freezetime");
        if (FreezeTime == null) return HookResult.Continue;

        int TimeFreezeTime = FreezeTime.GetPrimitiveValue<int>();
        
        AddTimer(TimeFreezeTime + 1.0f, () => 
        {
            LeaderCanTakePause = true;
        });

        return HookResult.Continue;
    }
    private HookResult InfinityMoneyAndClearWeapons (EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || currentState != MatchState.Warmup) return HookResult.Continue;

        if (player.InGameMoneyServices == null) return HookResult.Continue;
        player.InGameMoneyServices.Account = 16000;
        
        Server.NextFrame(() =>
        {
            CleanAllWeaponInWarmup();
        });

        return HookResult.Continue;
    }
    private HookResult SaveHp (EventRoundStart @event, GameEventInfo info)
    {
        if (Config.TypeFriendlyFire != "Faceit") return HookResult.Continue;

        Server.NextFrame(() =>
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.Team == CsTeam.None || player.Team == CsTeam.Spectator) continue;
                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;
                SaveHealth[player] = pawn.Health;
            }
        });
        return HookResult.Continue;
    }
    private HookResult TeamDamage (EventPlayerHurt @event, GameEventInfo info)
    {
        if (Config.TypeFriendlyFire != "Faceit") return HookResult.Continue;

        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (attacker == null || victim == null || !attacker.IsValid || !victim.IsValid) return HookResult.Continue;

        var teamAttacker = attacker.Team;
        var teamVictim = victim.Team;
        var pawn = victim.PlayerPawn.Value;

        if (pawn == null || !pawn.IsValid) return HookResult.Continue;
        if (attacker == victim) return HookResult.Continue;
        if (teamAttacker == teamVictim)
        {
            if (@event.Weapon == "inferno" || @event.Weapon == "hegrenade")
            {
                SaveHealth[victim] = pawn.Health;
                return HookResult.Continue;
            } 
            pawn.Health = SaveHealth[victim];
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            SaveHealth[victim] = pawn.Health;
        }

        return HookResult.Continue;
    }
    private HookResult MessageAboutDamageFirstStep (EventRoundStart @event, GameEventInfo info)
    {
        if (!Config.EnableMessageOrNot || (currentState != MatchState.KnifeRound && currentState != MatchState.MainMatch && currentState != MatchState.Timeout)) return HookResult.Continue;

        EpicDamage.Clear();

        foreach (var player in Utilities.GetPlayers())
        {
            if (player.Team != CsTeam.CounterTerrorist) continue;
            EpicDamage[player] = new Dictionary<CCSPlayerController, StatsDamage>();
            foreach (var players in Utilities.GetPlayers())
            {
                if (players.Team != CsTeam.Terrorist) continue;
                EpicDamage[player][players] = new StatsDamage
                {
                    OpponentName = players.PlayerName,
                    DamageDealt = 0,
                    HitsDealt = 0,
                    DamageReceived = 0,
                    HitsReceived = 0,
                    CurrentHp = players.Health
                };
            }
        }
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.Team != CsTeam.Terrorist) continue;
            EpicDamage[player] = new Dictionary<CCSPlayerController, StatsDamage>();
            foreach (var players in Utilities.GetPlayers())
            {
                if (players.Team != CsTeam.CounterTerrorist) continue;
                EpicDamage[player][players] = new StatsDamage
                {
                    OpponentName = players.PlayerName,
                    DamageDealt = 0,
                    HitsDealt = 0,
                    DamageReceived = 0,
                    HitsReceived = 0
                };
            }
        }

        return HookResult.Continue;
    }
    private HookResult MessageAboutDamageSecondStep (EventPlayerHurt @event, GameEventInfo info)
    {
        if (!Config.EnableMessageOrNot || (currentState != MatchState.KnifeRound && currentState != MatchState.MainMatch && currentState != MatchState.Timeout)) return HookResult.Continue;
        
        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (attacker == null || victim == null || !attacker.IsValid || !victim.IsValid) return HookResult.Continue;
        if (attacker == victim) return HookResult.Continue;
        if (attacker.Team == victim.Team) return HookResult.Continue;

        EpicDamage[attacker][victim].CurrentHp -= @event.DmgHealth;
        EpicDamage[attacker][victim].DamageDealt += @event.DmgHealth;
        EpicDamage[attacker][victim].HitsDealt++;
        EpicDamage[victim][attacker].DamageReceived += @event.DmgHealth;
        EpicDamage[victim][attacker].HitsReceived++;

        return HookResult.Continue;
    }
    private HookResult MessageAboutDamageThirdStep (EventRoundEnd @event, GameEventInfo info)
    {
        if (!Config.EnableMessageOrNot || (currentState != MatchState.KnifeRound && currentState != MatchState.MainMatch && currentState != MatchState.Timeout)) return HookResult.Continue;

        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid) continue;
            foreach (var players in Utilities.GetPlayers())
            {
                if (player == players) continue;

                int attackerDamage = EpicDamage[player][players].DamageDealt;
                int attackerHits = EpicDamage[player][players].HitsDealt;
                int victimDamage = EpicDamage[player][players].DamageReceived;
                int victimHits = EpicDamage[player][players].HitsReceived;
                int victimHealth = EpicDamage[player][players].CurrentHp;
                string playerName = EpicDamage[player][players].OpponentName;
                if (players != null && players.IsValid && players.PlayerPawn.Value != null)
                {
                    if (!players.PawnIsAlive || players.PlayerPawn.Value.Health <= 0)
                    {
                        victimHealth = 0;
                    }
                    else
                    {
                        victimHealth = players.PlayerPawn.Value.Health;
                    }
                }
                player.PrintToChat(Localizer["Prefix"] + Localizer["DamageMessage", attackerDamage, attackerHits, victimDamage, victimHits, playerName, victimHealth]);
            }
        }

        return HookResult.Continue;
    }
    public void LeaderSetNameTeam (CCSPlayerController? player, CommandInfo info)
    {
        if (currentState == MatchState.NotStarted) return;

        if (player == null || !player.IsValid) return;

        if (!TeamLeaders.Contains(player))
        {
            player.PrintToCenter(Localizer["DontHavePermission"]);
            return;
        }

        if (info.ArgCount != 2)
        {
            player.PrintToChat(Localizer["Prefix"] + Localizer["NotCorrectName"]);
            return;
        }

        string nameCommand = info.GetArg(1);

        if (SecondTeam.Contains(player))
        {
            Server.ExecuteCommand($"mp_teamname_2 {nameCommand}");
        }
        else if (FirstTeam.Contains(player))
        {
            Server.ExecuteCommand($"mp_teamname_1 {nameCommand}");
        }
    }
    private HookResult GiveTimerUsers (EventRoundStart @event, GameEventInfo info)
    {
        if (currentState != MatchState.Timeout || IsTimeOut) return HookResult.Continue;

        if (WhoTakePause == null || !WhoTakePause.IsValid) return HookResult.Continue;
        WhoTakePause.PrintToChat(Localizer["Prefix"] + Localizer["MessageAboutUnPause"]);

        int time = timeTimeOuts;
        IsTimeOut = true;
        
        TimeOutsMessage = AddTimer(1.0f, () =>
        {
            if (time <= 0)
            {
                TimeOutsMessage?.Kill();
                TimeOutsMessage = null;
                SetUnPause();
                WhoTakePause = null;
                IsTimeOut = false;
            }
            int minutes = time / 60;
            int second = time % 60;
            foreach (var player in Utilities.GetPlayers())
            {
                player.PrintToCenterAlert($"{Localizer["TimerText"]} {minutes:D2}:{second:D2}");
            }
            time--;
        },
        CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT
        );

        return HookResult.Continue;
    }
    public void LeaderSetPause (CCSPlayerController? player, CommandInfo info)
    {
        if (currentState != MatchState.MainMatch || player == null || !player.IsValid || !TeamLeaders.Contains(player)) return;

        if (!LeaderCanTakePause)
        {
            player.PrintToChat(Localizer["Prefix"] + Localizer["CantTakePauseNow"]);
            return;
        }

        if (WhoTakePause != null)
        {
            player.PrintToCenter(Localizer["OtherLeaderTakePause"]);
        }

        if (FirstTeam.Contains(player))
        {
            if (CountTimeOuts[0] > 0)
            {
                CountTimeOuts[0]--;
                SetPause();
                WhoTakePause = player;
            }
        }
        else if (SecondTeam.Contains(player))
        {
            if (CountTimeOuts[1] > 0)
            {
                CountTimeOuts[1]--;
                SetPause();
                WhoTakePause = player;
            }
        }
    } 
    public void LeaderSetUnPause (CCSPlayerController? player, CommandInfo info)
    {
        if (currentState != MatchState.Timeout || player == null || !player.IsValid || !TeamLeaders.Contains(player)) return;

        if (WhoTakePause == player)
        {
            TimeOutsMessage?.Kill();
            SetUnPause();
            WhoTakePause = null;
        }
    }
    private HookResult LeaderNotChooie (EventWarmupEnd @event, GameEventInfo info)
    {
        if (currentState != MatchState.WaitingChoice) return HookResult.Continue;

        SwitchOrStay();

        return HookResult.Continue;
    }
    private HookResult LeaderNotChooised (CCSPlayerController? player, CommandInfo command)
    {
        if (currentState != MatchState.WaitingChoice) return HookResult.Continue;

        SwitchOrStay();
        
        return HookResult.Continue;
    }
    private HookResult PlayerDisconnectReadLastTeam (EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (currentState == MatchState.NotStarted || currentState == MatchState.Warmup) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        if (FirstTeam.Contains(player) || SecondTeam.Contains(player))
        {
            PlayerLastTeamAfterDisconnect.Add(player.SteamID, player.Team);
        }

        return HookResult.Continue;
    }
    private HookResult ChangeTeamConnectedPlayer (EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (currentState == MatchState.NotStarted || currentState == MatchState.Warmup) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        if (FirstTeam.Contains(player) || SecondTeam.Contains(player))
        {
            if (FirstTeam.Contains(player) && FirstTeam.Count >= 2)
            {
                PlayerMoveTeamAfterDisconnect(player, FirstTeam);
            }
            else if (SecondTeam.Contains(player) && SecondTeam.Count >= 2)
            {
                PlayerMoveTeamAfterDisconnect(player, SecondTeam);
            }
            else
            {
                if (PlayerLastTeamAfterDisconnect.ContainsKey(player.SteamID))
                {
                    Server.ExecuteCommand("bot_kick");
                    CsTeam LastTeam = PlayerLastTeamAfterDisconnect[player.SteamID];
                    player.ChangeTeam(LastTeam);

                    PlayerLastTeamAfterDisconnect.Remove(player.SteamID);
                }
            }
        }
        else
        {
            player.ChangeTeam(CsTeam.Spectator);
        }

        return HookResult.Continue;
    }
    public void ChooiseSwitch (CCSPlayerController? player, CommandInfo info)
    {
        if (currentState != MatchState.WaitingChoice) return;
        if (player == null || !player.IsValid) return;

        if (player.TeamNum != knifeWinnerTeam || !TeamLeaders.Contains(player))
        {
            player.PrintToCenter(Localizer["DontHavePermission"]);
            return;
        }
        IsStay = false;

        SwitchOrStay();

        return;
    }
    public void ChooiseStay (CCSPlayerController? player, CommandInfo info)
    {
        if (currentState != MatchState.WaitingChoice) return;
        if (player == null || !player.IsValid) return;

        if (player.TeamNum != knifeWinnerTeam || !TeamLeaders.Contains(player))
        {
            player.PrintToCenter(Localizer["DontHavePermission"]);
            return;
        }
        IsStay = true;

        SwitchOrStay();

        return;
    }
    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_startmatch", "Запускает матч")]
    public void StartMatch(CCSPlayerController player, CommandInfo info)
    {
        if (currentState == MatchState.NotStarted)
        {
            ApplyMatchSetting();
        }
        else
        {
            PrintNotificationMessage(player, "Матч уже запущен!\nВы можете его перезапустить\nС помощью css_restartmatch", "Матч уже запущен!\nВы можете его перезапустить\nС помощью !restartmatch");
        }
        return;
    }
    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_restartmatch", "Перезапускает матч")]
    public void RestartMatch(CCSPlayerController player, CommandInfo info)
    {
        if (currentState != MatchState.NotStarted)
        {
            ApplyMatchSetting();
        }
        else
        {
            PrintNotificationMessage(player, "На данный момент матч не запущен\nВы можете его запустить\nС помощью css_startmatch", "На данный момент матч не запущен\nВы можете его запустить\nС помощью !startmatch");
        }
        return;
    }
    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_addleader", "Добавляет лидера")]
    public void AddNewLeader(CCSPlayerController player, CommandInfo info)
    {
        if (info.ArgCount == 1 || info.ArgCount >= 3) 
        {
            PrintNotificationMessage(player, "Используйте css_addleader <steamid64>", "Используйте !addleader <steamid64>");
            return;
        };

        string steamidleader = info.GetArg(1);
        if (!ulong.TryParse(steamidleader, out ulong steamidnew))
        {
            PrintNotificationMessage(player, "Вы ввели неккоректное значение SteamID64", "Вы ввели неккоректное значение SteamID64");
        }
        else
        {
            var leader = Utilities.GetPlayerFromSteamId64(steamidnew);
            if (leader == null || !leader.IsValid)
            {
                PrintNotificationMessage(player, "Такой игрок не был найден!", "Такой игрок не был найден!");
                return;
            }
            if (TeamLeaders.Add(leader))
            {
                PrintNotificationMessage(player, "Лидер успешно добавлен!", "Лидер успешно добавлен!");
                leader.PrintToChat(Localizer["Prefix"] + Localizer["WelcomeLeader"]);
            }
            else
            {
                PrintNotificationMessage(player, "Лидер уже был добавлен ранее!", "Лидер уже был добавлен ранее!");
            }
        }
    }
    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_delleader", "Удаляет лидера")]
    public void DeleteLeader(CCSPlayerController player, CommandInfo info)
    {
        if (info.ArgCount == 1 || info.ArgCount >= 3) 
        {
            PrintNotificationMessage(player, "Используйте css_delleader <steamid64>", "Используйте !delleader <steamid64>");
            return;
        };
        string steamidleader = info.GetArg(1);
        if (!ulong.TryParse(steamidleader, out ulong steamidnew))
        {
            PrintNotificationMessage(player, "Вы ввели неккоректное значение SteamID64", "Вы ввели неккоректное значение SteamID64");
        }
        else
        {
            bool hasLeader = false;

            foreach (var leader in TeamLeaders)
            {
                if (leader.SteamID == steamidnew)
                {
                    removeListPlayer(leader, TeamLeaders);
                    hasLeader = true;
                }
            }
            if (hasLeader)
            {
                PrintNotificationMessage(player, "Лидер успешно удален!", "Лидер успешно удален!");
            }
            else
            {
                PrintNotificationMessage(player, "Лидер не был найден :(", "Лидер не был найден :(");
            }
        }
    }
    private HookResult LockSwitchTeam (CCSPlayerController? player, CommandInfo command)
    {
        if (currentState == MatchState.NotStarted || currentState == MatchState.Warmup)
        {
            return HookResult.Continue;
        }

        return HookResult.Handled;
    }
    private HookResult CheckFullTeam (EventPlayerTeam @event, GameEventInfo info)
    {
        if (currentState != MatchState.AllPlayersReady && currentState != MatchState.Warmup) return HookResult.Continue;

        var player = @event.Userid;
        var numTeam = @event.Team;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
        if (currentState == MatchState.Warmup)
        {
            if (numTeam == 2 || numTeam == 3)
            {
                AllPlayersGame.Add(player);
            }
            else if (numTeam == 1 || numTeam == 0)
            {
                removeListPlayer(player, AllPlayersGame);
                removeListPlayer(player, PlayerReadyList);
            }
            if (currentState == MatchState.Warmup)
            {           
                messageCountPlayerNeed = AddTimer(0.1f, () =>
                {
                    int countCurrentPlayer = AllPlayersGame.Count;
                    if (player == null || !player.IsValid) return;
                    player.PrintToCenterAlert(Localizer["WaitingPlayers", countCurrentPlayer, requiredPlayer]);
                },
                CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT
                );

                TimersPlayers.Add(messageCountPlayerNeed);
            }
            if (AllPlayersGame.Count == requiredPlayer)
            {
                foreach (var t in TimersPlayers)
                {
                    t?.Kill();
                }
                TimersPlayers.Clear();
                Server.PrintToChatAll(Localizer["Prefix"] + Localizer["AllPlayersJoin"]);
                allPlayerJoin = true;
            }
            else if (AllPlayersGame.Count > requiredPlayer)
            {
                AddTimer(0.3f, () =>
                {
                    if (player == null || !player.IsValid || player.IsBot) return;

                    player.ChangeTeam(CsTeam.Spectator);
                    removeListPlayer(player, AllPlayersGame);
                });
            }
            else
            {
                allPlayerJoin = false;
                foreach (var players in AllPlayersGame)
                {   
                    messageCountPlayerNeed = AddTimer(0.1f, () =>
                    {
                        int countCurrentPlayer = AllPlayersGame.Count;
                        if (players == null || !players.IsValid) return;
                        players.PrintToCenterAlert(Localizer["WaitingPlayers", countCurrentPlayer, requiredPlayer]);
                        },
                        CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT
                    );
                    TimersPlayers.Add(messageCountPlayerNeed);
                }
            }
        }
        else
        {
            player.ChangeTeam(CsTeam.Spectator);
        }

        return HookResult.Continue;
    }
    public void ReadyPlayer(CCSPlayerController? player, CommandInfo info)
    {
        if (!allPlayerJoin || currentState != MatchState.Warmup || player == null || !player.IsValid) return;

        if (player.Team == CsTeam.Spectator)
        {
            player.PrintToChat(Localizer["Prefix"] + Localizer["CanUseOnlyPlayerInGame"]);
            return;
        }
        if (!PlayerReadyList.Add(player))
        {
            player.PrintToChat(Localizer["Prefix"] + Localizer["YouHaveReadyStatus"]);
            return;
        }
        int countPlayerReady = PlayerReadyList.Count;
        Server.PrintToChatAll(Localizer["Prefix"] + Localizer["PlayerSendReady", player.PlayerName, countPlayerReady, requiredPlayer]);
        if (PlayerReadyList.Count == requiredPlayer)
        {
            Server.ExecuteCommand("mp_warmup_pausetimer 0");
            Server.PrintToChatAll(Localizer["Prefix"] + Localizer["AllPlayersReady"]);

            foreach (var playerok in PlayerReadyList)
            {
                if (playerok.Team == CsTeam.CounterTerrorist)
                {
                    FirstTeam.Add(playerok);
                }
                else
                {
                    SecondTeam.Add(playerok);
                }
            }

            PlayerReadyList.Clear();
            Server.ExecuteCommand("mp_weapons_allow_typecount 2");
            currentState = MatchState.AllPlayersReady;
        }
        return;
    }
    public void UnReadyPlayer(CCSPlayerController? player, CommandInfo info)
    {
        if (!allPlayerJoin || currentState != MatchState.Warmup || player == null || !player.IsValid) return;
        
        if (player.Team == CsTeam.Spectator)
        {
            player.PrintToChat(Localizer["Prefix"] + Localizer["CanUseOnlyPlayerInGame"]);
            return;
        }
        if (PlayerReadyList.Remove(player))
        {
            int AllPlayersGames = PlayerReadyList.Count;
            Server.PrintToChatAll(Localizer["Prefix"] + Localizer["UnReadyPlayer", player.PlayerName, AllPlayersGames, requiredPlayer]);
        }
        else
        {
            player.PrintToChat(Localizer["Prefix"] + Localizer["YouNotHaveReadyStatus"]);
        }

        return;
    }
    private HookResult KnifeRoundStart (EventRoundStart @event, GameEventInfo info)
    {
        if (currentState == MatchState.AllPlayersReady)
        {
            currentState = MatchState.KnifeRound;
        }
        else
        {
            return HookResult.Continue;
        }
        
        foreach (var player in AllPlayersGame)
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive) continue;
            player.RemoveWeapons();
            player.GiveNamedItem("weapon_knife");
            player.GiveNamedItem("item_assaultsuit");

            if (player.InGameMoneyServices == null) continue;
            player.InGameMoneyServices.Account = 0;
        }

        return HookResult.Continue;
    }
    private HookResult KnifeRoundEnd (EventRoundEnd @event, GameEventInfo eventInfo)
    {
        if (currentState != MatchState.KnifeRound) return HookResult.Continue;
        knifeWinnerTeam = @event.Winner;
        Server.ExecuteCommand("mp_warmuptime 60");
        Server.ExecuteCommand("mp_warmup_start");
        Server.NextFrame(() =>
        {
            currentState = MatchState.WaitingChoice;
        });
        
        SetNewLeaderIfListEmpty();
        
        AddTimer(1.0f, () =>
        {
            foreach (var player in AllPlayersGame)
            {
                if (TeamLeaders.Contains(player) && player.TeamNum == knifeWinnerTeam)
                {
                    player.PrintToChat(Localizer["LeaderMessageAboutStaySwitch"]);
                }
                else
                {
                    endMessage = AddTimer(0.1f, () =>
                    {
                        if (player == null || !player.IsValid) return;
                        player.PrintToCenterAlert(Localizer["WaitingLeader"]);
                    },
                    CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT
                    );
                }
            }
        });

        return HookResult.Continue;
    }
    public void PrintNotificationMessage (CCSPlayerController player, string messageServer, string messagePlayer)
    {
        if (player == null)
        {
            Console.WriteLine($"{messageServer}");
        }
        else if (player != null && player.IsValid)
        {
            player.PrintToChat($"{messagePlayer}");
        }
    }
    public void removeListPlayer (CCSPlayerController player, HashSet<CCSPlayerController> list)
    {
        if (list.Contains(player))
        {
            list.Remove(player);
        }
    }
    public void ApplyMatchSetting ()
    {
        foreach (var playerMove in Utilities.GetPlayers())
        {
            if (playerMove.Team == CsTeam.Spectator) continue;
            playerMove.ChangeTeam(CsTeam.Spectator);
        }
        AllPlayersGame.Clear();
        PlayerReadyList.Clear();
        TeamLeaders.Clear();
        FirstTeam.Clear();
        SecondTeam.Clear();
        endMessage?.Kill();
        TimeOutsMessage?.Kill();
        foreach (var timer in TimersPlayers)
        {
            timer?.Kill();
        }
        TimersPlayers.Clear();
        
        Server.ExecuteCommand("exec gamemode_competitive");
        Server.ExecuteCommand("mp_warmup_start"); 
    
        Server.NextFrame(() =>
        {
            Server.ExecuteCommand("mp_warmuptime 30");
            Server.ExecuteCommand("mp_warmup_pausetimer 1");
            Server.ExecuteCommand("mp_spectators_max 32");
            Server.ExecuteCommand("mp_autokick 0");
            Server.ExecuteCommand("mp_weapons_allow_typecount 500");
            Server.ExecuteCommand("mp_teamname_1 \"\"");
            Server.ExecuteCommand("mp_teamname_2 \"\"");
            Server.ExecuteCommand("sv_allow_votes 0");
            
            if (!Config.EnableDrawOrNot)
            {
                Server.ExecuteCommand("mp_overtime_enable 1");
                Server.ExecuteCommand("mp_overtime_maxrounds 6");
                Server.ExecuteCommand("mp_overtime_startmoney 10000");
            }
            if (Config.TypeFriendlyFire == "All" || Config.TypeFriendlyFire == "Faceit")
            {
                Server.ExecuteCommand("mp_friendlyfire 1");
            }
            else
            {
                Server.ExecuteCommand("mp_friendlyfire 0");
            }

        });

        currentState = MatchState.Warmup;
    }
    public void SwitchOrStay ()
    {
        currentState = MatchState.MainMatch;

        endMessage?.Kill();
        if (!IsStay)
        {
            (FirstTeam, SecondTeam) = (SecondTeam, FirstTeam);
            Server.ExecuteCommand("mp_warmup_end");
            Server.ExecuteCommand("mp_swapteams");
        }
        else
        {
            Server.ExecuteCommand("mp_warmup_end");
            Server.ExecuteCommand("mp_restartgame 1");
        }
    }
    public void PlayerMoveTeamAfterDisconnect (CCSPlayerController player, HashSet<CCSPlayerController> listPlayers)
    {
        CsTeam teamNum = CsTeam.None;

        foreach (var players in listPlayers)
        {
            if (players != null && !players.IsValid && players.Connected == PlayerConnectedState.PlayerConnected)
            {
                teamNum = players.Team;
                break;
            }
        }

        player.ChangeTeam(teamNum);
    }
    public void SetNewLeaderIfListEmpty ()
    {
        bool haveLeaderFirstTeam = false, haveLeaderSecondTeam = false;

        foreach (var player in TeamLeaders)
        {
            if (TeamLeaders.Contains(player))
            {
                if (player.Team == CsTeam.Terrorist)
                {
                    haveLeaderSecondTeam = true;
                }
                else if (player.Team == CsTeam.CounterTerrorist)
                {
                    haveLeaderFirstTeam = true;
                }
            }
        }

        if (!haveLeaderFirstTeam)
        {
            int randomIndexPlayerFirst = Random.Shared.Next(0, FirstTeam.Count - 1);
            Server.ExecuteCommand($"css_addleader {FirstTeam.ElementAt(randomIndexPlayerFirst).SteamID}");
        }
        if (!haveLeaderSecondTeam)
        {
            int randomIndexPlayerSecond = Random.Shared.Next(0, SecondTeam.Count - 1);
            Server.ExecuteCommand($"css_addleader {SecondTeam.ElementAt(randomIndexPlayerSecond).SteamID}");
        }
    }
    public void SetPause ()
    {
        currentState = MatchState.Timeout;
        Server.ExecuteCommand("mp_pause_match 1");
    }
    public void SetUnPause ()
    {
        currentState = MatchState.MainMatch;
        Server.ExecuteCommand("mp_unpause_match 1");
    }
    public void CleanAllWeaponInWarmup()
    {
        foreach (var weapon in Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>("weapon"))
        {
            if (weapon == null || !weapon.IsValid) continue;
            if (weapon.Entity == null) continue;
            if (!weapon.DesignerName.StartsWith("weapon_")) continue;
            if (weapon.OwnerEntity != null && weapon.OwnerEntity.IsValid) continue;

            weapon.Remove();
        }
    }
}

public class StatsDamage
{
    public string OpponentName {get; set;} = "";
    public int DamageDealt {get; set;} = 0;
    public int HitsDealt {get; set;} = 0;
    public int DamageReceived {get; set;} = 0;
    public int HitsReceived {get; set;} = 0;
    public int CurrentHp {get; set;} = 100;
}

public class MatchMakeConfig : BasePluginConfig
{
    [JsonPropertyName("PlayersForStart")]
    public int NeedPlayer {get; set;} = 10;

    [JsonPropertyName("EnableMessageAboutDamage")]
    public bool EnableMessageOrNot {get; set;} = true;

    [JsonPropertyName("CanDraw")]
    public bool EnableDrawOrNot {get; set;} = false;

    [JsonPropertyName("TypeFriendlyFire")]
    public string TypeFriendlyFire {get; set;} = "Off/Faceit/All";

    [JsonPropertyName("CountPauses")]
    public int CountTimersAll {get; set;} = 3;

    [JsonPropertyName("PauseTime")]
    public int TimeTimersSecond {get; set;} = 30;

    [JsonPropertyName("EnableAutoStart")]
    public bool EnableAutoStart {get; set;} = true;

    [JsonPropertyName("AutoReplacePlayers")]
    public bool AutoReplacePlayers {get; set;} = true;
}