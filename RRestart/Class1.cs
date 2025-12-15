using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LabApi.Events;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using PlayerRoles;

namespace IOPRestart
{
    public class IOPRestart : Plugin<IOPRestart>
    {
        private CancellationTokenSource _cts;

        private Task _loopTask;

        private readonly TimeSpan _tick = TimeSpan.FromSeconds(1.0);

        private readonly TimeSpan _endCooldown = TimeSpan.FromSeconds(6.0);

        private DateTime _lastEndAttempt = DateTime.MinValue;

        private bool _endingInProgress;

        private bool _isRoundActive;

        private DateTime _roundStartTime = DateTime.MinValue;

        private readonly TimeSpan _gracePeriod = TimeSpan.FromSeconds(15.0);

        private const int MinPlayersToEnd = 2;

        public override string Name => "RRestart";

        public override string Author => "adasjusk";

        public override Version Version => new Version(1, 0, 6);

        public override Version RequiredApiVersion => new Version(1, 1, 4);

        public override string Description => "Ends the round when only one class or one allied faction remains.";

        public override void Enable()
        {
            //IL_0007: Unknown result type (might be due to invalid IL or missing references)
            //IL_0011: Expected O, but got Unknown
            //IL_0018: Unknown result type (might be due to invalid IL or missing references)
            //IL_0022: Expected O, but got Unknown
            ServerEvents.WaitingForPlayers += new LabEventHandler(OnWaitingForPlayers);
            ServerEvents.RoundStarted += new LabEventHandler(OnRoundStarted);
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => MonitorLoop(_cts.Token), _cts.Token);
            Console.WriteLine($"{((Plugin)this).Name} v{((Plugin)this).Version} enabled. Polling every {_tick.TotalSeconds}s.");
        }

        public override void Disable()
        {
            //IL_0007: Unknown result type (might be due to invalid IL or missing references)
            //IL_0011: Expected O, but got Unknown
            //IL_0018: Unknown result type (might be due to invalid IL or missing references)
            //IL_0022: Expected O, but got Unknown
            ServerEvents.WaitingForPlayers -= new LabEventHandler(OnWaitingForPlayers);
            ServerEvents.RoundStarted -= new LabEventHandler(OnRoundStarted);
            try
            {
                _cts?.Cancel();
                _loopTask?.Wait(1500);
            }
            catch
            {
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _loopTask = null;
            }
            Console.WriteLine(((Plugin)this).Name + " disabled.");
        }

        private void OnWaitingForPlayers()
        {
            _isRoundActive = false;
        }

        private void OnRoundStarted()
        {
            _isRoundActive = true;
            _roundStartTime = DateTime.UtcNow;
        }

        private async Task MonitorLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_isRoundActive)
                    {
                        CheckAndEndIfNeeded();
                    }
                }
                catch (Exception arg)
                {
                    Console.WriteLine($"[{((Plugin)this).Name}] MonitorLoop error: {arg}");
                }
                try
                {
                    await Task.Delay(_tick, ct);
                }
                catch
                {
                    break;
                }
            }
        }

        private void CheckAndEndIfNeeded()
        {
            //IL_00c9: Unknown result type (might be due to invalid IL or missing references)
            //IL_00ce: Unknown result type (might be due to invalid IL or missing references)
            if (_endingInProgress || DateTime.UtcNow - _lastEndAttempt < _endCooldown || DateTime.UtcNow - _roundStartTime < _gracePeriod)
            {
                return;
            }
            List<Player> list = Player.List.ToList();
            if (list.Count < 2)
            {
                return;
            }
            List<Player> list2 = list.Where((Player p) => p != null && p.IsAlive && (int)p.Role != -1 && p.Role != RoleTypeId.Spectator).ToList();
            List<RoleTypeId> list3 = list2.Select((Player p) => p.Role).Distinct().ToList();
            if (list3.Count == 1 && list2.Count > 0)
            {
                string text = list3[0].ToString();
                Console.WriteLine($"[{((Plugin)this).Name}] single class remains ({text}) with {list2.Count} players -> attempting end.");
                TryEndRound(text);
                return;
            }
            List<string> list4 = list2.Select((Player p) => MapFaction(p.Role)).Distinct().ToList();
            if (list4.Count == 1 && list2.Count > 0)
            {
                Console.WriteLine($"[{((Plugin)this).Name}] only faction '{list4[0]}' remains with {list2.Count} players -> attempting end.");
                TryEndRound(list4[0]);
            }
        }

        private string MapFaction(RoleTypeId role)
        {
            string text = role.ToString().ToLowerInvariant();
            if (text.StartsWith("scp") || text.Contains("scp"))
            {
                return "SCP";
            }
            switch (role)
            {
                case RoleTypeId.ChaosConscript:
                    return "Chaos";
                case RoleTypeId.ChaosMarauder:
                case RoleTypeId.ChaosRepressor:
                case RoleTypeId.ChaosRifleman:
                    return "Chaos";
                case RoleTypeId.FacilityGuard:
                case RoleTypeId.NtfCaptain:
                case RoleTypeId.NtfPrivate:
                case RoleTypeId.NtfSergeant:
                case RoleTypeId.NtfSpecialist:
                case RoleTypeId.Scientist:
                    return "Foundation";
                default:
                    if (text.Contains("guard") || text.Contains("mtf") || text.Contains("ntf"))
                    {
                        return "Foundation";
                    }
                    return "Other";
            }
        }

        public void ForceEndManual(string factionOrClass = null)
        {
            TryEndRound(string.IsNullOrEmpty(factionOrClass) ? "manual" : factionOrClass);
        }

        private void TryEndRound(string reason)
        {
            _lastEndAttempt = DateTime.UtcNow;
            _endingInProgress = true;
            try
            {
                if (EndRoundWithTeamEnum(reason))
                {
                    Console.WriteLine("[" + ((Plugin)this).Name + "] Round end invoked with team enum (" + reason + ").");
                }
                else if (EndRoundViaReflection())
                {
                    Console.WriteLine("[" + ((Plugin)this).Name + "] Round end invoked (fallback parameterless) (" + reason + ").");
                }
                else if (EndRoundBruteForce())
                {
                    Console.WriteLine("[" + ((Plugin)this).Name + "] Round end invoked (fallback brute) (" + reason + ").");
                }
                else
                {
                    Console.WriteLine("[" + ((Plugin)this).Name + "] could not find a round-end API");
                }
            }
            catch (Exception arg)
            {
                Console.WriteLine($"[{((Plugin)this).Name}] TryEndRound exception: {arg}");
            }
            finally
            {
                _endingInProgress = false;
            }
        }

        private bool EndRoundWithTeamEnum(string faction)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }
                Type[] array = types;
                foreach (Type type in array)
                {
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);
                    }
                    catch
                    {
                        continue;
                    }
                    MethodInfo[] array2 = methods;
                    foreach (MethodInfo methodInfo in array2)
                    {
                        if (!string.Equals(methodInfo.Name, "EndRound", StringComparison.OrdinalIgnoreCase) && !string.Equals(methodInfo.Name, "ForceEnd", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        ParameterInfo[] parameters = methodInfo.GetParameters();
                        if (parameters.Length != 1)
                        {
                            continue;
                        }
                        Type parameterType = parameters[0].ParameterType;
                        if (!parameterType.IsEnum)
                        {
                            continue;
                        }
                        string text = MapFactionToEnumName(parameterType, faction);
                        if (text == null)
                        {
                            continue;
                        }
                        try
                        {
                            object obj3 = Enum.Parse(parameterType, text, ignoreCase: true);
                            if (methodInfo.IsStatic)
                            {
                                methodInfo.Invoke(null, new object[1] { obj3 });
                                return true;
                            }
                            object obj4 = TryGetSingletonInstanceOfType(type);
                            if (obj4 != null)
                            {
                                methodInfo.Invoke(obj4, new object[1] { obj3 });
                                return true;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            return false;
        }

        private string MapFactionToEnumName(Type enumType, string faction)
        {
            string[] array = (from n in enumType.GetEnumNames()
                              select n.ToLowerInvariant()).ToArray();
            faction = (faction ?? "").ToLowerInvariant();
            if (faction.Contains("scp"))
            {
                string pick = array.FirstOrDefault((string n) => n.Contains("anom") || n.Contains("scp") || n.Contains("anomal"));
                if (pick != null)
                {
                    return enumType.GetEnumNames().First((string n) => n.ToLowerInvariant() == pick);
                }
            }
            if (faction.Contains("foundation") || faction.Contains("facility") || faction.Contains("mtf") || faction.Contains("ntf"))
            {
                string pick2 = array.FirstOrDefault((string n) => n.Contains("facility") || n.Contains("facilityfor") || n.Contains("facility_forces"));
                if (pick2 != null)
                {
                    return enumType.GetEnumNames().First((string n) => n.ToLowerInvariant() == pick2);
                }
            }
            if (faction.Contains("chaos"))
            {
                string pick3 = array.FirstOrDefault((string n) => n.Contains("chaos") || n.Contains("insurg"));
                if (pick3 != null)
                {
                    return enumType.GetEnumNames().First((string n) => n.ToLowerInvariant() == pick3);
                }
            }
            string drawPick = array.FirstOrDefault((string n) => n.Contains("draw"));
            if (drawPick != null)
            {
                return enumType.GetEnumNames().First((string n) => n.ToLowerInvariant() == drawPick);
            }
            if (array.Length != 0)
            {
                return enumType.GetEnumNames()[0];
            }
            return null;
        }

        private object TryGetSingletonInstanceOfType(Type t)
        {
            FieldInfo fieldInfo = t.GetField("singleton", BindingFlags.Static | BindingFlags.Public) ?? t.GetField("instance", BindingFlags.Static | BindingFlags.Public) ?? t.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
            if (fieldInfo != null)
            {
                return fieldInfo.GetValue(null);
            }
            PropertyInfo propertyInfo = t.GetProperty("singleton", BindingFlags.Static | BindingFlags.Public) ?? t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public) ?? t.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
            if (propertyInfo != null)
            {
                return propertyInfo.GetValue(null);
            }
            return null;
        }

        private bool EndRoundViaReflection()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }
                Type[] array = types;
                foreach (Type type in array)
                {
                    if (!string.Equals(type.Name, "RoundSummary", StringComparison.OrdinalIgnoreCase) && !string.Equals(type.Name, "Round", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    MethodInfo method = type.GetMethod("ForceEnd", BindingFlags.Static | BindingFlags.Public);
                    if (method != null && method.GetParameters().Length == 0)
                    {
                        method.Invoke(null, null);
                        return true;
                    }
                    MethodInfo method2 = type.GetMethod("EndRound", BindingFlags.Static | BindingFlags.Public);
                    if (method2 != null && method2.GetParameters().Length == 0)
                    {
                        method2.Invoke(null, null);
                        return true;
                    }
                    object obj2 = TryGetSingletonInstanceOfType(type);
                    if (obj2 != null)
                    {
                        MethodInfo method3 = type.GetMethod("ForceEnd", BindingFlags.Instance | BindingFlags.Public);
                        if (method3 != null && method3.GetParameters().Length == 0)
                        {
                            method3.Invoke(obj2, null);
                            return true;
                        }
                        MethodInfo method4 = type.GetMethod("EndRound", BindingFlags.Instance | BindingFlags.Public);
                        if (method4 != null && method4.GetParameters().Length == 0)
                        {
                            method4.Invoke(obj2, null);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool EndRoundBruteForce()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                MethodInfo[] array;
                try
                {
                    array = assembly.GetTypes().SelectMany((Type t) => t.GetMethods(BindingFlags.Static | BindingFlags.Public)).ToArray();
                }
                catch
                {
                    continue;
                }
                MethodInfo[] array2 = array;
                foreach (MethodInfo methodInfo in array2)
                {
                    if (string.Equals(methodInfo.Name, "ForceEnd", StringComparison.Ordinal) && methodInfo.GetParameters().Length == 0)
                    {
                        try
                        {
                            methodInfo.Invoke(null, null);
                            return true;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            return false;
        }
    }
}
